using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AppUpdater.Classes;

public static class DownloadHelper
{
    public static async Task DownloadFileWithProgressAsync(
        HttpClient http,
        string url,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Важно: сначала получаем заголовки, потом читаем стрим
        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength; // может быть null

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        long totalRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            if (contentLength.HasValue && contentLength.Value > 0)
            {
                double percent = (double)totalRead / contentLength.Value * 100.0;
                progress?.Report(percent);
            }
            else
            {
                // Если размер неизвестен — можно репортить -1 или просто не репортить
                // progress?.Report(-1);
            }
        }

        progress?.Report(100.0);
    }
}
