using System.IO;

namespace SekiroAPClient.Classes;

public sealed class ItemTransferLogger
{
    private readonly string _path;
    private readonly Func<bool> _isEnabled;
    private readonly object _lock = new();

    public ItemTransferLogger(string path, Func<bool> isEnabled)
    {
        _path = path;
        _isEnabled = isEnabled;
    }

    public string Path => _path;

    public void Log(string message)
    {
        if (!_isEnabled())
            return;

        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
