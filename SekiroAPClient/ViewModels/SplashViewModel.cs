using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SekiroAPClient.Classes;
using System.Diagnostics;
using System.IO;

namespace SekiroAPClient.ViewModels;

public partial class SplashViewModel : MyBaseViewModel
{
    private static readonly System.Version RequiredSekiroInjectorDllVersion = new(1, 2, 4, 0);

    #region Properties

    [ObservableProperty]
    string version;

    [ObservableProperty]
    bool isError;

    [ObservableProperty]
    bool isSekiroLocationInCorrect;

    [ObservableProperty]
    bool isSekiroDllMissing;

    [ObservableProperty]
    bool isPipeServerInCorrect;

    [ObservableProperty]
    bool isSekiroInjectorDllMissing;

    [ObservableProperty]
    bool isSekiroInjectorDllOutdated;

    [ObservableProperty]
    string sekiroInjectorDllVersion = "";

    public string SekiroInjectorRequieredVersion => RequiredSekiroInjectorDllVersion.ToString();

    #endregion

    #region Appearring

    [RelayCommand]
    async Task Appearing()
    {        
        Version = App.AppVersion;
        await Task.Delay(1000);
        IsBusy = true;        
        CheckStuff();        
    }
    #endregion

    #region Commands

    [RelayCommand]
    void CloseApp() => App.Current.Shutdown();
    #endregion

    #region Methods

    private void GoToConnectPage()
    {
        var frame = (App.Current.MainWindow as MainWindow).frame;        
        frame.Source = new Uri($"/Views/ConnectPage.xaml", UriKind.Relative);
        (App.Current.MainWindow as MainWindow).NeedClean = true;
        MainWindow.ClearHistory();
    }

    public void CheckStuff()
    {         
        if (!CheckPipeServer()) { IsError = true; return; }
        if (!CheckSekiroExe()) { IsError = true; return; }
        if (!CheckSekiroInjectorDll()) { IsError = true; return; }
        if (!Checkoo2core_6_win64dll()) { IsError = true; return; }
        if (!CheckDinput8Dll()) { IsError = true; return; }
        if (!CheckModengineIni()) { IsError = true; return; }
        IsBusy = false;
        GoToConnectPage();
    }
    #endregion

    #region State Checks

    private bool CheckPipeServer()
    {
        IsPipeServerInCorrect = !App.PipeServer.IsStarted;
        return !IsPipeServerInCorrect;
    }

    private bool Checkoo2core_6_win64dll()
    {
        if (!File.Exists(Path.Combine(App.Location, "oo2core_6_win64.dll")))
        {
            if (!File.Exists(Path.Combine(App.Location, @"..\oo2core_6_win64.dll")))
            {
                IsSekiroDllMissing = true;
                return false; 
            }
            else
            {
                File.Copy(Path.Combine(App.Location, @"..\oo2core_6_win64.dll"), Path.Combine(App.Location, "oo2core_6_win64.dll"));
            }
        }
        return true;
    }


    private bool CheckSekiroExe()
    {
        IsSekiroLocationInCorrect = !File.Exists(Path.Combine(App.Location, @"..\sekiro.exe"));        
        return !IsSekiroLocationInCorrect;
    }

    private bool CheckDinput8Dll()
    {
        if (!File.Exists(Path.Combine(App.Location, @"..\dinput8.dll")))
        {
            File.Copy(@"modengine\dinput8.dll", @"..\dinput8.dll");
        }
        return true;
    }

    private bool CheckModengineIni()
    {
        if (!File.Exists(Path.Combine(App.Location, @"..\modengine.ini")))
        {
            File.Copy(@"modengine\modengine.ini", @"..\modengine.ini");
        }
        else
        {
            ModEngineIniHelper.UpdateModEngineIni(@"..\modengine.ini", @"\randomizerAP\SekiroInjector.dll", @"\randomizerAP\randomizer");
        }
        return true;
    }

    private bool CheckSekiroInjectorDll()
    {
        var dllPath = Path.Combine(App.Location, "SekiroInjector.dll");

        if (!File.Exists(dllPath))
        {            
            IsSekiroInjectorDllMissing = true;
            return false;   
        }

        var versionInfo = FileVersionInfo.GetVersionInfo(dllPath);
        var versionText = versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "";
        SekiroInjectorDllVersion = string.IsNullOrWhiteSpace(versionText) ? "unknown" : versionText;

        if (!System.Version.TryParse(versionText, out var dllVersion) || dllVersion < RequiredSekiroInjectorDllVersion)
        {
            IsSekiroInjectorDllOutdated = true;
            return false;
        }

        return true;
    }

    #endregion

}
