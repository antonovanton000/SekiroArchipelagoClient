using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SekiroAPClient.Classes;
using SekiroAPClient.Models;
using SekiroAPClient.Properties;
using SekiroAPClient.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SekiroAPClient.ViewModels;

public partial class RoomViewModel : MyBaseViewModel
{
    #region Constructors
    public RoomViewModel()
    {
        CurrentSession = default!;
        RandomizerHelper = new();
        pipeServer = App.PipeServer;
        stateStorage = new StateStorage<ApRandomizationState>(Path.Combine(App.Location, "ap_randomization_state.json"));
        itemTransferLogger = CreateItemTransferLogger();
        pendingLocationChecks = CreatePendingLocationCheckStore();
        hintItemNames = LoadHintItemNames();
        UpdateGameLaunchUi();

    }

    public RoomViewModel(ArchipelagoSession session)
    {
        CurrentSession = session;
        RandomizerHelper = new();
        pipeServer = App.PipeServer;
        stateStorage = new StateStorage<ApRandomizationState>(Path.Combine(App.Location, "ap_randomization_state.json"));
        itemTransferLogger = CreateItemTransferLogger();
        pendingLocationChecks = CreatePendingLocationCheckStore();
        hintItemNames = LoadHintItemNames();
        UpdateGameLaunchUi();
    }
    #endregion

    #region Fields and Properties
    
    private OverlayWindow? overlayWindow = null;
    bool isGoBackEnabled = false;
    bool isClosingEnabled = false;
    PipeServer pipeServer;
    public RandomizerHelper RandomizerHelper { get; set; }

    [ObservableProperty]
    KeyItemTracker keyItemTracker = new();

    [ObservableProperty]
    string logText = "";

    [ObservableProperty]
    string serverCommand = "";

    [ObservableProperty]
    ArchipelagoSession currentSession;

    StateStorage<ApRandomizationState> stateStorage;

    [ObservableProperty]
    ApRandomizationState? state;

    [ObservableProperty]
    bool isRandomizing = true;

    [ObservableProperty]
    bool hasErrors;

    [ObservableProperty]
    bool isConnectedToGame;

    [ObservableProperty]
    bool showGameActionButton = true;

    [ObservableProperty]
    bool showGameConnectionStatus;

    [ObservableProperty]
    string gameConnectionStatusText = "";

    [ObservableProperty]
    string gameActionButtonText = "Launch Game";

    [ObservableProperty]
    bool showNotifications = true;

    public ObservableCollection<ServerNotification> Notifications { get; }
        = new ObservableCollection<ServerNotification>();

    public ObservableCollection<string> HintSuggestions { get; } = [];

    [ObservableProperty]
    string? selectedHintSuggestion;

    [ObservableProperty]
    bool isHintAutocompleteOpen;

    readonly List<string> hintItemNames;

    Dictionary<long, int> ApIdsToItemIds = new();

    DeathLinkService? deathLinkService;

    public bool IsDeveloperMode => App.IsDeveloperMode;

    private CancellationTokenSource? _reconnectCts;
    private readonly CancellationTokenSource _receivedItemsCts = new();
    private readonly SemaphoreSlim _receivedItemsLock = new(1, 1);
    private static int _nextRoomViewModelId;
    private static int _activeRoomViewModelId;
    private readonly int _roomViewModelId = Interlocked.Increment(ref _nextRoomViewModelId);
    private bool _eventHandlersAdded;
    private bool _receivedItemProcessingReady;
    bool connectedToArchipelagoHintQueued;

    [ObservableProperty]
    bool isReconnecting = false;

    ReceivedItemStore localItemsStore = null!;
    PendingLocationCheckStore pendingLocationChecks;
    ItemTransferLogger itemTransferLogger;

    List<string> consoleCommands = [];

    int lastSentCommandIndex = 0;
    private static string LocalItemsStorePath
    => Path.Combine(App.Location, "randomizer\\localItemsStore.json");

    private static string KeyItemTrackerStatePath
        => Path.Combine(App.Location, "randomizer\\keyitemtracker_state.json");

    private static string PendingLocationChecksPath
        => Path.Combine(App.Location, "randomizer\\pending_location_checks.json");

    private static ReceivedItemStore CreateLocalItemsStore()
        => new(LocalItemsStorePath);

    private static PendingLocationCheckStore CreatePendingLocationCheckStore()
        => new(PendingLocationChecksPath);

    private ItemTransferLogger CreateItemTransferLogger()
        => new(Path.Combine(App.Location, "ap_item_transfer_log.txt"), () => IsDeveloperMode);

