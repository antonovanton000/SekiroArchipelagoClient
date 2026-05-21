using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using SekiroAPClient.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;

namespace SekiroAPClient.ViewModels;

public partial class ItemTrackerViewModel : MyBaseViewModel, IDisposable
{
    private readonly ApRandomizationState _state;
    private readonly PipeServer _pipeServer;
    private readonly string _currentPlayerName;
    private readonly Dictionary<long, ItemTrackerEntry> _entriesByLocationId = new();
    private readonly Dictionary<(long LotId, bool IsShop), List<ItemTrackerEntry>> _entriesByLot = new();
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private readonly object _saveLock = new();
    private bool _entriesBuilt;

    public event Action<ItemTrackerEntry>? RecentlyPickedEntryRequested;

    public ObservableCollection<ItemTrackerRegionGroup> Groups { get; } = [];

    [ObservableProperty]
    ItemTrackerRegionGroup? selectedGroup;

    [ObservableProperty]
    string spoilerUrl = "";

    [ObservableProperty]
    string status = "";

    [ObservableProperty]
    string lastPickupStatus = "No pickups tracked yet.";

    [ObservableProperty]
    bool isSpoilerLoaded;

    [ObservableProperty]
    int checkedCount;

    [ObservableProperty]
    int totalCount;

    public ItemTrackerViewModel(ApRandomizationState state, PipeServer pipeServer, string? currentPlayerName = null)
    {
        _state = state;
        _pipeServer = pipeServer;
        _currentPlayerName = currentPlayerName?.Trim() ?? "";
        Title = "Archipelago Item Tracker";

        _pipeServer.ItemReceived += PipeServer_ItemReceived;
    }

    [RelayCommand]
    async Task Appearing()
    {
        await BuildEntriesAsync();
    }

