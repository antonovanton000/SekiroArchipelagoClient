using System.IO;
using System.Text.Json;

namespace SekiroAPClient.Classes;

public sealed class PendingLocationCheckStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<long, PendingLocationCheckRecord> _records = new();

    public PendingLocationCheckStore(string path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<PendingLocationCheckRecord> Snapshot()
    {
        lock (_lock)
            return _records.Values.OrderBy(record => record.LocationId).ToList();
    }

    public void Add(IEnumerable<long> locationIds, long lotId, int goodId, int quantity, bool isFromShop)
    {
        lock (_lock)
        {
            foreach (var locationId in locationIds)
            {
                if (_records.ContainsKey(locationId))
                    continue;

                _records[locationId] = new PendingLocationCheckRecord
                {
                    LocationId = locationId,
                    LotId = lotId,
                    GoodId = goodId,
                    Quantity = quantity,
                    IsFromShop = isFromShop,
                    CreatedUtc = DateTime.UtcNow
                };
            }

            SaveLocked();
        }
    }

    public void Remove(IEnumerable<long> locationIds)
    {
        lock (_lock)
        {
            bool changed = false;
            foreach (var locationId in locationIds)
                changed |= _records.Remove(locationId);

            if (changed)
                SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var records = JsonSerializer.Deserialize<List<PendingLocationCheckRecord>>(json) ?? [];
            foreach (var record in records)
            {
                if (record.LocationId > 0)
                    _records[record.LocationId] = record;
            }
        }
        catch
        {
            _records.Clear();
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var records = _records.Values.OrderBy(record => record.LocationId).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class PendingLocationCheckRecord
{
    public long LocationId { get; set; }
    public long LotId { get; set; }
    public int GoodId { get; set; }
    public int Quantity { get; set; }
    public bool IsFromShop { get; set; }
    public DateTime CreatedUtc { get; set; }
}
