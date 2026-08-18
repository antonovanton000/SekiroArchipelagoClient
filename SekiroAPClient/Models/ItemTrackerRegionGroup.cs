using System.Collections.ObjectModel;
using System.Linq;

namespace SekiroAPClient.Models;

public class ItemTrackerRegionGroup
{
    public string RegionName { get; init; } = "";
    public ObservableCollection<ItemTrackerEntry> Entries { get; init; } = [];
    public string PickedSummary => $"{Entries.Count(e => e.IsChecked)}/{Entries.Count}";
}
