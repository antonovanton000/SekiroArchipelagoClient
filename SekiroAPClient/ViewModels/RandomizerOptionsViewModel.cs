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
using YamlDotNet.Serialization;
using static RandomizerCommon.EnemyAnnotations;

namespace SekiroAPClient.ViewModels;

public partial class RandomizerOptionsViewModel : MyBaseViewModel
{   
    public RandomizerOptionsViewModel()
    {
    }

    [ObservableProperty]
    bool deathLinkOnFullDeath = Properties.Settings.Default.DeathLinkOnFullDeath;

    [ObservableProperty]
    bool removeHeadlessSlowWalk = Properties.Settings.Default.RemoveHeadlessSlowWalk;

    [ObservableProperty]
    bool openBellDemonDoor = Properties.Settings.Default.OpenBellDemonDoor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOopsAll))]
    string selectedPreset = string.IsNullOrEmpty(Properties.Settings.Default.SelectedPreset) ? "None" : Properties.Settings.Default.SelectedPreset;

    [ObservableProperty]
    string selectedEnemy = Properties.Settings.Default.SelectedEnemy;

    [ObservableProperty]
    List<string> presets = [];

    [ObservableProperty]
    List<string> enemiesList = [];    

    public bool IsOopsAll => SelectedPreset == "Oops All";


    [RelayCommand]
    async Task Appearing()
    {
        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;   
        MainWindow.HideTopButtons();
        Presets = GetPresetNames();
        EnemiesList = GetEnemiesNames();
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
        Properties.Settings.Default.DeathLinkOnFullDeath = DeathLinkOnFullDeath;
        Properties.Settings.Default.RemoveHeadlessSlowWalk = RemoveHeadlessSlowWalk;
        Properties.Settings.Default.OpenBellDemonDoor = OpenBellDemonDoor;
        Properties.Settings.Default.SelectedPreset = SelectedPreset == "None" ? "" : SelectedPreset;
        Properties.Settings.Default.SelectedEnemy = SelectedPreset == "Oops All" ? SelectedEnemy : "";
        Properties.Settings.Default.Save();
    }

    public List<string> GetPresetNames()
    {
        List<string> ret = new List<string>();
        if (Directory.Exists("presets"))
        {
            ret = Directory.GetFiles("presets", "*.txt").Select(p => Path.GetFileNameWithoutExtension(p)).ToList();
            ret.Remove("README");
            ret.Remove("Template");
            ret.Sort();
        }
        ret.Insert(0, "None");
        return ret;
    }

    public List<string> GetEnemiesNames()
    {
        List<string> enemyNames = new List<string>();
        try
        {
            EnemyAnnotations ann;
            IDeserializer deserializer = new DeserializerBuilder().Build();
            using (var reader = File.OpenText($"dists/Base/enemy.txt"))
            {
                ann = deserializer.Deserialize<EnemyAnnotations>(reader);
            }
            HashSet<string> singletons = ann.Singletons == null ? new HashSet<string>() : new HashSet<string>(ann.Singletons);            
            foreach (EnemyCategory cat in ann.Categories)
            {
                if (cat.Name == null || cat.Hidden || singletons.Contains(cat.Name)) continue;
                enemyNames.Add(cat.Name);
                foreach (string subname in new[] { cat.Partition, cat.Partial, cat.Instance }.Where(g => g != null).SelectMany(g => g))
                {
                    if (singletons.Contains(subname)) continue;
                    enemyNames.Add("- " + subname);
                }
            }
        }
        catch (Exception)
        {
            
        }
        return enemyNames;
    }

}
