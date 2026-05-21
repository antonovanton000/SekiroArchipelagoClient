using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace SekiroAPClient.Models;

public partial class ItemTrackerEntry : ObservableObject
{
    public long LocationId { get; init; }
    public string LocationName { get; init; } = "";
    public string RegionPrefix { get; init; } = "";
    public string RegionName { get; init; } = "";
    public IReadOnlyList<ApLotEntry> LotEntries { get; init; } = [];

    [ObservableProperty]
    string spoilerItemName = "";

    [ObservableProperty]
    bool isChecked;

    [ObservableProperty]
    bool isRecentlyPicked;

    [ObservableProperty]
    int? lastPickedGoodId;

    [ObservableProperty]
    int? lastPickedQuantity;

    [ObservableProperty]
    string flagStatus = "Not checked";

    [ObservableProperty]
    string reportStatus = "";

    public long GoodId => LotEntries.FirstOrDefault()?.GoodId ?? 0;
    public long FullId => LotEntries.FirstOrDefault()?.FullId ?? 0;
    public int Quantity => LotEntries.FirstOrDefault()?.Quantity ?? 0;
    public int EventFlagId => LotEntries.FirstOrDefault(e => e.EventFlagId > 0)?.EventFlagId ?? -1;
    public bool IsShop => LotEntries.Any(e => e.IsShop);
    public bool IsForeign => LotEntries.FirstOrDefault()?.IsForeign ?? false;
    public string LotIdSummary => string.Join(", ", LotEntries.Select(e => e.LotId).Distinct().OrderBy(i => i));
    public string BaseLotIdSummary => string.Join(", ", LotEntries.Select(e => e.BaseLotId).Distinct().OrderBy(i => i));
    public string CheckedText => IsChecked ? "Picked" : "Pending";
    public string SpoilerDisplay => string.IsNullOrWhiteSpace(SpoilerItemName) ? "(not loaded)" : SpoilerItemName;

    partial void OnIsCheckedChanged(bool value)
    {
        OnPropertyChanged(nameof(CheckedText));
    }

    partial void OnSpoilerItemNameChanged(string value)
    {
        OnPropertyChanged(nameof(SpoilerDisplay));
    }
}
