namespace SekiroAPClient.Classes;

using System;
using System.IO;

public static class ModEngineIniHelper
{
    public static void UpdateModEngineIni(
        string iniPath,
        string newChainDInput8DLLPath,
        string newModOverrideDirectory)
    {
        if (!File.Exists(iniPath))
            throw new FileNotFoundException("modengine.ini file not found", iniPath);

        var lines = new List<string>(File.ReadAllLines(iniPath));

        // 1) Update existing active keys if present
        bool chainUpdated = UpdateActiveKey(lines, "chainDInput8DLLPath", newChainDInput8DLLPath);
        bool overrideUpdated = UpdateActiveKey(lines, "modOverrideDirectory", newModOverrideDirectory);

        // 2) Insert if missing OR only commented
        if (!chainUpdated)
        {
            InsertKeyInSectionAfterAnchor(
                lines,
                sectionName: "misc",
                anchorKey: "skipLogos",
                keyToInsert: "chainDInput8DLLPath",
                valueToInsert: newChainDInput8DLLPath);
        }

        if (!overrideUpdated)
        {
            InsertKeyInSectionAfterAnchor(
                lines,
                sectionName: "files",
                anchorKey: "useModOverrideDirectory",
                keyToInsert: "modOverrideDirectory",
                valueToInsert: newModOverrideDirectory);
        }

        File.WriteAllLines(iniPath, lines);
    }

    // Updates only NON-commented occurrences of key. Returns true if updated at least one.
    private static bool UpdateActiveKey(List<string> lines, string key, string newValue)
    {
        bool updated = false;

        for (int i = 0; i < lines.Count; i++)
        {
            string raw = lines[i];
            string trimmed = raw.TrimStart();

            // Skip empty and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                continue;

            if (StartsWithIniKey(trimmed, key))
            {
                lines[i] = SetIniValue(raw, key, newValue);
                updated = true;
            }
        }

        return updated;
    }

    private static void InsertKeyInSectionAfterAnchor(
        List<string> lines,
        string sectionName,
        string anchorKey,
        string keyToInsert,
        string valueToInsert)
    {
        // Ensure section exists; if not, create at end with a blank line before (if needed)
        if (!TryFindSection(lines, sectionName, out int sectionStart, out int sectionEndExclusive))
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);

            sectionStart = lines.Count;
            lines.Add($"[{sectionName}]");
            sectionEndExclusive = lines.Count; // currently end
        }

        // Refresh section bounds (in case we created it)
        TryFindSection(lines, sectionName, out sectionStart, out sectionEndExclusive);

        // If there is already an ACTIVE key line in section, we shouldn't insert (safety)
        if (HasActiveKeyInRange(lines, keyToInsert, sectionStart + 1, sectionEndExclusive))
            return;

        // Determine insertion index: after anchorKey (active or commented) if present, else end of section
        int insertAt = FindLineWithKeyInRange(lines, anchorKey, sectionStart + 1, sectionEndExclusive);
        if (insertAt >= 0)
            insertAt = insertAt + 1;
        else
            insertAt = sectionEndExclusive; // end of section

        // Keep formatting simple and consistent with your previous output
        string newLine = $"{keyToInsert}=\"{valueToInsert}\"";

        // If we are inserting at end-of-section and there is a section header right there,
        // it means we insert before next section header.
        lines.Insert(insertAt, newLine);
    }

    private static bool TryFindSection(List<string> lines, string sectionName, out int startIndex, out int endExclusive)
    {
        startIndex = -1;
        endExclusive = lines.Count;

        string target = $"[{sectionName}]";

        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                startIndex = i;

                // endExclusive = next section header or end of file
                for (int j = i + 1; j < lines.Count; j++)
                {
                    string tj = lines[j].Trim();
                    if (tj.StartsWith("[") && tj.EndsWith("]"))
                    {
                        endExclusive = j;
                        break;
                    }
                }

                return true;
            }
        }

        return false;
    }

    private static bool HasActiveKeyInRange(List<string> lines, string key, int fromInclusive, int toExclusive)
    {
        for (int i = fromInclusive; i < toExclusive && i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                continue;

            if (StartsWithIniKey(trimmed, key))
                return true;
        }
        return false;
    }

    // Finds first line that contains the key as "key =" (active OR commented).
    // Returns index or -1.
    private static int FindLineWithKeyInRange(List<string> lines, string key, int fromInclusive, int toExclusive)
    {
        for (int i = fromInclusive; i < toExclusive && i < lines.Count; i++)
        {
            string trimmed = lines[i].TrimStart();

            // allow commented anchor as well
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // strip leading comment markers for matching anchor
            string match = trimmed;
            while (match.StartsWith(";") || match.StartsWith("#"))
                match = match.Substring(1).TrimStart();

            if (StartsWithIniKey(match, key))
                return i;
        }
        return -1;
    }

    // key match: "key", then optional spaces, then '='
    private static bool StartsWithIniKey(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            return false;

        int idx = key.Length;
        while (idx < line.Length && char.IsWhiteSpace(line[idx])) idx++;
        return idx < line.Length && line[idx] == '=';
    }

    /// <summary>
    /// Replaces the value after '=' in an ini line,
    /// preserving everything on the left side (including whitespace).
    /// </summary>
    private static string SetIniValue(string originalLine, string key, string newValue)
    {
        int equalsIndex = originalLine.IndexOf('=');
        if (equalsIndex < 0)
            return $"{key}=\"{newValue}\"";

        string left = originalLine.Substring(0, equalsIndex + 1); // "key   ="
        string right = originalLine.Substring(equalsIndex + 1).Trim();

        bool hadQuotes = right.StartsWith("\"") && right.EndsWith("\"");
        return hadQuotes ? $"{left}\"{newValue}\"" : $"{left}{newValue}";
    }
}
