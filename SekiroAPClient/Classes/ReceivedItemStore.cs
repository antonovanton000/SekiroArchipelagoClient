using System.IO;
using System.Text.Json;

namespace SekiroAPClient.Classes;

public sealed class ReceivedItemStore
{
    public const int DeliveryFlagBase = 79_200_000;
    public const int DeliveryFlagCount = 2_000;

    private readonly string _path;
    private readonly Dictionary<string, ReceivedItemRecord> _records = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _nextDeliveryOffset;

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
            return _records.TryGetValue(key, out var record) && record.Delivered;
    }

    public ReceivedItemRecord GetOrCreateDelivery(string key)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(key, out var existing))
            {
                if (existing.DeliveryFlagId <= 0)
                    existing.DeliveryFlagId = AllocateDeliveryFlagId();

                return existing;
            }

            var record = new ReceivedItemRecord
            {
                Key = key,
                DeliveryFlagId = AllocateDeliveryFlagId(),
                Delivered = false
            };

            _records[key] = record;
            return record;
        }
    }

    public bool TryMark(string key)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(key, out var existing))
            {
                if (existing.Delivered)
                    return false;

                existing.Delivered = true;
                return true;
            }

            _records[key] = new ReceivedItemRecord
            {
                Key = key,
                DeliveryFlagId = 0,
                Delivered = true
            };
            return true;
        }
    }

    public bool MarkDeliveredByFlagId(int deliveryFlagId)
    {
        lock (_lock)
        {
            var record = _records.Values.FirstOrDefault(r => r.DeliveryFlagId == deliveryFlagId);
            if (record == null)
                return false;

            record.Delivered = true;
            if (!string.IsNullOrWhiteSpace(record.SingletonKey))
            {
                _records[record.SingletonKey] = new ReceivedItemRecord
                {
                    Key = record.SingletonKey,
                    DeliveryFlagId = 0,
                    Delivered = true
                };
            }
            return true;
        }
    }

    public void MarkDelivered(string key)
    {
        lock (_lock)
        {
            if (_records.TryGetValue(key, out var record))
            {
                record.Delivered = true;
            }
        }
    }

    public void UpdateDeliveryPayload(string key, int fullId, int goodId, int quantity, int eventId, string itemName)
    {
        lock (_lock)
        {
            if (!_records.TryGetValue(key, out var record))
                return;

            record.FullId = fullId;
            record.GoodId = goodId;
            record.Quantity = Math.Max(1, quantity);
            record.EventId = eventId;
            record.ItemName = itemName;
        }
    }

    public List<ReceivedItemRecord> GetPendingDeliveries()
    {
        lock (_lock)
        {
            return _records.Values
                .Where(r => !r.Delivered && r.DeliveryFlagId > 0 && r.FullId != 0 && r.Quantity > 0)
                .Select(r => r.Clone())
                .ToList();
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            var data = new StoreData
            {
                NextDeliveryOffset = _nextDeliveryOffset,
                Records = _records.Values
                    .OrderBy(r => r.DeliveryFlagId <= 0 ? int.MaxValue : r.DeliveryFlagId)
                    .ThenBy(r => r.Key, StringComparer.Ordinal)
                    .ToList()
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private int AllocateDeliveryFlagId()
    {
        if (_nextDeliveryOffset >= DeliveryFlagCount)
            throw new InvalidOperationException($"Received item delivery flag range exhausted ({DeliveryFlagBase}-{DeliveryFlagBase + DeliveryFlagCount - 1}).");

        return DeliveryFlagBase + _nextDeliveryOffset++;
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
                LoadLegacyStringArray(doc.RootElement);
                return;
            }

            var data = JsonSerializer.Deserialize<StoreData>(json);
            if (data?.Records == null)
                return;

            foreach (var record in data.Records.Where(r => !string.IsNullOrWhiteSpace(r.Key)))
            {
                _records[record.Key] = record;
            }

            int maxOffset = _records.Values
                .Where(r => r.DeliveryFlagId >= DeliveryFlagBase && r.DeliveryFlagId < DeliveryFlagBase + DeliveryFlagCount)
                .Select(r => r.DeliveryFlagId - DeliveryFlagBase + 1)
                .DefaultIfEmpty(0)
                .Max();

            _nextDeliveryOffset = Math.Max(data.NextDeliveryOffset, maxOffset);
        }
        catch
        {
            // Ignore corrupt store files. The game-side delivery flags are the
            // source of truth for new records after the store is recreated.
        }
    }

    private void LoadLegacyStringArray(JsonElement root)
    {
        foreach (var element in root.EnumerateArray())
        {
            string? key = element.GetString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            _records[key] = new ReceivedItemRecord
            {
                Key = key,
                DeliveryFlagId = 0,
                Delivered = true
            };
        }
    }

    private sealed class StoreData
    {
        public int NextDeliveryOffset { get; set; }
        public List<ReceivedItemRecord> Records { get; set; } = new();
    }
}

public sealed class ReceivedItemRecord
{
    public string Key { get; set; } = "";
    public int DeliveryFlagId { get; set; }
    public bool Delivered { get; set; }
    public string? SingletonKey { get; set; }
    public int FullId { get; set; }
    public int GoodId { get; set; }
    public int Quantity { get; set; }
    public int EventId { get; set; }
    public string? ItemName { get; set; }

    public ReceivedItemRecord Clone()
    {
        return new ReceivedItemRecord
        {
            Key = Key,
            DeliveryFlagId = DeliveryFlagId,
            Delivered = Delivered,
            SingletonKey = SingletonKey,
            FullId = FullId,
            GoodId = GoodId,
            Quantity = Quantity,
            EventId = EventId,
            ItemName = ItemName
        };
    }
}