    [RelayCommand]
    async Task LoadSpoilerAsync()
    {
        if (string.IsNullOrWhiteSpace(SpoilerUrl))
        {
            Status = "Paste spoiler log URL first.";
            return;
        }

        if (!_entriesBuilt)
            await BuildEntriesAsync();

        IsBusy = true;
        IsSpoilerLoaded = false;
        Status = "Loading all required item tracker data, please wait...";
        try
        {
            using var client = new HttpClient();
            string text = await client.GetStringAsync(SpoilerUrl);
            var spoilerItems = ParseSpoilerLocations(text);

            int matched = 0;
            foreach (var entry in _entriesByLocationId.Values)
            {
                if (spoilerItems.TryGetValue(entry.LocationName, out var itemName))
                {
                    entry.SpoilerItemName = itemName;
                    matched++;
                }
            }

            IsSpoilerLoaded = true;
            string playerSuffix = string.IsNullOrWhiteSpace(_currentPlayerName) ? "" : $" for {_currentPlayerName}";
            Status = $"Spoiler loaded{playerSuffix}. Matched {matched}/{TotalCount} tracker locations.";
            SaveTrackerState();
        }
        catch (Exception ex)
        {
            IsSpoilerLoaded = false;
            Status = $"Spoiler load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseTracker))]
    async Task CheckAllFlagsAsync()
    {
        IsBusy = true;
        Status = "Checking event flags...";
        try
        {
            foreach (var entry in _entriesByLocationId.Values.Where(e => e.EventFlagId > 0))
            {
                await VerifyFlagAsync(entry, persist: false);
                await Task.Delay(25);
            }

            Status = "Event flag check finished.";
            SaveTrackerState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    bool CanUseTracker() => IsSpoilerLoaded;

    partial void OnIsSpoilerLoadedChanged(bool value)
    {
        CheckAllFlagsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    void Report(ItemTrackerEntry? entry)
    {
        if (entry == null)
            return;

        Directory.CreateDirectory(Path.Combine(App.Location, "ItemIssuesReports"));
        string safeName = Regex.Replace(entry.LocationName, @"[^a-zA-Z0-9_\-]+", "_").Trim('_');
        if (safeName.Length > 80)
            safeName = safeName[..80];

        string fileName = $"{DateTime.Now:yyyy-MM-dd_HH.mm.ss}_loc_{entry.LocationId}_{safeName}.json";
        string path = Path.Combine(App.Location, "ItemIssuesReports", fileName);

        var report = new
        {
            CreatedAt = DateTime.Now,
            SpoilerUrl,
            Entry = new
            {
                entry.LocationId,
                entry.LocationName,
                entry.RegionName,
                entry.SpoilerItemName,
                entry.GoodId,
                entry.FullId,
                entry.Quantity,
                entry.EventFlagId,
                entry.IsShop,
                entry.IsForeign,
                entry.LotIdSummary,
                entry.BaseLotIdSummary,
                entry.IsChecked,
                entry.LastPickedGoodId,
                entry.LastPickedQuantity,
                entry.FlagStatus
            },
            LotEntries = entry.LotEntries,
            State = new
            {
                _state.ServerAddress,
                _state.RoomName,
                _state.SlotName,
                _state.Seed,
                _state.LocalSeed,
                _state.Game
            }
        };

        File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
        entry.ReportStatus = $"Reported: {fileName}";
        Status = $"Issue report written: {fileName}";
    }

    public void Dispose()
    {
        _pipeServer.ItemReceived -= PipeServer_ItemReceived;
    }

    private async Task BuildEntriesAsync()
    {
        await _buildLock.WaitAsync();
        try
        {
            if (_entriesBuilt)
                return;

            IsBusy = true;
            Status = "Loading all required item tracker data, please wait...";

            var build = await Task.Run(() =>
            {
                var entries = _state.ApLotMap
                    .GroupBy(e => e.LocationId)
                    .Select(g =>
                    {
                        var first = g.First();
                        string locationName = string.IsNullOrWhiteSpace(first.LocationName)
                            ? $"Location {first.LocationId}"
                            : first.LocationName;
                        string prefix = ExtractRegionPrefix(locationName);

                        return new ItemTrackerEntry
                        {
                            LocationId = first.LocationId,
                            LocationName = locationName,
                            RegionPrefix = prefix,
                            RegionName = FormatRegionName(prefix),
                            LotEntries = g.OrderBy(e => e.LotId).ToList()
                        };
                    })
                    .OrderBy(e => e.LocationId)
                    .ToList();

                var entriesByLocationId = entries.ToDictionary(e => e.LocationId);
                var entriesByLot = new Dictionary<(long LotId, bool IsShop), List<ItemTrackerEntry>>();

                foreach (var entry in entries)
                {
                    foreach (var lot in entry.LotEntries)
                    {
                        AddLotIndex(entriesByLot, lot.LotId, lot.IsShop, entry);
                        AddLotIndex(entriesByLot, lot.BaseLotId, lot.IsShop, entry);
                    }
                }

                var groups = entries
                    .GroupBy(e => e.RegionName)
                    .Select(group => (RegionName: $"{group.Key} ({group.Count()})", Entries: group.ToList()))
                    .ToList();

                var savedState = LoadSavedTrackerState();
                bool restoredProgress = false;
                if (savedState != null)
                    restoredProgress = ApplySavedTrackerState(entriesByLocationId, savedState);

                return new
                {
                    Entries = entries,
                    EntriesByLocationId = entriesByLocationId,
                    EntriesByLot = entriesByLot,
                    Groups = groups,
                    SavedState = savedState,
                    RestoredProgress = restoredProgress
                };
            });

            Groups.Clear();
            _entriesByLocationId.Clear();
            _entriesByLot.Clear();

            foreach (var pair in build.EntriesByLocationId)
                _entriesByLocationId[pair.Key] = pair.Value;

            foreach (var pair in build.EntriesByLot)
                _entriesByLot[pair.Key] = pair.Value;

            foreach (var group in build.Groups)
            {
                Groups.Add(new ItemTrackerRegionGroup
                {
                    RegionName = group.RegionName,
                    Entries = new ObservableCollection<ItemTrackerEntry>(group.Entries)
                });
            }

            SelectedGroup = Groups.FirstOrDefault();

            TotalCount = build.Entries.Count;
            CheckedCount = build.Entries.Count(e => e.IsChecked);
            if (build.RestoredProgress && build.SavedState != null)
            {
                SpoilerUrl = build.SavedState.SpoilerUrl ?? "";
                IsSpoilerLoaded = build.SavedState.IsSpoilerLoaded;
                Status = IsSpoilerLoaded
                    ? $"Restored previous tracker progress. Picked {CheckedCount}/{TotalCount}."
                    : "Restored previous tracker progress. Load spoiler log to continue.";
            }
            else
            {
                Status = "Paste the Archipelago spoiler log URL and load it to start tracking.";
            }
            _entriesBuilt = true;
        }
        finally
        {
            IsBusy = false;
            _buildLock.Release();
        }
    }

    private static void AddLotIndex(
        Dictionary<(long LotId, bool IsShop), List<ItemTrackerEntry>> entriesByLot,
        long lotId,
        bool isShop,
        ItemTrackerEntry entry)
    {
        if (lotId <= 0)
            return;

        var key = (lotId, isShop);
        if (!entriesByLot.TryGetValue(key, out var entries))
        {
            entries = [];
            entriesByLot[key] = entries;
        }

        if (!entries.Any(e => e.LocationId == entry.LocationId))
            entries.Add(entry);
    }

    private async void PipeServer_ItemReceived(ItemRecievedArgs obj)
    {
        var matches = new List<ItemTrackerEntry>();
        if (_entriesByLot.TryGetValue((obj.LotId, obj.IsFromShop), out var byLot))
            matches.AddRange(byLot);

        foreach (var entry in matches.DistinctBy(e => e.LocationId))
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SelectedGroup = Groups.FirstOrDefault(g => g.Entries.Contains(entry)) ?? SelectedGroup;
                entry.IsChecked = true;
                entry.LastPickedGoodId = obj.GoodId;
                entry.LastPickedQuantity = obj.Quantity;
                CheckedCount = _entriesByLocationId.Values.Count(e => e.IsChecked);
                LastPickupStatus = FormatPickupStatus(entry, obj);
            });

            RecentlyPickedEntryRequested?.Invoke(entry);
            await VerifyFlagAsync(entry);
        }
    }

    private static string FormatPickupStatus(ItemTrackerEntry entry, ItemRecievedArgs pickup)
    {
        string quantity = pickup.Quantity == 1 ? "" : $" x{pickup.Quantity}";
        return $"Picked Good {pickup.GoodId}{quantity} at {entry.LocationName}. Expected: {entry.SpoilerDisplay}.";
    }

    private async Task VerifyFlagAsync(ItemTrackerEntry entry)
        => await VerifyFlagAsync(entry, persist: true);

    private async Task VerifyFlagAsync(ItemTrackerEntry entry, bool persist)
    {
        if (entry.EventFlagId <= 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => entry.FlagStatus = "No EventFlagId");
            if (persist)
                SaveTrackerState();
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() => entry.FlagStatus = "Checking...");
        bool? value = await _pipeServer.SendGetEventFlagIdAsync(entry.EventFlagId);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            entry.FlagStatus = value switch
            {
                true => "ON",
                false => "OFF",
                null => "Unknown / no response"
            };
        });