    private bool IsActiveRoomViewModel => Volatile.Read(ref _activeRoomViewModelId) == _roomViewModelId;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OverlayButtonText))]
    bool isExternalWindowOpened = false;
    
    public string OverlayButtonText => IsExternalWindowOpened ? "Close Overlay" : "Show Overlay";

    #endregion

    #region Appearing

    [RelayCommand]
    async Task Appearing()
    {
        Volatile.Write(ref _activeRoomViewModelId, _roomViewModelId);
        _receivedItemProcessingReady = false;
        MainWindow.HideTopButtons();
        ShowNotifications = Settings.Default.ShowNotifications;
        itemTransferLogger.Log($"SESSION START server={CurrentSession.Socket.Uri} slot={CurrentSession.ConnectionInfo.Slot} player={CurrentSession.Players.ActivePlayer.Name}");
        AddEventHandlers();
        ResetKeyItemTracker(false);
        IsConnectedToGame = pipeServer.IsConnected;
        await LoadApItemMappingAsync(CurrentSession);
        LogText += $"Successfully connected to {CurrentSession.Socket.Uri.Authority}\r\n";

        ApRandomizationState? savedState = await TryLoadExistingRandomization(CurrentSession);
        if (savedState == null)
        {
            DeleteLocalItemsStoreForNewRoom();
            DeleteKeyItemTrackerStateForNewRoom();
            DeletePendingLocationChecksForNewRoom();
        }

        localItemsStore = CreateLocalItemsStore();

        if (savedState != null)
        {
            State = savedState;
            ResetKeyItemTracker(State.RoomRandomizerOptions.AdditionalRegionLock);
            IsRandomizing = false;
            await LoadKeyItemTrackerStateAsync();
            if (ShowNotifications)
            {
                await PushNotificationAsync(new ServerNotification()
                {
                    IsItemNotification = false,
                    Text = "Successfully connected to Server!"
                });
            }
        }
        else
        {
            await Randomize();
        }
        if (State != null)
        {
            if (pipeServer.IsConnected)
            {
                LogText += $"Successfully connected to Game!\r\n";
                if (IsDeveloperMode)
                {
                    pipeServer.ChangeDebugState(true);
                    await Task.Delay(100);
                }
                pipeServer.ChangeFullDeathDetection(State.RoomRandomizerOptions.DeathLinkOnFullDeath);
            }

            if (State.RoomRandomizerOptions.DeathLink)
            {
                deathLinkService = CurrentSession.CreateDeathLinkService();
                deathLinkService.EnableDeathLink();
                deathLinkService.OnDeathLinkReceived += DeathLinkService_OnDeathLinkReceived;
            }
            await ShowConnectedToArchipelagoHintAsync();
            _receivedItemProcessingReady = true;
            if (CurrentSession.Items.Any())
            {
                await RecieveItems(CurrentSession.Items, _receivedItemsCts.Token);
            }
            await FlushPendingLocationChecksAsync();
        }

    }

    #endregion

    #region Commands
    [RelayCommand]
    void GoToDebugPage()
    {
        if (!App.IsDeveloperMode)
            return;

        MainWindow.NavigateTo(new DebugPage() { DataContext = new DebugViewModel() });
    }

    [RelayCommand]
    void SendCommand()
    {
        if (CurrentSession != null && !string.IsNullOrEmpty(ServerCommand))
        {
            IsHintAutocompleteOpen = false;
            CurrentSession.Say(ServerCommand);
            consoleCommands.Add(ServerCommand);
            lastSentCommandIndex = consoleCommands.Count;
            ServerCommand = "";
        }
    }

    [RelayCommand]
    void SetDefaultCommand(string command)
    {
        if (!string.IsNullOrEmpty(command))
            ServerCommand = command;
    }

    public void SelectPreviousHintSuggestion()
    {
        if (!IsHintAutocompleteOpen || HintSuggestions.Count == 0)
            return;

        var currentIndex = SelectedHintSuggestion == null
            ? 0
            : HintSuggestions.IndexOf(SelectedHintSuggestion);

        SelectedHintSuggestion = currentIndex <= 0
            ? HintSuggestions[^1]
            : HintSuggestions[currentIndex - 1];
    }

    public void SelectNextHintSuggestion()
    {
        if (!IsHintAutocompleteOpen || HintSuggestions.Count == 0)
            return;

        var currentIndex = SelectedHintSuggestion == null
            ? -1
            : HintSuggestions.IndexOf(SelectedHintSuggestion);

        SelectedHintSuggestion = currentIndex >= HintSuggestions.Count - 1
            ? HintSuggestions[0]
            : HintSuggestions[currentIndex + 1];
    }

    public bool AcceptSelectedHintSuggestion()
    {
        if (!IsHintAutocompleteOpen || string.IsNullOrWhiteSpace(SelectedHintSuggestion))
            return false;

        ServerCommand = $"!hint {SelectedHintSuggestion}";
        IsHintAutocompleteOpen = false;
        return true;
    }

    public void CloseHintAutocomplete()
    {
        IsHintAutocompleteOpen = false;
    }


    [RelayCommand]
    void GetPrevCommand()
    {
        if (consoleCommands.Count == 0)
            return;

        if (lastSentCommandIndex > 0)
            lastSentCommandIndex--;
        else
            return;

        ServerCommand = consoleCommands[lastSentCommandIndex];
    }

    [RelayCommand]
    void GetNextCommand()
    {
        if (consoleCommands.Count == 0)
            return;

        if (lastSentCommandIndex <= consoleCommands.Count - 2)
            lastSentCommandIndex++;
        else
            return;

        ServerCommand = consoleCommands[lastSentCommandIndex];
    }


    [RelayCommand]
    void ShowLogFile()
    {
        if (!string.IsNullOrEmpty(RandomizerHelper.LogFilePath))
            Process.Start(new ProcessStartInfo() { FileName = RandomizerHelper.LogFilePath, UseShellExecute = true });
    }

    [RelayCommand]
    void OpenItemTracker()
    {
        if (!App.IsDeveloperMode || State == null)
            return;

        var window = new ItemTrackerWindow
        {
            Owner = App.Current.MainWindow,
            DataContext = new ItemTrackerViewModel(State, pipeServer, CurrentSession.Players.ActivePlayer.Name)
        };
        window.Show();
    }

    [RelayCommand]
    void OpenDebugPage()
    {
        if (!App.IsDeveloperMode)
            return;

        var newWindow = new DebugWindow();        
        newWindow.Show();
    }

    [RelayCommand]
    void LaunchGame()
    {
        if (pipeServer.IsTcpTransport)
        {
            var window = new SteamLaunchOptionsWindow
            {
                Owner = App.Current.MainWindow
            };
            window.ShowDialog();
            return;
        }

#if DEBUG
        Process.Start(new ProcessStartInfo() { FileName = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Sekiro\\sekiro.exe", WorkingDirectory = Path.Combine(App.Location, "..") });
#else
        Process.Start(new ProcessStartInfo() { FileName = Path.Combine(App.Location, "../sekiro.exe"),  WorkingDirectory = Path.Combine(App.Location, "..")  });
#endif
    }

    [RelayCommand]
    void ChangeNotificationState()
    {
        Settings.Default.ShowNotifications = ShowNotifications;
        Settings.Default.Save();
    }

    [RelayCommand]
    void ReRandomizeRun()
    {
        MainWindow.ShowMessage("Are you sure you want to randomize again?\r\rAll current progress will be lost.", MessageNotificationType.YesNo, async () =>
        {
            await Randomize();
        });
    }

    [RelayCommand]
    void OpenExternalWindow()
    {
        if (!IsExternalWindowOpened)
        {
            IsExternalWindowOpened = true;
            overlayWindow = new OverlayWindow();
            overlayWindow.Closed += (s, e) =>
            {
                overlayWindow = null;
                IsExternalWindowOpened = false;
            };
            overlayWindow.DataContext = this;
            overlayWindow.Topmost = true;
            overlayWindow.Show();
        }
        else
        {
            if (overlayWindow != null)
            {
                overlayWindow.Close();
                overlayWindow = null;
                IsExternalWindowOpened = false;
            }
        }
    }

    #endregion

    #region Hint Autocomplete

    partial void OnServerCommandChanged(string value)
    {
        UpdateHintSuggestions(value);
    }

    static List<string> LoadHintItemNames()
    {
        try
        {
            return SekiroItemRepository.LoadItems()
                .Select(item => item.name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    void UpdateHintSuggestions(string command)
    {
        HintSuggestions.Clear();
        SelectedHintSuggestion = null;

        if (!TryGetHintQuery(command, out var query) || query.Length < 3)
        {
            IsHintAutocompleteOpen = false;
            return;
        }

        if (hintItemNames.Any(name => string.Equals(name, query, StringComparison.OrdinalIgnoreCase)))
        {
            IsHintAutocompleteOpen = false;
            return;
        }

        var matches = hintItemNames
            .Where(name => name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name)
            .Take(10)
            .ToList();

        foreach (var match in matches)
        {
            HintSuggestions.Add(match);
        }

        SelectedHintSuggestion = HintSuggestions.FirstOrDefault();
        IsHintAutocompleteOpen = HintSuggestions.Count > 0;
    }

    static bool TryGetHintQuery(string command, out string query)
    {
        query = string.Empty;

        const string hintCommand = "!hint";
        if (string.IsNullOrWhiteSpace(command) ||
            !command.StartsWith(hintCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (command.Length == hintCommand.Length)
            return true;

        if (!char.IsWhiteSpace(command[hintCommand.Length]))
            return false;

        query = command[hintCommand.Length..].TrimStart();
        return true;
    }

    #endregion

    #region Event Handlers

    private async void PipeServer_ConnectionChanged(string state)
    {
        if (!IsActiveRoomViewModel)
            return;

        if (state == "connected")
        {
            IsConnectedToGame = true;
            LogText += $"Successfully connected to Game!\r\n";
            if (IsDeveloperMode)
            {
                await Task.Delay(500);
                pipeServer.ChangeDebugState(true);
            }
            await Task.Delay(100);
            pipeServer.ChangeFullDeathDetection(State?.RoomRandomizerOptions.DeathLinkOnFullDeath ?? false);
            if (CurrentSession.Socket.Connected)
            {
                await Task.Delay(500);
                await ShowConnectedToArchipelagoHintAsync();
            }
        }
        else if (state == "disconnected")
        {
            IsConnectedToGame = false;
            LogText += $"Disconnected from Game!\r\n";
        }
    }

    partial void OnIsConnectedToGameChanged(bool value)
    {
        UpdateGameLaunchUi();
    }

    private async void Socket_ErrorReceived(Exception e, string message)
    {
        if (!IsActiveRoomViewModel)
            return;

        LogText += $"Connection Error. Error: {message}\r\n";
        await PushNotificationAsync(new ServerNotification()
        {
            IsItemNotification = false,
            Text = "Connection Error!"
        });
    }

    private async void Socket_SocketClosed(string reason)
    {
        if (!IsActiveRoomViewModel)
            return;

        LogText += $"Connection closed. Reason: {reason}\r\n";
        await TryReconnectAgain();
    }

    private async void MessageLog_OnMessageReceived(Archipelago.MultiClient.Net.MessageLog.Messages.LogMessage message)
    {
        if (!IsActiveRoomViewModel)
            return;

        if (message is ItemSendLogMessage itemMessage)
        {            
            if (ShowNotifications)
            {
                if (itemMessage.IsSenderTheActivePlayer || itemMessage.IsReceiverTheActivePlayer)
                {
                    await PushNotificationAsync(new ServerNotification()
                    {
                        Player = itemMessage.Sender.Name,
                        Item = itemMessage.Item.ItemName,
                        Location = itemMessage.Item.LocationName,
                        IsItemNotification = true
                    });
                }
            }
        }
        else if (message is TagsChangedLogMessage logMessage)
        {            
            if (ShowNotifications)
            {
                await PushNotificationAsync(new ServerNotification()
                {
                    Text = logMessage.ToString(),
                    IsItemNotification = false
                });
            }
        }
        LogText += message.ToString() + Environment.NewLine;
    }

    private async void Items_ItemReceived(ReceivedItemsHelper helper)
    {
        if (!IsActiveRoomViewModel || !_receivedItemProcessingReady)
            return;

        await RecieveItems(helper, _receivedItemsCts.Token);
    }

    private async void PipeServer_ItemReceived(ItemRecievedArgs obj)
    {
        if (!IsActiveRoomViewModel)
            return;

        if (CurrentSession != null && State != null)
        {
            var lotMaps = State.ApLotMap.Where(i => (i.LotId == obj.LotId || i.BaseLotId == obj.LotId) && i.IsShop == obj.IsFromShop);
            if (lotMaps.Count() > 0)
            {
                var locationIds = lotMaps.Select(i => i.LocationId).ToArray();
                QueuePendingLocationChecks(locationIds, obj);
                await FlushPendingLocationChecksAsync();

                if (IsSekiroMemoryItem(obj.GoodId))
                {
                    itemTransferLogger.Log($"LOCAL_PICKUP extra_memory_counter good={obj.GoodId} lot={obj.LotId}");
                    pipeServer.SendSpawnItem(0x40000000 + 5400, 1);
                }

                if (IsMechanicalBarrelItem(obj.GoodId))
                {
                    int eventFlagId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(obj.GoodId);
                    await Task.Delay(20);
                    itemTransferLogger.Log($"LOCAL_PICKUP set_permanent_flag good={obj.GoodId} flag={eventFlagId} lot={obj.LotId}");
                    pipeServer.SendSetEventFlagId(eventFlagId, 1);
                }

                if (IsAromaticBranchItem(obj.GoodId))
                {
                    int eventFlagId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(obj.GoodId);
                    await Task.Delay(20);
                    itemTransferLogger.Log($"LOCAL_PICKUP set_permanent_flag good={obj.GoodId} flag={eventFlagId} lot={obj.LotId}");
                    pipeServer.SendSetEventFlagId(eventFlagId, 1);
                }

                if (KeyItemTracker.CheckItem(obj.GoodId))
                {
                    await SaveKeyItemTrackerStateAsync(obj.GoodId);
                }

            }
            else
            {
                LogText += $"[AP][LOT-MAP-MISSING] No AP location for lot={obj.LotId} good={obj.GoodId} shop={obj.IsFromShop}\r\n";
                itemTransferLogger.Log($"SEND_CHECK missing_lot_map lot={obj.LotId} good={obj.GoodId} qty={obj.Quantity} shop={obj.IsFromShop}");
            }
        }
    }

    private async void PipeServer_PlayerDeath(bool isRealDeath)
    {
        if (!IsActiveRoomViewModel)
            return;

        if (State != null)
        {
            try
            {
                await App.Current.Dispatcher.InvokeAsync(() => State.DeathCounter++);
                await stateStorage.SaveAsync(State);
            }
            catch (Exception ex)
            {
                await App.Current.Dispatcher.InvokeAsync(() => LogText += $"[AP] Failed to save death counter: {ex.Message}\r\n");
            }
        }

        deathLinkService?.SendDeathLink(new DeathLink(CurrentSession.Players.ActivePlayer.Name, DeathLinkReasonHelper.GetRandomDeathLinkReason()));
    }

    private async void DeathLinkService_OnDeathLinkReceived(DeathLink deathLink)
    {
        if (!IsActiveRoomViewModel)
            return;

        pipeServer.SendShowHint($"Player: {deathLink.Source}\nCause: {deathLink.Cause}", 15100006);
        await Task.Delay(1000);
        pipeServer.SendKillPlayer();
    }

    private void Frame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == System.Windows.Navigation.NavigationMode.Back)
        {
            e.Cancel = !isGoBackEnabled;
            if (!isGoBackEnabled)
            {
                MainWindow.ShowMessage("Are you sure you want to go back?\r\n\r\nThis will disconnect you from the Archipelago server.", MessageNotificationType.YesNo, async () =>
                {
                    if (overlayWindow != null)
                    {
                        overlayWindow.Close();
                        overlayWindow = null;
                        IsExternalWindowOpened = false;
                    }

                    _receivedItemProcessingReady = false;
                    _receivedItemsCts.Cancel();
                    _reconnectCts?.Cancel();
                    RemoveEventHandlers();
                    if (IsActiveRoomViewModel)
                        Volatile.Write(ref _activeRoomViewModelId, 0);
                    isGoBackEnabled = true;
                    if (CurrentSession.Socket.Connected)
                        await CurrentSession.Socket.DisconnectAsync();
                    pipeServer.SendShowSmallHint("Disconnected from Archipelago!");
                    MainWindow.GoBack();
                }, () =>
                {
                    isGoBackEnabled = false;
                });
            }
        }
    }

    private void RoomViewModel_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !isClosingEnabled;
        if (!isGoBackEnabled)
        {
            MainWindow.ShowMessage("Are you sure you want close this app?\r\n\r\nThis will disconnect you from the Archipelago server.", MessageNotificationType.YesNo, async () =>
            {
                if (overlayWindow != null)
                {
                    overlayWindow.Close();
                    overlayWindow = null;
                    IsExternalWindowOpened = false;
                }
                _receivedItemProcessingReady = false;
                _receivedItemsCts.Cancel();
                _reconnectCts?.Cancel();
                RemoveEventHandlers();
                if (IsActiveRoomViewModel)
                    Volatile.Write(ref _activeRoomViewModelId, 0);
                isClosingEnabled = true;
                pipeServer.SendShowSmallHint("Disconnected from Archipelago!");
                if (CurrentSession.Socket.Connected)
                    await CurrentSession.Socket.DisconnectAsync();
                App.Current.Shutdown();
            }, () =>
            {
                isClosingEnabled = false;
            });
        }
    }

    private void PipeServer_EndingDetected(string endingType)
    {
        if (!IsActiveRoomViewModel)
            return;

        if (string.IsNullOrEmpty(endingType))
            return;

        if (IsEndingMatchingGoal(endingType, State?.RoomRandomizerOptions.GoalOption ?? 0))
        {
            LogText += $"[AP] Goal ending detected: {endingType}. Sending goal achieved.\r\n";
            CurrentSession.SetGoalAchieved();
            return;
        }

        LogText += $"[AP] Ending detected: {endingType}, but current goal is {State?.RoomRandomizerOptions.GoalOptionText ?? "Full Game"}. Goal not sent.\r\n";
    }

    #endregion

    #region Methods

    private void UpdateGameLaunchUi()
    {
        if (pipeServer.IsTcpTransport)
        {
            ShowGameActionButton = true;
            ShowGameConnectionStatus = true;
            GameActionButtonText = "Steam Setup";
            GameConnectionStatusText = IsConnectedToGame ? "Game: Connected" : "Game: Disconnected";
            return;
        }

        ShowGameActionButton = !IsConnectedToGame;
        ShowGameConnectionStatus = IsConnectedToGame;
        GameActionButtonText = "Launch Game";
        GameConnectionStatusText = "Connected to Game";
    }

    void AddEventHandlers()
    {
        if (_eventHandlersAdded)
            return;

        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;
        (App.Current.MainWindow as MainWindow).Closing += RoomViewModel_Closing;
        CurrentSession.MessageLog.OnMessageReceived += MessageLog_OnMessageReceived;
        CurrentSession.Items.ItemReceived += Items_ItemReceived;
        CurrentSession.Socket.SocketClosed += Socket_SocketClosed;
        CurrentSession.Socket.ErrorReceived += Socket_ErrorReceived;
        pipeServer.ItemReceived += PipeServer_ItemReceived;
        pipeServer.ConnectionChanged += PipeServer_ConnectionChanged;
        pipeServer.WorldStateChanged += PipeServer_WorldStateChanged;
        pipeServer.PlayerDeath += PipeServer_PlayerDeath;
        pipeServer.EndingDetected += PipeServer_EndingDetected;
        _eventHandlersAdded = true;
    }

    void RemoveEventHandlers()
    {
        if (!_eventHandlersAdded)
            return;

        (App.Current.MainWindow as MainWindow).frame.Navigating -= Frame_Navigating;
        (App.Current.MainWindow as MainWindow).Closing -= RoomViewModel_Closing;
        CurrentSession.MessageLog.OnMessageReceived -= MessageLog_OnMessageReceived;
        CurrentSession.Items.ItemReceived -= Items_ItemReceived;
        CurrentSession.Socket.SocketClosed -= Socket_SocketClosed;
        CurrentSession.Socket.ErrorReceived -= Socket_ErrorReceived;
        pipeServer.ItemReceived -= PipeServer_ItemReceived;
        pipeServer.ConnectionChanged -= PipeServer_ConnectionChanged;
        pipeServer.WorldStateChanged -= PipeServer_WorldStateChanged;
        pipeServer.PlayerDeath -= PipeServer_PlayerDeath;
        pipeServer.EndingDetected -= PipeServer_EndingDetected;
        if (deathLinkService != null)
        {
            deathLinkService.OnDeathLinkReceived -= DeathLinkService_OnDeathLinkReceived;
            deathLinkService = null;
        }
        _eventHandlersAdded = false;
    }

    async Task Randomize()
    {
        IsRandomizing = true;
        HasErrors = false;
        DeleteKeyItemTrackerStateForNewRoom();
        State = await RandomizerHelper.RandomizeArchipelago(CurrentSession);
        if (State != null)
        {
            ResetKeyItemTracker(State.RoomRandomizerOptions.AdditionalRegionLock);
            IsRandomizing = false;
            await Task.Delay(500);
            await PushNotificationAsync(new ServerNotification()
            {
                IsItemNotification = false,
                Text = "Randomization complete!\nRestart your game and do not close this window!"
            });
        }
        else
        {
            HasErrors = true;
        }
    }

    void ResetKeyItemTracker(bool withApItems)
    {
        KeyItemTracker = new KeyItemTracker();
        KeyItemTracker.InitializeKeyItems(withApItems);
    }

    async Task ShowConnectedToArchipelagoHintAsync(bool force = false)
    {
        if (!force && connectedToArchipelagoHintQueued)
            return;

        if (!force)
            connectedToArchipelagoHintQueued = true;

        await pipeServer.SendShowSmallHintWhenWorldLoaded("Connected to Archipelago!");
    }

    void DeleteLocalItemsStoreForNewRoom()
    {
        try
        {
            if (!File.Exists(LocalItemsStorePath))
                return;

            File.Delete(LocalItemsStorePath);
            itemTransferLogger.Log($"LOCAL_STORE deleted reason=state_mismatch_or_missing path='{LocalItemsStorePath}'");
        }
        catch (Exception ex)
        {
            LogText += $"[AP] Failed to delete local item store for a new room: {ex.Message}\r\n";
            itemTransferLogger.Log($"LOCAL_STORE delete_failed path='{LocalItemsStorePath}' error='{ex.Message}'");
        }
    }

    void DeletePendingLocationChecksForNewRoom()
    {
        try
        {
            if (!File.Exists(PendingLocationChecksPath))
                return;

            File.Delete(PendingLocationChecksPath);
            pendingLocationChecks = CreatePendingLocationCheckStore();
            itemTransferLogger.Log($"LOCATION_CHECK_QUEUE deleted reason=new_room path='{PendingLocationChecksPath}'");
        }
        catch (Exception ex)
        {
            LogText += $"[AP] Failed to delete pending location checks for a new room: {ex.Message}\r\n";
            itemTransferLogger.Log($"LOCATION_CHECK_QUEUE delete_failed path='{PendingLocationChecksPath}' error='{ex.Message}'");
        }
    }

    void QueuePendingLocationChecks(IReadOnlyCollection<long> locationIds, ItemRecievedArgs item)
    {
        if (locationIds.Count == 0)
            return;

        pendingLocationChecks.Add(locationIds, item.LotId, item.GoodId, item.Quantity, item.IsFromShop);
        LogText += $"[AP] Queued location check(s): {string.Join(", ", locationIds)} lot={item.LotId} good={item.GoodId} shop={item.IsFromShop}\r\n";
        itemTransferLogger.Log($"SEND_CHECK queued locations={string.Join(",", locationIds)} lot={item.LotId} good={item.GoodId} qty={item.Quantity} shop={item.IsFromShop}");
    }

    async Task FlushPendingLocationChecksAsync()
    {
        if (CurrentSession?.Socket?.Connected != true)
        {
            var pending = pendingLocationChecks.Snapshot();
            if (pending.Count > 0)
                itemTransferLogger.Log($"SEND_CHECK flush_skipped_no_connection pending={pending.Count}");

            return;
        }

        var records = pendingLocationChecks.Snapshot();
        if (records.Count == 0)
            return;

        var locationIds = records.Select(record => record.LocationId).Distinct().OrderBy(id => id).ToArray();
        LogText += $"[AP] Sending pending location check(s): {string.Join(", ", locationIds)}\r\n";
        itemTransferLogger.Log($"SEND_CHECK flush_start locations={string.Join(",", locationIds)} pending={records.Count}");

        try
        {
            await CurrentSession.Locations.CompleteLocationChecksAsync(locationIds);
            pendingLocationChecks.Remove(locationIds);
            itemTransferLogger.Log($"SEND_CHECK flush_ok locations={string.Join(",", locationIds)}");
        }
        catch (Exception ex)
        {
            itemTransferLogger.Log($"SEND_CHECK flush_error locations={string.Join(",", locationIds)} error={ex.Message}");
            LogText += $"[AP] Failed to send pending location checks. They will be retried after reconnect. Error: {ex.Message}\r\n";
        }
    }

    void DeleteKeyItemTrackerStateForNewRoom()
    {
        try
        {
            if (!File.Exists(KeyItemTrackerStatePath))
                return;

            File.Delete(KeyItemTrackerStatePath);
            itemTransferLogger.Log($"KEY_TRACKER_STATE deleted reason=new_room path='{KeyItemTrackerStatePath}'");
        }
        catch (Exception ex)
        {
            LogText += $"[AP] Failed to delete key item tracker state for a new room: {ex.Message}\r\n";
            itemTransferLogger.Log($"KEY_TRACKER_STATE delete_failed path='{KeyItemTrackerStatePath}' error='{ex.Message}'");
        }
    }

    async Task LoadKeyItemTrackerStateAsync()
    {
        HashSet<int> checkedGoodIds = new();

        try
        {
            if (File.Exists(KeyItemTrackerStatePath))
            {
                var json = await File.ReadAllTextAsync(KeyItemTrackerStatePath);
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<int>>(json) ?? [];
                foreach (var id in ids)
                    checkedGoodIds.Add(NormalizeGoodId(id));
            }
            else if (State != null)
            {
                foreach (var id in State.CheckedKeyItems)
                    checkedGoodIds.Add(NormalizeGoodId(id));
            }
        }
        catch (Exception ex)
        {
            itemTransferLogger.Log($"KEY_TRACKER_STATE load_failed path='{KeyItemTrackerStatePath}' error='{ex.Message}'");
        }

        foreach (var goodId in checkedGoodIds)
            KeyItemTracker.CheckItem(goodId);

        if (checkedGoodIds.Count > 0)
            await SaveKeyItemTrackerStateAsync(checkedGoodIds);
    }

    async Task SaveKeyItemTrackerStateAsync(int goodId)
    {
        await SaveKeyItemTrackerStateAsync(new[] { goodId });
    }

    async Task SaveKeyItemTrackerStateAsync(IEnumerable<int> goodIds)
    {
        if (State == null)
            return;

        var checkedGoodIds = State.CheckedKeyItems
            .Select(NormalizeGoodId)
            .Concat(goodIds.Select(NormalizeGoodId))
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        State.CheckedKeyItems = checkedGoodIds;
        await stateStorage.SaveAsync(State);

        Directory.CreateDirectory(Path.GetDirectoryName(KeyItemTrackerStatePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(checkedGoodIds, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(KeyItemTrackerStatePath, json);
        itemTransferLogger.Log($"KEY_TRACKER_STATE saved count={checkedGoodIds.Count} path='{KeyItemTrackerStatePath}'");
    }

    static int NormalizeGoodId(int id)
    {
        return id >= 0x10000000 ? id & 0x0FFFFFFF : id;
    }

    async Task<ApRandomizationState?> TryLoadExistingRandomization(ArchipelagoSession session)
    {

        var path = Path.Combine(App.Location, "ap_randomization_state.json");
        if (!File.Exists(path))
            return null;

        ApRandomizationState? loaded;
        try
        {
            loaded = await stateStorage.LoadAsync();
        }
        catch
        {
            return null;
        }

        if (loaded == null)
            return null;

        if (!string.Equals(loaded.ServerAddress, session.Socket.Uri.ToString(), StringComparison.Ordinal) ||
            !string.Equals(loaded.SlotName, session.ConnectionInfo.Slot.ToString(), StringComparison.Ordinal) ||
            !string.Equals(loaded.Game, session.ConnectionInfo.Game, StringComparison.Ordinal) ||
            !string.Equals(loaded.Seed, session.RoomState.Seed, StringComparison.Ordinal) ||
            loaded.LocalSeed != (RandomizerHelper.HashStringToInt(session.RoomState.Seed) + session.ConnectionInfo.Slot))
        {
            if (!string.Equals(loaded.ServerAddress, session.Socket.Uri.ToString(), StringComparison.Ordinal) && string.Equals(loaded.Seed, session.RoomState.Seed, StringComparison.Ordinal))
            {
                var userResult = await MainWindow.ShowYesNoMessageAsync("Local Room state with same seed found. \r\nDo you want to save state and connect again?");
                if (!userResult)
                    return null;
            }
            else
                return null;
        }
        return loaded;
    }

    async Task LoadApItemMappingAsync(ArchipelagoSession session)
    {
        var slotData = await session.DataStorage.GetSlotDataAsync();
        ApIdsToItemIds = ((JObject)slotData["apIdsToItemIds"]).ToObject<Dictionary<string, int>>()
            .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
    }

    async Task RecieveItems(IReceivedItemsHelper helper, CancellationToken cancellationToken)
    {
        if (!_receivedItemProcessingReady)
            return;

        try
        {
            await _receivedItemsLock.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested && helper.Any())
            {
                var item = helper.DequeueItem();
                var isCheatConsole = item.LocationName == "Cheat Console";
                var key = ReceivedItemStore.MakeKey(item.ItemId, item.LocationId, item.Player.Slot);
                var alreadyDelivered = !isCheatConsole && localItemsStore.Has(key);

                itemTransferLogger.Log($"RECEIVE dequeued key={key} itemId={item.ItemId} item='{item.ItemName}' locationId={item.LocationId} location='{item.LocationName}' from='{item.Player.Name}' fromSlot={item.Player.Slot} cheat={isCheatConsole} storeDelivered={alreadyDelivered}");

                var gameItemFullId = ApIdsToItemIds.ContainsKey(item.ItemId) ? ApIdsToItemIds[item.ItemId] : -1;
                if (gameItemFullId != -1)
                {
                    var goodId = gameItemFullId & 0x0FFFFFFF;
                    var count = ItemCountParser.GetCountFromItemName(item.ItemName);
                    var itemEventId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(goodId);
                    var isSingleton = SingletonItemPolicy.ShouldClampToOne(goodId);
                    var singletonKey = isSingleton ? MakeSingletonItemKey(goodId, itemEventId) : null;
                    itemTransferLogger.Log($"RECEIVE mapped key={key} fullId={gameItemFullId} good={goodId} qty={count} event={itemEventId} singleton={isSingleton} singletonKey={singletonKey ?? ""}");

                    if (KeyItemTracker.CheckItem(goodId))
                    {
                        await SaveKeyItemTrackerStateAsync(goodId);
                    }

                    if (alreadyDelivered)
                    {
                        itemTransferLogger.Log($"RECEIVE skip_already_delivered key={key} fullId={gameItemFullId} good={goodId}");
                        if (singletonKey != null)
                            localItemsStore.TryMark(singletonKey);                    

                        continue;
                    }


                    if (!isCheatConsole && singletonKey != null && localItemsStore.Has(singletonKey))
                    {
                        LogText += $"[AP] Skipped duplicate singleton item: {item.ItemName} goodId={goodId} eventId={itemEventId} from={item.Player.Name} location={item.LocationName}\r\n";
                        itemTransferLogger.Log($"RECEIVE skip_duplicate_singleton key={key} singletonKey={singletonKey} good={goodId} event={itemEventId}");
                        localItemsStore.MarkDelivered(key);
                        localItemsStore.Save();
                        continue;
                    }

                    if (count > 1 && isSingleton)
                    {
                        LogText += $"[AP] Clamped singleton item quantity to 1: {item.ItemName} goodId={goodId} originalQty={count} from={item.Player.Name} location={item.LocationName}\r\n";
                        itemTransferLogger.Log($"RECEIVE clamp_singleton key={key} good={goodId} originalQty={count}");
                        count = 1;
                    }

                    bool delivered = await SendTrackedItemToGameAsync(gameItemFullId, goodId, count, itemEventId, cancellationToken);
                    if (!delivered)
                    {
                        LogText += $"[AP] Item delivery was not confirmed by the game and will be retried on reconnect: {item.ItemName} from {item.Player.Name} at {item.LocationName}\r\n";
                        itemTransferLogger.Log($"RECEIVE delivery_failed_not_stored key={key} fullId={gameItemFullId} good={goodId} qty={count} event={itemEventId}");
                        continue;
                    }


                    if (!isCheatConsole)
                    {
                        localItemsStore.TryMark(key);
                        if (singletonKey != null)
                            localItemsStore.TryMark(singletonKey);
                        localItemsStore.Save();
                        itemTransferLogger.Log($"RECEIVE stored_delivered key={key} fullId={gameItemFullId} good={goodId} qty={count} event={itemEventId}");
                    }

                }
                else
                {
                    if (alreadyDelivered)
                        continue;

                    LogText += $"[AP][ITEM-MAP-MISSING] Cannot deliver AP Item ID {item.ItemId} ({item.ItemName}) from {item.Player.Name} at {item.LocationName}. Leaving it pending.\r\n";
                    itemTransferLogger.Log($"RECEIVE map_missing key={key} itemId={item.ItemId} item='{item.ItemName}' locationId={item.LocationId} location='{item.LocationName}' from='{item.Player.Name}'");
                }                
                await Task.Yield();
            }
            localItemsStore.Save();
        }
        finally
        {
            _receivedItemsLock.Release();
        }
    }

    private async Task<bool> SendTrackedItemToGameAsync(int fullId, int goodId, int count, int itemEventId, CancellationToken cancellationToken)
    {
        itemTransferLogger.Log($"DELIVER_TO_GAME queue fullId={fullId} good={goodId} qty={count} event={itemEventId} connected={pipeServer.IsConnected} worldLoaded={pipeServer.IsWorldLoaded}");
        bool delivered = await pipeServer.SendSpawnItemReliableAsync(fullId, count, itemEventId, cancellationToken: cancellationToken);
        itemTransferLogger.Log($"DELIVER_TO_GAME ack fullId={fullId} good={goodId} qty={count} event={itemEventId} delivered={delivered}");
        if (!delivered)
            return false;

        if (IsMechanicalBarrelItem(goodId) || IsAromaticBranchItem(goodId))
        {
            int eventFlagId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(goodId);
            itemTransferLogger.Log($"DELIVER_TO_GAME set_permanent_flag good={goodId} flag={eventFlagId}");
            pipeServer.SendSetEventFlagId(eventFlagId, 1);
        }

        if (IsSekiroMemoryItem(goodId))
        {
            itemTransferLogger.Log($"DELIVER_TO_GAME extra_memory_counter good={goodId}");
            pipeServer.SendSpawnItem(0x40000000 + 5400, 1);
        }

        return true;
    }

    private void PipeServer_WorldStateChanged(bool isLoaded)
    {
        if (!IsActiveRoomViewModel)
            return;
    }

    static string MakeSingletonItemKey(int goodId, int itemEventId)
    {
        return itemEventId > 0
            ? $"singleton:event:{itemEventId}"
            : $"singleton:good:{goodId}";
    }

    async Task TryReconnectAgain()
    {
        if (IsReconnecting || !IsActiveRoomViewModel)
            return;

        IsReconnecting = true;
        _reconnectCts = new CancellationTokenSource();
        await pipeServer.ShowConnectedToServerAsync("Connection lost. Attempting to reconnect...");
        var token = _reconnectCts.Token;

        while (!token.IsCancellationRequested && IsActiveRoomViewModel)
        {
            try
            {
                LogText += "Trying to reconnect...";
                var newSession = ArchipelagoSessionFactory.CreateSession(Settings.Default.RoomUrl);

                var roomInfo = await newSession.ConnectAsync();
                if (roomInfo != null)
                {
                    var loginResult = await newSession.LoginAsync(ConnectModel.GameName, Settings.Default.PlayerName, ItemsHandlingFlags.RemoteItems, password: Settings.Default.Password);
                    if (loginResult.Successful)
                    {
                        await App.Current.Dispatcher.Invoke(async () =>
                        {
                            if (!IsActiveRoomViewModel || token.IsCancellationRequested)
                                return;

                            _receivedItemProcessingReady = false;
                            RemoveEventHandlers();
                            CurrentSession = newSession;
                            await LoadApItemMappingAsync(CurrentSession);
                            AddEventHandlers();
                            if (State?.RoomRandomizerOptions.DeathLink == true)
                            {
                                deathLinkService = CurrentSession.CreateDeathLinkService();
                                deathLinkService.EnableDeathLink();
                                deathLinkService.OnDeathLinkReceived += DeathLinkService_OnDeathLinkReceived;
                            }
                            await ShowConnectedToArchipelagoHintAsync(force: true);
                            _receivedItemProcessingReady = true;
                            if (CurrentSession.Items.Any())
                            {
                                await RecieveItems(CurrentSession.Items, _receivedItemsCts.Token);
                            }
                            await FlushPendingLocationChecksAsync();
                        });
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogText += $"[AP] Reconnect exception: {ex.Message}";
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        IsReconnecting = false;
    }

    async Task PushNotificationAsync(ServerNotification notification, int lifetimeMs = 3000)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Notifications.Insert(0, notification);
            while (Notifications.Count > 3)
                Notifications.RemoveAt(Notifications.Count - 1);
        });
        await Task.Delay(lifetimeMs);

        App.Current.Dispatcher.Invoke(() =>
        {
            Notifications.Remove(notification);
        });
    }

    private static bool IsSekiroMemoryItem(int goodId)
    {
        return goodId >= 5200 && goodId <= 5213;
    }

    private static bool IsMechanicalBarrelItem(int goodId)
    {
        return goodId == 2910;
    }

    private static bool IsAromaticBranchItem(int goodId)
    {
        return goodId == 2502;
    }

    private static bool IsEndingMatchingGoal(string endingType, int goalOption)
    {
        return goalOption switch
        {
            1 => string.Equals(endingType, "shura", StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(endingType, "immortal", StringComparison.OrdinalIgnoreCase),
        };
    }


    #endregion

}
