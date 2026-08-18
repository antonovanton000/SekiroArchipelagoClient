using System.IO;

namespace SekiroAPClient.Classes;

public sealed class ItemTransferLogger
{
    private string _path;
    private bool _isEnabled;
    private object _lock = new();

    public ItemTransferLogger(string path, bool isEnabled)
    {
        _path = path;
        _isEnabled = isEnabled;
    }

    public string Path => _path;

    public void Log(string message)
    {
        if (!_isEnabled)
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