        if (persist)
            SaveTrackerState();
    }

    private TrackerSaveState? LoadSavedTrackerState()
    {
        string path = GetTrackerStatePath();
        if (!File.Exists(path))
            return null;

        try
        {
            var savedState = JsonConvert.DeserializeObject<TrackerSaveState>(File.ReadAllText(path));
            return savedState != null && MatchesCurrentWorld(savedState) ? savedState : null;
        }
        catch
        {
            return null;
        }
    }

    private bool ApplySavedTrackerState(
        Dictionary<long, ItemTrackerEntry> entriesByLocationId,
        TrackerSaveState savedState)
    {
        if (savedState.Entries.Count == 0)
            return false;

        foreach (var savedEntry in savedState.Entries)
        {
            if (!entriesByLocationId.TryGetValue(savedEntry.LocationId, out var entry))
                continue;

            entry.SpoilerItemName = savedEntry.SpoilerItemName ?? "";
            entry.IsChecked = savedEntry.IsChecked;
            entry.LastPickedGoodId = savedEntry.LastPickedGoodId;
            entry.LastPickedQuantity = savedEntry.LastPickedQuantity;
            entry.FlagStatus = string.IsNullOrWhiteSpace(savedEntry.FlagStatus)
                ? "Not checked"
                : savedEntry.FlagStatus;
        }

        return true;
    }

    private void SaveTrackerState()
    {
        if (_entriesByLocationId.Count == 0)
            return;

        var savedState = new TrackerSaveState
        {
            ServerAddress = _state.ServerAddress,
            RoomName = _state.RoomName,
            SlotName = _state.SlotName,
            PlayerName = _currentPlayerName,
            Seed = _state.Seed,
            LocalSeed = _state.LocalSeed,
            Game = _state.Game,
            SpoilerUrl = SpoilerUrl,
            IsSpoilerLoaded = IsSpoilerLoaded,
            SavedAt = DateTime.Now,
            Entries = _entriesByLocationId.Values
                .OrderBy(e => e.LocationId)
                .Select(e => new TrackerEntrySaveState
                {
                    LocationId = e.LocationId,
                    SpoilerItemName = e.SpoilerItemName,
                    IsChecked = e.IsChecked,
                    LastPickedGoodId = e.LastPickedGoodId,
                    LastPickedQuantity = e.LastPickedQuantity,
                    FlagStatus = e.FlagStatus
                })
                .ToList()
        };

        string path = GetTrackerStatePath();
        lock (_saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(savedState, Formatting.Indented));
        }
    }

    private bool MatchesCurrentWorld(TrackerSaveState savedState)
    {
        return string.Equals(savedState.ServerAddress, _state.ServerAddress, StringComparison.Ordinal)
            && string.Equals(savedState.SlotName, _state.SlotName, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(_currentPlayerName)
                || string.Equals(savedState.PlayerName, _currentPlayerName, StringComparison.OrdinalIgnoreCase))
            && string.Equals(savedState.Seed, _state.Seed, StringComparison.Ordinal)
            && savedState.LocalSeed == _state.LocalSeed
            && string.Equals(savedState.Game, _state.Game, StringComparison.Ordinal);
    }

    private string GetTrackerStatePath()
    {
        string fileName = Regex.Replace($"{_state.Game}_{_state.SlotName}_{_state.Seed}_{_state.LocalSeed}", @"[^a-zA-Z0-9_\-]+", "_");
        if (fileName.Length > 120)
            fileName = fileName[..120];

        return Path.Combine(App.Location, "ItemTrackerStates", $"{fileName}.json");
    }

    private Dictionary<string, string> ParseSpoilerLocations(string spoilerText)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var knownLocations = _entriesByLocationId.Values
            .Select(e => e.LocationName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderByDescending(n => n.Length)
            .ToList();

        bool inLocations = false;
        foreach (var rawLine in spoilerText.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (!inLocations)
            {
                if (line.Equals("Locations:", StringComparison.OrdinalIgnoreCase))
                    inLocations = true;
                continue;
            }

            if (line.EndsWith(':') && !knownLocations.Any(loc => line.StartsWith(loc + ":", StringComparison.OrdinalIgnoreCase)))
                break;

            foreach (string locationName in knownLocations)
            {
                if (!TryReadSpoilerItemForLocation(line, locationName, _currentPlayerName, out string itemName))
                    continue;

                if (itemName.Length > 0)
                    result[locationName] = itemName;
                break;
            }
        }

        return result;
    }

    private static bool TryReadSpoilerItemForLocation(string line, string locationName, string currentPlayerName, out string itemName)
    {
        itemName = "";
        if (!line.StartsWith(locationName, StringComparison.OrdinalIgnoreCase))
            return false;

        string rest = line[locationName.Length..].TrimStart();

        // Single-player spoiler format:
        // DT: Pellet - next to idol: Heavy Coin Purse
        if (rest.StartsWith(':'))
        {
            itemName = rest[1..].Trim();
            return true;
        }

        // Multiworld spoiler format:
        // DT: Pellet - next to idol (PlayerName): Heavy Coin Purse (OwnerName)
        if (rest.StartsWith('('))
        {
            int ownerSuffixEnd = rest.IndexOf("):", StringComparison.Ordinal);
            if (ownerSuffixEnd >= 0)
            {
                string locationPlayer = rest[1..ownerSuffixEnd].Trim();
                if (!string.IsNullOrWhiteSpace(currentPlayerName) &&
                    !string.Equals(locationPlayer, currentPlayerName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                itemName = rest[(ownerSuffixEnd + 2)..].Trim();
                return true;
            }
        }

        return false;
    }

    private static string ExtractRegionPrefix(string locationName)
    {
        int colon = locationName.IndexOf(':');
        return colon > 0 ? locationName[..colon].Trim() : "Other";
    }

    private static string FormatRegionName(string prefix)
    {
        return prefix switch
        {
            "T" => "T - Tutorial",
            "DT" => "DT - Dilapidated Temple",
            "AO" => "AO - Ashina Outskirts",
            "HE1" => "HE1 - Hirata Estate",
            "AC" => "AC - Ashina Castle",
            "AR" => "AR - Ashina Reservoir",
            "AD" => "AD - Abandoned Dungeon",
            "ST" => "ST - Senpou Temple",
            "SV" => "SV - Sunken Valley",
            "SVP" => "SVP - Sunken Valley Passage",
            "PP" => "PP - Poison Pool",
            "HF" => "HF - Hidden Forest",
            "MV" => "MV - Mibu Village",
            "AC/I" => "AC/I - Ashina Castle Interior Ministry",
            "HE2" => "HE2 - Hirata Estate Father",
            "FP1" => "FP1 - Fountainhead Palace",
            "FP2" => "FP2 - Fountainhead Palace Late",
            "AC/C" => "AC/C - Ashina Castle Central Forces",
            "AO/C" => "AO/C - Ashina Outskirts Central Forces",
            "AR/C" => "AR/C - Ashina Reservoir Central Forces",
            _ => $"{prefix} - Other"
        };
    }

    private sealed class TrackerSaveState
    {
        public string ServerAddress { get; set; } = "";
        public string RoomName { get; set; } = "";
        public string SlotName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Seed { get; set; } = "";
        public long LocalSeed { get; set; }
        public string Game { get; set; } = "";
        public string SpoilerUrl { get; set; } = "";
        public bool IsSpoilerLoaded { get; set; }
        public DateTime SavedAt { get; set; }
        public List<TrackerEntrySaveState> Entries { get; set; } = [];
    }

    private sealed class TrackerEntrySaveState
    {
        public long LocationId { get; set; }
        public string SpoilerItemName { get; set; } = "";
        public bool IsChecked { get; set; }
        public int? LastPickedGoodId { get; set; }
        public int? LastPickedQuantity { get; set; }
        public string FlagStatus { get; set; } = "";
    }
}
