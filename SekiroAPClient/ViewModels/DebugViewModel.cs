using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using SekiroAPClient.Classes;
using SekiroAPClient.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SekiroAPClient.ViewModels;

public partial class DebugViewModel : MyBaseViewModel
{
    PipeServer pipeServer;
    List<RawSekiroItem> rawItems;
    public DebugViewModel()
    {
        pipeServer = App.PipeServer;
    }

    private async void PipeServer_ItemReceived(ItemRecievedArgs item)
    {
        if (rawItems?.Count > 0)
        {
            var itemName = rawItems.FirstOrDefault(i => i.code == item.GoodId)?.name ?? "unknown";
            LogText += $"[ItemRecieve] {itemName} x{item.Quantity}" + Environment.NewLine;
        }

        if (IsSendBack)
        {
            await Task.Delay(50);
            pipeServer.SendSpawnItem(item.GoodId, item.Quantity);
        }
    }

    private void PipeServer_MessageReceived(string log)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            LogText += log + Environment.NewLine;
        });
    }

    private void PipeServer_DebugLodReceived(string log)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            LogText += log + Environment.NewLine;
        });
    }

    private void PipeServer_ConnectionChanged(string state)
    {
        IsServerConnected = state == "connected";
    }

    [ObservableProperty]
    bool isServerConnected;

    [ObservableProperty]
    string logText = "";

    public ObservableCollection<GameItemCategory> GameItemCategories { get; set; } = [];

    [ObservableProperty]
    GameItemCategory selectedCategory;

    [ObservableProperty]
    GameItem selectedItem;

    [ObservableProperty]
    int spawnCount = 1;

    [ObservableProperty]
    string smallHintText = "Hello from Arhipelago";

    [ObservableProperty]
    string hintText = "Hello from Arhipelago\r\nNext Line Text";

    [ObservableProperty]
    bool isInterruptItems = true;

    [ObservableProperty]
    bool isSendBack = false;

    [ObservableProperty]
    bool isEnemyAiDisabled = false;

    [ObservableProperty]
    bool isOneHitKillEnabled = false;

    [ObservableProperty]
    int eventFlagId;

    [ObservableProperty]
    int eventFlagValue = 0;

    [RelayCommand]
    async Task Appearing()
    {
        IsServerConnected = pipeServer.IsConnected;
        ChangeDebugState(true);
        LoadSekiroItems();
        pipeServer = App.PipeServer;
        pipeServer.DebugLodReceived += PipeServer_DebugLodReceived;
        pipeServer.ItemReceived += PipeServer_ItemReceived;
        pipeServer.ConnectionChanged += PipeServer_ConnectionChanged;
        pipeServer.MessageReceived += PipeServer_MessageReceived;
        (App.Current.MainWindow as MainWindow).frame.Navigating += Frame_Navigating;
    }

    private void Frame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (e.NavigationMode == System.Windows.Navigation.NavigationMode.Back)
        {
            ChangeDebugState(false);
            (App.Current.MainWindow as MainWindow).frame.Navigating -= Frame_Navigating;
            pipeServer.DebugLodReceived -= PipeServer_DebugLodReceived;
            pipeServer.ItemReceived -= PipeServer_ItemReceived;
            pipeServer.ConnectionChanged -= PipeServer_ConnectionChanged;
            pipeServer.MessageReceived -= PipeServer_MessageReceived;
        }
    }

    [RelayCommand]
    void SpawnItem()
    {
        if (SelectedItem == null)
            return;

        //if (SpawnCount <= 0)
        //    return;

        //var eventId = PermanentGoodToFlagCollection.GetPermanentFlagForItem(SelectedItem.Id & 0x0FFFFFFF);
        pipeServer.SendSpawnItem(SelectedItem.Id, SpawnCount);//, eventId);            
    }

    [RelayCommand]
    void ShowSmallHint()
    {
        if (string.IsNullOrEmpty(SmallHintText))
            return;

        pipeServer.SendShowSmallHint(SmallHintText);
    }

    [RelayCommand]
    void ShowHint()
    {
        if (string.IsNullOrEmpty(HintText))
            return;

        pipeServer.SendShowHint(HintText);
    }

    [RelayCommand]
    void KillPlayer()
    {
        pipeServer.SendKillPlayer();
    }

    partial void OnIsEnemyAiDisabledChanged(bool value)
    {
        pipeServer.SendSetEnemyAiDisabled(value);
    }

    partial void OnIsOneHitKillEnabledChanged(bool value)
    {
        pipeServer.SendSetOneHitKillEnabled(value);
    }

    [RelayCommand]
    async Task ActivateAreasIdols()
    {
        int[] areaIdolFlags =
        {
            11110000,
            12500003,
            12000000,
            11500000,
            11700000,
            11000000
        };

        foreach (var flag in areaIdolFlags)
        {
            pipeServer.SendSetEventFlagId(flag, 1);
            await Task.Delay(50);
        }
    }

    [RelayCommand]
    void StartInvasion1()
    {
        SetDebugEventFlag(8301, 1, "Started invasion 1");
    }

    [RelayCommand]
    void StartInvasion2()
    {
        SetDebugEventFlag(8302, 1, "Started invasion 2");
    }

    [RelayCommand]
    void SendEventFlagValue()
    {
        if (EventFlagId == 0 || EventFlagValue <= -1 || EventFlagValue >1) return;
        pipeServer.SendSetEventFlagId(EventFlagId, EventFlagValue);
    }

    void SetDebugEventFlag(int eventFlagId, int value, string message)
    {
        pipeServer.SendSetEventFlagId(eventFlagId, value);
        LogText += $"[Debug] {message}: flag {eventFlagId} = {value}" + Environment.NewLine;
    }

    void ChangeDebugState(bool debug)
    {
        var payload = new
        {
            type = "debug_state",
            value = debug
        };
        string json = JsonSerializer.Serialize(payload);
        pipeServer.SendJson(json);
    }

    void LoadSekiroItems()
    {
        GameItemCategories.Clear();

        // 1. Читаем WPF-ресурс SekiroData/items.json
        var resourceUri = new Uri("SekiroData/items.json", UriKind.Relative);
        var sri = Application.GetResourceStream(resourceUri);
        if (sri == null)
            throw new FileNotFoundException("Resource SekiroData/items.json not found. Проверь Build Action = Resource и путь.");

        string json;
        using (var reader = new StreamReader(sri.Stream))
        {
            json = reader.ReadToEnd();
        }

        // 2. Десериализация
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        rawItems = JsonSerializer.Deserialize<List<RawSekiroItem>>(json, options) ?? [];

        // 3. Группировка по type -> категории
        var groups = rawItems
            .GroupBy(i => i.type ?? string.Empty)
            .OrderBy(g => g.Key); // по имени категории

        foreach (var group in groups)
        {
            var category = new GameItemCategory
            {
                CategoryName = group.Key,
                Items = new ObservableCollection<GameItem>(
                    group
                        .OrderBy(x => x.code) // сортировка внутри категории
                        .Select(x => new GameItem
                        {
                            Id = 0x40000000 + x.code,
                            Name = x.name ?? string.Empty
                        }))
            };

            GameItemCategories.Add(category);
        }
    }
}

