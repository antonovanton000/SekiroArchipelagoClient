using System.IO;
using System.Text.Json;

namespace SekiroAPClient.Classes;
public sealed class ReceivedItemStore
{
    private readonly string _path;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ReceivedItemStore(string path)
    {
        _path = path;
        Load();
    }

    // Уникальный ключ на “полученный предмет”
    public static string MakeKey(long itemId, long locationId, int fromPlayerSlot)
        => $"{itemId}:{locationId}:{fromPlayerSlot}";

    public bool Has(string key)
    {
        lock (_lock) return _seen.Contains(key);
    }

    // Возвращает true если это НОВЫЙ предмет (и мы его отметили), false если уже был
    public bool TryMark(string key)
    {
        lock (_lock)
        {
            if (_seen.Contains(key))
                return false;

            _seen.Add(key);
            return true;
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            var data = _seen.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            var json = File.ReadAllText(_path);
            var arr = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            foreach (var k in arr)
                _seen.Add(k);
        }
        catch
        {
            // если файл битый — можно удалить или игнорировать
        }
    }
}
