using System;
using System.Text.RegularExpressions;

public static class ItemCountParser
{
    // Matches: " ... x2" / "...x2" / "... x12   "
    // Only at end of string.
    private static readonly Regex TrailingCountRegex =
        new Regex(@"\s*x(?<count>\d+)\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Extracts trailing "xN" from an item name. If not present, returns 1.
    /// Examples:
    ///   "Mibu Possession Balloon x2" -> 2
    ///   "Oil x5" -> 5
    ///   "Some Item x2 (from other world)" -> 1 (because x2 is not at end)
    /// </summary>
    public static int GetCountFromItemName(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return 1;

        var m = TrailingCountRegex.Match(itemName);
        if (!m.Success)
            return 1;

        if (!int.TryParse(m.Groups["count"].Value, out var count) || count == 0)
            return 1;

        return count;
    }

    /// <summary>
    /// Optional helper: returns the name without the trailing "xN".
    /// </summary>
    public static string StripTrailingCount(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return itemName ?? "";

        return TrailingCountRegex.Replace(itemName, "").TrimEnd();
    }
}