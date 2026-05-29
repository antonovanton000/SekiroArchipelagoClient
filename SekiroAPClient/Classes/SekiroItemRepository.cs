using SekiroAPClient.Models;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace SekiroAPClient.Classes;

public static class SekiroItemRepository
{
    public static List<RawSekiroItem> LoadItems()
    {
        var resourceUri = new Uri("SekiroData/items.json", UriKind.Relative);
        var streamResourceInfo = Application.GetResourceStream(resourceUri);
        if (streamResourceInfo == null)
            throw new FileNotFoundException("Resource SekiroData/items.json not found.");

        using var reader = new StreamReader(streamResourceInfo.Stream);
        var json = reader.ReadToEnd();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<List<RawSekiroItem>>(json, options) ?? [];
    }
}
