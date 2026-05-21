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
        localItemsStore = new ReceivedItemStore(Path.Combine(App.Location, "randomizer\\localItemsStore.json"));

    }

    public RoomViewModel(ArchipelagoSession session)
    {
        CurrentSession = session;
        RandomizerHelper = new();
        pipeServer = App.PipeServer;
        stateStorage = new StateStorage<ApRandomizationState>(Path.Combine(App.Location, "ap_randomization_state.json"));
        localItemsStore = new ReceivedItemStore(Path.Combine(App.Location, "randomizer\\localItemsStore.json"));
    }
    #endregion

    #region Fields and Properties

    bool isGoBackEnabled = false;
    bool isClosingEnabled = false;
    PipeServer pipeServer;
    public RandomizerHelper RandomizerHelper { get; set; }

    public KeyItemTracker KeyItemTracker { get; set; } = new KeyItemTracker();

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
    bool showNotifications = true;

    public ObservableCollection<ServerNotification> Notifications { get; }
        = new ObservableCollection<ServerNotification>();

    Dictionary<long, int> ApIdsToItemIds = new();

    DeathLinkService? deathLinkService;

    [ObservableProperty]
    bool isDebug;

    private CancellationTokenSource _reconnectCts;

    [ObservableProperty]
    bool isReconnecting = false;

    ReceivedItemStore localItemsStore;

    List<string> consoleCommands = [];

    int lastSentCommandIndex = 0;

    #endregion

    #region Appearing

    [RelayCommand]
    async Task Appearing()
    {
        MainWindow.HideTopButtons();
        IsDebug = Settings.Default.IsDebug;
        ShowNotifications = Settings.Default.ShowNotifications;
        AddEventHandlers();
        KeyItemTracker.InitializeKeyItems();
        IsConnectedToGame = pipeServer.IsConnected;
        var slotData = await CurrentSession.DataStorage.GetSlotDataAsync();
        ApIdsToItemIds = ((JObject)slotData["apIdsToItemIds"]).ToObject<Dictionary<string, int>>()
            .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
        LogText += $"Successfully connected to {CurrentSession.Socket.Uri.Authority}\r\n";

        ApRandomizationState? savedState = await TryLoadExistingRandomization(CurrentSession);
        if (savedState != null)
        {
            State = savedState;
            IsRandomizing = false;
            foreach (var goodId in State.CheckedKeyItems)
            {
                KeyItemTracker.CheckItem(goodId);
            }
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
                if (IsDebug)
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
            await pipeServer.ShowConnectedToServerAsync("Connected to Archipelago!");
            if (CurrentSession.Items.Any())
            {
                await RecieveItems(CurrentSession.Items);
            }
        }

    }

    #endregion

    #region Commands
    [RelayCommand]
    void GoToDebugPage() => MainWindow.NavigateTo(new DebugPage() { DataContext = new DebugViewModel() });

    [RelayCommand]
    void SendCommand()
    {
        if (CurrentSession != null && !string.IsNullOrEmpty(ServerCommand))
        {
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
        if (State == null)
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
        var newWindow = new MainWindow();
        newWindow.frame.Navigate(new DebugPage() { DataContext = new DebugViewModel() });
        newWindow.Show();
    }

    [RelayCommand]
    void LaunchGame()
    {
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

    #endregion

    #region Event Handlers

    private async void PipeServer_ConnectionChanged(string state)
    {
        if (state == "connected")
        {
            IsConnectedToGame = true;
            LogText += $"Successfully connected to Game!\r\n";
            if (IsDebug)
            {
                await Task.Delay(500);
                pipeServer.ChangeDebugState(true);
            }
            await Task.Delay(100);
            pipeServer.ChangeFullDeathDetection(State?.RoomRandomizerOptions.DeathLinkOnFullDeath ?? false);
            if (CurrentSession.Socket.Connected)
            {
                await Task.Delay(500);
                await pipeServer.SendShowSmallHintWhenWorldLoaded("Connected to Archipelago!");
            }
        }
        else if (state == "disconnected")
        {
            IsConnectedToGame = false;
            LogText += $"Disconnected from Game!\r\n";
        }
    }
    private async void Socket_ErrorReceived(Exception e, string message)
    {
        LogText += $"Connection Error. Error: {message}\r\n";
        await PushNotificationAsync(new ServerNotification()
        {
            IsItemNotification = false,
            Text = "Connection Error!"
        });
    }

    private async void Socket_SocketClosed(string reason)
    {
        LogText += $"Connection closed. Reason: {reason}\r\n";
        await TryReconnectAgain();
    }

    private async void MessageLog_OnMessageReceived(Archipelago.MultiClient.Net.MessageLog.Messages.LogMessage message)
    {
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
        await RecieveItems(helper);
    }

    private async void PipeServer_ItemReceived(ItemRecievedArgs obj)
    {
        if (CurrentSession != null && State != null)
        {
            var lotMaps = State.ApLotMap.Where(i => (i.LotId == obj.LotId || i.BaseLotId == obj.LotId) && i.IsShop == obj.IsFromShop);
            if (lotMaps.Count() > 0)
            {
                await CurrentSession.Locations.CompleteLocationChecksAsync(lotMaps.Select(i => i.LocationId).ToArray());
                var permanentFlag = PermanentGoodToFlagCollection.GetPermanentFlagForItem(obj.GoodId);
                if (permanentFlag > 0)
                {
                    pipeServer.SendSetEventFlagId(permanentFlag, 1);
                }

                if (IsSekiroMemoryItem(obj.GoodId))
                {
                    pipeServer.SendSpawnItem(0x40000000 + 5400, 1);
                }

                if (KeyItemTracker.CheckItem(obj.GoodId))
                {
                    if (State != null)
                    {
                        State.CheckedKeyItems.Add(obj.GoodId);
                        await stateStorage.SaveAsync(State);
                    }
                }

            }
        }
    }

    private static bool IsSekiroMemoryItem(int goodId)
    {
        return goodId >= 5200 && goodId <= 5213;
    }

    private void PipeServer_PlayerDeath(bool isRealDeath)
    {
        deathLinkService?.SendDeathLink(new DeathLink(CurrentSession.Players.ActivePlayer.Name, DeathLinkReasonHelper.GetRandomDeathLinkReason()));
    }

    private async void DeathLinkService_OnDeathLinkReceived(DeathLink deathLink)
    {
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
                    RemoveEventHandlers();
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
                RemoveEventHandlers();
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
        if (!string.IsNullOrEmpty(endingType))
        {
            if (endingType == "shura") // Then we need to check global option witch ending is set 
            {

            }
            else if (endingType == "immortal")
            {

            }
            CurrentSession.SetGoalAchieved(); // For now just send anyway
        }
    }

    #endregion

    #region Methods

    void AddEventHandlers()
    {
        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;
        (App.Current.MainWindow as MainWindow).Closing += RoomViewModel_Closing;
        CurrentSession.MessageLog.OnMessageReceived += MessageLog_OnMessageReceived;
        CurrentSession.Items.ItemReceived += Items_ItemReceived;
        CurrentSession.Socket.SocketClosed += Socket_SocketClosed;
        CurrentSession.Socket.ErrorReceived += Socket_ErrorReceived;
        pipeServer.ItemReceived += PipeServer_ItemReceived;
        pipeServer.ConnectionChanged += PipeServer_ConnectionChanged;
        pipeServer.PlayerDeath += PipeServer_PlayerDeath;
        pipeServer.EndingDetected += PipeServer_EndingDetected;
    }

    void RemoveEventHandlers()
    {
        (App.Current.MainWindow as MainWindow).frame.Navigating -= Frame_Navigating;
        (App.Current.MainWindow as MainWindow).Closing -= RoomViewModel_Closing;
        CurrentSession.MessageLog.OnMessageReceived -= MessageLog_OnMessageReceived;
        CurrentSession.Items.ItemReceived -= Items_ItemReceived;
        CurrentSession.Socket.SocketClosed -= Socket_SocketClosed;
        CurrentSession.Socket.ErrorReceived -= Socket_ErrorReceived;
        pipeServer.ItemReceived -= PipeServer_ItemReceived;
        pipeServer.ConnectionChanged -= PipeServer_ConnectionChanged;
        pipeServer.PlayerDeath -= PipeServer_PlayerDeath;
        pipeServer.EndingDetected -= PipeServer_EndingDetected;
    }

    async Task Randomize()
    {
        IsRandomizing = true;
        HasErrors = false;
        State = await RandomizerHelper.RandomizeArchipelago(CurrentSession);
        if (State != null)
        {
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
            return null;
        }
        return loaded;
    }

    async Task RecieveItems(IReceivedItemsHelper helper)
    {
        while (helper.Any())
        {
            var item = helper.DequeueItem();
            var key = ReceivedItemStore.MakeKey(item.ItemId, item.LocationId, item.Player.Slot);
            if (!localItemsStore.TryMark(key) && item.LocationName != "Cheat Console")
                continue;


            var gameItemFullId = ApIdsToItemIds.ContainsKey(item.ItemId) ? ApIdsToItemIds[item.ItemId] : -1;
            if (gameItemFullId != -1)
            {
                var goodId = gameItemFullId & 0x0FFFFFFF;
                var count = ItemCountParser.GetCountFromItemName(item.ItemName);
                var itemEventId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(goodId);

                if (itemEventId>0)
                {
                    pipeServer.SendSpawnItem(gameItemFullId, count, itemEventId);                    
                }                
                else
                {
                    pipeServer.SendSpawnItem(gameItemFullId, count);
                }
                if (goodId >= 5200 && goodId <= 5213)
                {                        
                    pipeServer.SendSpawnItem(0x40000000 + 5400, 1);//Add extra memory item for proper memorys count in idol menu
                }                    


                if (KeyItemTracker.CheckItem(goodId))
                {
                    if (State != null)
                    {
                        State.CheckedKeyItems.Add(gameItemFullId);
                        await stateStorage.SaveAsync(State);
                    }
                }

            }
            else
            {
                LogText += $"Received unknown item with AP Item ID: {item.ItemId}\r\n";
            }
        }
        localItemsStore.Save();
    }

    async Task TryReconnectAgain()
    {
        if (IsReconnecting)
            return;

        IsReconnecting = true;
        _reconnectCts = new CancellationTokenSource();
        await pipeServer.ShowConnectedToServerAsync("Connection lost. Attempting to reconnect...");
        var token = _reconnectCts.Token;

        while (!token.IsCancellationRequested)
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
                            RemoveEventHandlers();
                            CurrentSession = newSession;
                            AddEventHandlers();
                            if (State?.RoomRandomizerOptions.DeathLink == true)
                            {
                                deathLinkService = CurrentSession.CreateDeathLinkService();
                                deathLinkService.EnableDeathLink();
                                deathLinkService.OnDeathLinkReceived += DeathLinkService_OnDeathLinkReceived;
                            }
                            await pipeServer.ShowConnectedToServerAsync("Connected to Archipelago!");
                            if (CurrentSession.Items.Any())
                            {
                                await RecieveItems(CurrentSession.Items);
                            }
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

    #endregion

}
