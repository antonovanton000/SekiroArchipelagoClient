using System.IO;
using System.Text.Json;

namespace SekiroAPClient.Classes;

public sealed class ReceivedItemStore
{
    private readonly string _path;
    private readonly HashSet<string> _deliveredKeys = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ReceivedItemStore(string path)
    {
        _path = path;
        Load();
    }

    public static string MakeKey(long itemId, long locationId, int fromPlayerSlot)
        => $"{itemId}:{locationId}:{fromPlayerSlot}";

    public bool Has(string key)
    {
        lock (_lock)
            return _deliveredKeys.Contains(key);
    }

    public bool TryMark(string key)
    {
        lock (_lock)
            return _deliveredKeys.Add(key);
    }

    public void MarkDelivered(string key)
    {
        lock (_lock)
            _deliveredKeys.Add(key);
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var keys = _deliveredKeys
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            File.WriteAllText(_path, JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true }));
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

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                LoadStringArray(doc.RootElement);
                return;
            }

            var data = JsonSerializer.Deserialize<LegacyStoreData>(json);
            if (data?.Records == null)
                return;

            foreach (var record in data.Records)
            {
                if (!string.IsNullOrWhiteSpace(record.Key) && record.Delivered)
                    _deliveredKeys.Add(record.Key);
            }
        }
        catch
        {
            // If the local cache is corrupt, ignore it. Archipelago will resend
            // received items and the cache will be rebuilt from scratch.
        }
    }

    private void LoadStringArray(JsonElement root)
    {
        foreach (var element in root.EnumerateArray())
        {
            string? key = element.GetString();
            if (!string.IsNullOrWhiteSpace(key))
                _deliveredKeys.Add(key);
        }
    }

    private sealed class LegacyStoreData
    {
        public List<LegacyReceivedItemRecord> Records { get; set; } = new();
    }

    private sealed class LegacyReceivedItemRecord
    {
        public string Key { get; set; } = "";
        public bool Delivered { get; set; }
    }
}
