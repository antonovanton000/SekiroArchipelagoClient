using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RandomizerCommon;
using SekiroAPClient.Classes;
using SekiroAPClient.Models;
using SekiroAPClient.Properties;
using SekiroAPClient.Views;
using System.IO;

namespace SekiroAPClient.ViewModels;

public partial class SettingsViewModel : MyBaseViewModel
{   
    public SettingsViewModel()
    {
    }

    [ObservableProperty]
    bool isDebug;

    public string AppVersion => App.AppVersion;

    [RelayCommand]
    async Task Appearing()
    {
        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;
        IsDebug = Settings.Default.IsDebug;
        MainWindow.HideTopButtons();
    }

    private void Frame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == System.Windows.Navigation.NavigationMode.Back)
        {
            (App.Current.MainWindow as MainWindow).frame.Navigating -= Frame_Navigating;
            SaveSettings();       
        }
    }
    
    void SaveSettings()
    {
        Settings.Default.IsDebug = IsDebug;
        Settings.Default.Save();
    }

}
