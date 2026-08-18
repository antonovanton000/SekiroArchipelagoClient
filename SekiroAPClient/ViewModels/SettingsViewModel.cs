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


    [ObservableProperty]
    string additionalDllPath = "";

    string _injectorConfigIniPath = Path.Combine(App.Location, "injector_config.ini");

    public string AppVersion => App.AppVersion;

    [RelayCommand]
    async Task Appearing()
    {
        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;
        IsDebug = Settings.Default.IsDebug;
        LoadInjectorConfigIni();
        MainWindow.HideTopButtons();
    }

    private void Frame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == System.Windows.Navigation.NavigationMode.Back)
        {
            (App.Current.MainWindow as MainWindow).frame.Navigating -= Frame_Navigating;
            SaveSettings();
            SaveInjectorConfigIni();
        }
    }
    
    void SaveSettings()
    {
        Settings.Default.IsDebug = IsDebug;
        Settings.Default.Save();
    }

    void LoadInjectorConfigIni()
    {
        AdditionalDllPath = "";

        if (!File.Exists(_injectorConfigIniPath))
            return;

        string currentSection = "";
        foreach (string line in File.ReadLines(_injectorConfigIniPath))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1].Trim();
                continue;
            }

            if (!currentSection.Equals("Chainload", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith(';') ||
                trimmed.StartsWith('#'))
                continue;

            int equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex < 0 ||
                !trimmed[..equalsIndex].Trim().Equals("chainDInput8dllPath", StringComparison.OrdinalIgnoreCase))
                continue;

            string value = trimmed[(equalsIndex + 1)..].Trim();
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                value = value[1..^1];

            AdditionalDllPath = value;
            return;
        }
    }


    void SaveInjectorConfigIni()
    {
        var lines = File.Exists(_injectorConfigIniPath)
            ? new List<string>(File.ReadAllLines(_injectorConfigIniPath))
            : [];

        int sectionStart = -1;
        int sectionEnd = lines.Count;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();
            if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
                continue;

            if (sectionStart >= 0)
            {
                sectionEnd = i;
                break;
            }

            if (trimmed[1..^1].Trim().Equals("Chainload", StringComparison.OrdinalIgnoreCase))
                sectionStart = i;
        }

        string newLine = $"chainDInput8dllPath={AdditionalDllPath.Trim()}";

        if (sectionStart < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add("");

            lines.Add("[Chainload]");
            lines.Add(newLine);
        }
        else
        {
            bool keyUpdated = false;
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                    continue;

                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex < 0 ||
                    !trimmed[..equalsIndex].Trim().Equals("chainDInput8dllPath", StringComparison.OrdinalIgnoreCase))
                    continue;

                string indentation = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = indentation + newLine;
                keyUpdated = true;
                break;
            }

            if (!keyUpdated)
                lines.Insert(sectionEnd, newLine);
        }

        File.WriteAllLines(_injectorConfigIniPath, lines);
    }


}
