using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RandomizerCommon;
using SekiroAPClient.Classes;
using SekiroAPClient.Models;
using SekiroAPClient.Properties;
using SekiroAPClient.Views;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Net.Http;

namespace SekiroAPClient.ViewModels;

public partial class ConnectViewModel : MyBaseViewModel
{

    #region Constructor
    public ConnectViewModel()
    {
        ConnectModel = new ConnectModel()
        {
            RoomUrl = Settings.Default.RoomUrl,
            PlayerName = Settings.Default.PlayerName,
            Password = Settings.Default.Password
        };
        ConnectModel.PropertyChanged += ConnectModel_PropertyChanged;
    }
    #endregion

    #region Properties

    public ConnectModel ConnectModel { get; set; } = default!;

    ArchipelagoSession? _currentSession;

    [ObservableProperty]
    string? notificationStatus;

    [ObservableProperty]
    bool isDebug;

    [ObservableProperty]
    bool showUpdateNotification;

    [ObservableProperty]
    string version = "";

    #endregion

    #region Appearring

    [RelayCommand]
    async Task Appearing()
    {
        Version = App.AppVersion;
        MainWindow.ShowTopButtons();
        IsDebug = Settings.Default.IsDebug;

        await CheckForUpdate();
    }

    #endregion

    #region Commands

    [RelayCommand]
    void GoToDebugPage() => MainWindow.NavigateTo(new DebugPage() { DataContext = new DebugViewModel() });


    [RelayCommand]
    void GoToRandomizerOptionsPage() => MainWindow.NavigateTo(new RandomizerOptionsPage() { DataContext = new RandomizerOptionsViewModel() });

    [RelayCommand]
    async Task Connect()
    {
        IsBusy = true;
        _currentSession = ArchipelagoSessionFactory.CreateSession(ConnectModel.RoomUrl);
        try
        {
            NotificationStatus = $"Connecting to {ConnectModel.RoomUrl}";
            var roomInfo = await _currentSession.ConnectAsync();
            if (roomInfo != null)
            {
                if (!roomInfo.Games.Any(i => i == ConnectModel.GameName))
                {
                    MainWindow.ShowToast(new ToastInfo() { ToastType = ToastType.Error, Title = "Error", Detail = "Room dose not have Sekiro Game!" });
                    return;
                }
                NotificationStatus = $"Try to login as {ConnectModel.PlayerName}";
                var loginResult = await _currentSession.LoginAsync(ConnectModel.GameName, ConnectModel.PlayerName, ItemsHandlingFlags.RemoteItems, password: ConnectModel.Password);
                if (!loginResult.Successful)
                {
                    MainWindow.ShowToast(new ToastInfo() { ToastType = ToastType.Error, Title = "Error", Detail = "Player Name or Password invalid!" });
                    return;
                }
                App.SetActiveSession(_currentSession);
                SaveSettings();
                ConnectModel.PropertyChanged -= ConnectModel_PropertyChanged;
                MainWindow.NavigateTo(new RoomPage() { DataContext = new RoomViewModel(_currentSession) });

            }
        }
        catch (Exception ex)
        {
            MainWindow.ShowToast(new ToastInfo() { ToastType = ToastType.Error, Title = "Error", Detail = "Connection failed!" });
        }
        finally
        {
            IsBusy = false;
        }

    }

    [RelayCommand]
    void DismissUpdate() => ShowUpdateNotification = false;

    [RelayCommand]
    void StartUpdate()
    {
        Process.Start("AppUpdater.exe");
        App.Current.Shutdown();
    }

    #endregion

    #region Update Check

    async Task CheckForUpdate()
    {
        //using var http = new HttpClient();
        //var provider = new GitHubReleaseProvider(http, "antonovanton000", "SekiroArchipelagoClient");

        //var alpha = await provider.GetLatestAsync(includePrerelease: true, assetName: "randomizerAP.zip");
        //var stable = await provider.GetLatestAsync(includePrerelease: false, assetName: "randomizerAP.zip");

        //var appVersion = Version.Parse(App.AppVersion);

        //if (stable == null)
        //{
        //    if (alpha == null)
        //    {
        //        return;
        //    }
        //}

        //var latestVersion = stable?.Version ?? alpha?.Version;
        //if (latestVersion == null) return;

        //if (latestVersion > appVersion)
        //{
        //    ShowUpdateNotification = true;
        //}
    }

    #endregion

    #region Methods

    void SaveSettings()
    {
        Settings.Default.RoomUrl = ConnectModel.RoomUrl;
        Settings.Default.PlayerName = ConnectModel.PlayerName;
        Settings.Default.Password = ConnectModel.Password;
        Settings.Default.Save();
    }

    private void ConnectModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectModel.RoomUrl))
            Settings.Default.RoomUrl = ConnectModel.RoomUrl;

        if (e.PropertyName == nameof(ConnectModel.PlayerName))
            Settings.Default.PlayerName = ConnectModel.PlayerName;

        if (e.PropertyName == nameof(ConnectModel.Password))
            Settings.Default.Password = ConnectModel.Password;

        Settings.Default.Save();
    }

    #endregion
}
