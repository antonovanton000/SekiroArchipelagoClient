using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class StateStorage<TState>
{
    private readonly string _mainPath;
    private readonly string _backupPath;
    private readonly string _tempPath;

    // Ensures that only one save operation can run at a time
    // Prevents concurrent writes and file corruption
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public StateStorage(string mainPath)
    {
        _mainPath = mainPath;
        _backupPath = mainPath + ".bak";
        _tempPath = mainPath + ".tmp";
    }

    public async Task SaveAsync(TState state, CancellationToken cancellationToken = default)
    {
        // Block parallel save operations
        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            // Serialize the state into memory first
            var json = JsonConvert.SerializeObject(state, Formatting.Indented);

            // Write JSON into a temporary file
            // The temporary file is flushed to disk before replacing the main file
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            using (var fs = new FileStream(
                _tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough)) // Forces data to be written directly to disk
            using (var writer = new StreamWriter(fs, encoding))
            {
                await writer.WriteAsync(json);
                await writer.FlushAsync();

                // Ensure all buffered data is physically written to disk
                fs.Flush(true);
            }

            // Atomically replace the main file with the temporary one
            // This guarantees that the file is never partially written
            if (File.Exists(_mainPath))
            {
                // Replace the existing file and create/update a backup
                File.Replace(_tempPath, _mainPath, _backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                // First save: simply move the temporary file to the final location
                File.Move(_tempPath, _mainPath);
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task<TState?> LoadAsync()
    {
        // Try to load the main state file first
        if (File.Exists(_mainPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_mainPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<TState>(json);
            }
            catch
            {
                // If the main file is corrupted or unreadable, fall back to the backup
            }
        }

        // Attempt to load the backup file if the main file failed
        if (File.Exists(_backupPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_backupPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<TState>(json);
            }
            catch
            {
                // Backup file is also invalid – treat as no saved state
            }
        }

        return default;
    }
}
