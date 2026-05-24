using Archipelago.MultiClient.Net.Models;
using RandomizerCommon;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using static RandomizerCommon.LocationData;

public sealed class ArchipelagoMappingSekiroResult
{
    public Dictionary<long, LocationData.SlotKey> LocationToSlot { get; } = new();
    public Dictionary<long, HashSet<int>> LocationToLotIds { get; } = new();
}

public static class ArchipelagoMappingSekiro
{
    private static readonly Dictionary<string, string[]> AreaPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["T"] = new[] { "ashinareservoir_start" },
        ["DT"] = new[] { "ashinaoutskirts_temple" },
        ["AO"] = new[] { "ashinaoutskirts" },
        ["AO/C"] = new[] { "ashinaoutskirts_invasion", "ashinaoutskirts_invasion2" },
        ["HE1"] = new[] { "hirata", "hirata_temple1", "hirata_courtyard1", "hirata_thicketslope", "hirata_sidepath", "hirata_beforetemple", "hirata_pagoda", "hirata_underwater" },
        ["HE2"] = new[] { "hirata_courtyard2", "hirata_temple2" },
        ["AC"] = new[] { "ashinacastle" },
        ["AC/I"] = new[] { "ashinacastle_invasion" },
        ["AC/C"] = new[] { "ashinacastle_invasion2" },
        ["AR"] = new[] { "ashinareservoir" },
        ["AR/C"] = new[] { "ashinareservoir_invasion2", "ashinareservoir_endfield" },
        ["AD"] = new[] { "dungeon" },
        ["ST"] = new[] { "senpou" },
        ["SV"] = new[] { "sunkenvalley" },
        ["SVP"] = new[] { "sunkenvalley_passage", "bodhisattva", "sunkenvalley_serpent" },
        ["PP"] = new[] { "poisonpool", "ashina_depths", "guardianape" },
        ["HF"] = new[] { "hiddenforest" },
        ["MV"] = new[] { "mibuvillage" },
        ["FP1"] = new[] { "fountainhead", "fountainhead_surface", "fountainhead_bridge" },
        ["FP2"] = new[] { "fountainhead", "fountainhead_carp" },
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "at", "behind", "before", "below", "beside", "between",
        "by", "from", "in", "inside", "into", "left", "near", "next", "of", "on", "onto",
        "opposite", "outside", "right", "the", "to", "top", "under", "underwater", "up",
        "upper", "with", "after", "area", "idol", "drop", "enemy", "miniboss", "boss",
        "corpse", "chest", "item", "group"
    };

    //public static ArchipelagoMappingSekiroResult ArchipelagoSlotsSekiro(
    //Dictionary<long, string> apIdsToKeys,
    //AnnotationData ann,
    //LocationData data,
    //IReadOnlyList<ScoutedItemInfo> locations)
    //{
    //    var result = new ArchipelagoMappingSekiroResult();

    //    foreach (var loc in locations)
    //    {
    //        if (!apIdsToKeys.TryGetValue(loc.LocationId, out var annKey))
    //            continue;

    //        if (!ann.SlotsByAnnotationsKey.TryGetValue(annKey, out var slotAnn))
    //            continue;

    //        var scope = slotAnn.LocationScope;

    //        if (!data.Locations.TryGetValue(scope, out var allSlotsInScope) || allSlotsInScope.Count == 0)
    //            continue;

    //        var baseSlots = data.Location(scope);
    //        if (baseSlots == null || baseSlots.Count == 0)
    //        {
    //            result.LocationToSlot[loc.LocationId] = allSlotsInScope[0];
    //            continue;
    //        }          
            
    //        if (baseSlots.Count == 1)
    //        {
    //            var baseSlot = baseSlots[0];               
    //            result.LocationToSlot[loc.LocationId] = baseSlots[0];
    //            continue;
    //        }
            
    //        var eventId = ExtractEventIdFromAnnotationKey(annKey);

    //        var matched = baseSlots.FirstOrDefault(s =>
    //        {
    //            var itemLoc = data.Location(s);
    //            return itemLoc != null &&
    //                   itemLoc.Scope.EventID == eventId;
    //        });

    //        if (matched != null)
    //        {
    //            result.LocationToSlot[loc.LocationId] = matched;
    //        }
    //    }

    //    return result;
    //}

    public static ArchipelagoMappingSekiroResult ArchipelagoSlotsSekiro(
     Dictionary<long, string> apIdsToKeys,
     AnnotationData ann,
     LocationData data,
     IReadOnlyList<ScoutedItemInfo> locations)
    {
        var result = new ArchipelagoMappingSekiroResult();
        int exactKeyCount = 0;
        int overrideKeyCount = 0;
        int fallbackKeyCount = 0;
        int fuzzyFallbackKeyCount = 0;
        int missingKeyCount = 0;
        int missingSlotCount = 0;
        var locationOverrides = LoadLocationOverrides();
        var archipelagoFallback = BuildArchipelagoFallback(ann);

        foreach (var loc in locations)
        {
            bool usedFallback = false;
            bool usedFuzzyFallback = false;
            bool usedOverride = false;
            string? annKey = null;
            if (locationOverrides.TryGetValue(loc.LocationId, out annKey))
            {
                usedOverride = true;
            }
            else if ((annKey = GetLocationNameOverride(loc.LocationName)) != null)
            {
                usedOverride = true;
            }
            else if (!apIdsToKeys.TryGetValue(loc.LocationId, out annKey))
            {
                annKey = ResolveAnnotationKeyFromArchipelagoOrder(loc.LocationName, archipelagoFallback);
                if (annKey != null)
                {
                    usedFallback = true;
                }
                else
                {
                    annKey = ResolveAnnotationKeyFromLocationName(loc.LocationName, ann);
                    usedFuzzyFallback = annKey != null;
                }
            }

            if (annKey == null)
            {
                missingKeyCount++;
                Console.WriteLine($"[AP-MAP][NO-KEY] {loc.LocationId}: {loc.LocationName}");
                continue;
            }

            if (!ann.SlotsByAnnotationsKey.TryGetValue(annKey, out var slotAnn))
            {
                missingSlotCount++;
                Console.WriteLine($"[AP-MAP][NO-ANNOTATION] {loc.LocationId}: key={annKey} location={loc.LocationName}");
                continue;
            }

            if (usedOverride)
            {
                overrideKeyCount++;
                Console.WriteLine($"[AP-MAP][OVERRIDE] {loc.LocationId}: {loc.LocationName} -> {annKey}");
            }
            else if (usedFallback)
            {
                fallbackKeyCount++;
                Console.WriteLine($"[AP-MAP][FALLBACK] {loc.LocationId}: {loc.LocationName} -> {annKey}");
            }
            else if (usedFuzzyFallback)
            {
                fuzzyFallbackKeyCount++;
                Console.WriteLine($"[AP-MAP][FUZZY-FALLBACK] {loc.LocationId}: {loc.LocationName} -> {annKey}");
            }
            else
            {
                exactKeyCount++;
            }

            var scope = slotAnn.LocationScope;

            if (!data.Locations.TryGetValue(scope, out var allSlotsInScope) || allSlotsInScope.Count == 0)
            {
                Console.WriteLine($"[AP-MAP][NO-SCOPE] {loc.LocationId}: key={annKey} scope={scope} location={loc.LocationName}");
                continue;
            }

            var baseSlots = data.Location(scope);
            if (baseSlots == null || baseSlots.Count == 0)
            {
                var fallback = allSlotsInScope[0];
                result.LocationToSlot[loc.LocationId] = fallback;
                continue;
            }

            // Item name from the AP location
            var apItemName = ExtractApItemName(loc.LocationName);

            // DebugText line that contains this item
            var matchingLines = slotAnn.DebugText?
            .Where(t =>
            {
                if (string.IsNullOrWhiteSpace(t)) return false;
                var left = t.Split(" - ")[0].Trim();

                return left.Equals(apItemName, StringComparison.OrdinalIgnoreCase)
                    || left.StartsWith(apItemName + " x", StringComparison.OrdinalIgnoreCase)
                    || left.StartsWith(apItemName + "(", StringComparison.OrdinalIgnoreCase)
                    || left.StartsWith(apItemName + " ", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

            var ids = new HashSet<int>();

            if (matchingLines != null)
            {
                foreach (var line in matchingLines)
                {
                    foreach (var id in ExtractIdsFromDebugLine(line))
                        ids.Add(id);
                }
            }

            ApplyLocationSpecificLotExpansion(loc.LocationName, ids);

            result.LocationToLotIds[loc.LocationId] = ids;

            // Select the SlotKey that contains at least one of these IDs
            LocationData.SlotKey? matched = null;
            if (ids.Count == 0 && baseSlots.Count == 1)
            {
                matched = baseSlots[0];
            }
            else
            {
                foreach (var s in baseSlots)
                {
                    var itemLoc = data.Location(s);
                    if (itemLoc == null) continue;

                    if (itemLoc.Keys.Any(k =>
                        (k.Type == LocationKey.LocationType.LOT ||
                         k.Type == LocationKey.LocationType.SHOP) &&
                        ids.Contains((int)k.ID)))
                    {
                        matched = s;
                        break;
                    }
                }
            }

            if (matched == null)
            {
                Console.WriteLine($"[AP-MAP][NO-LOT-MATCH] {loc.LocationId}: key={annKey} item={apItemName} ids={string.Join(",", ids)} location={loc.LocationName}");
                continue;
            }

            result.LocationToSlot[loc.LocationId] = matched;
        }

        Console.WriteLine($"[AP-MAP] exact={exactKeyCount}, override={overrideKeyCount}, fallback={fallbackKeyCount}, fuzzyFallback={fuzzyFallbackKeyCount}, noKey={missingKeyCount}, noAnnotation={missingSlotCount}, mapped={result.LocationToSlot.Count}/{locations.Count}");
        return result;
    }

    private static string? GetLocationNameOverride(string locationName)
    {
        string normalized = NormalizeText(locationName);

        // apworld names this as the Tengu tower ground-floor droplet, while the
        // local randomizer annotation describes the same invasion-2 pickup as
        // the fortress building after Demon of Hatred. Keep the AP location on
        // its real ItemLotParam row instead of falling through to Shigekichi.
        if (normalized.Contains("ao/c:")
            && normalized.Contains("dragon's blood droplet")
            && normalized.Contains("inside tengu tower"))
        {
            return "00,0:51100850::";
        }

        return null;
    }

    private static void ApplyLocationSpecificLotExpansion(string locationName, HashSet<int> ids)
    {
        string normalized = NormalizeText(locationName);

        // The annotation key 07,0:50006320:: contains both vanilla Holy Chapter:
        // Infested sources. The AP location represents the check, so patch both
        // the Green Robed Infested NPC reward and the underwater pond treasure.
        if (normalized.Contains("holy chapter infested")
            && normalized.Contains("underwater")
            && normalized.Contains("carp pond"))
        {
            ids.Add(63200);
            ids.Add(2000820);
        }
    }

    private sealed record ParsedLocation(string Prefix, string ItemName, string Detail, string Original);
    private sealed record ArchipelagoFallbackCandidate(
        string Key,
        string ItemName,
        bool IsShopLike,
        HashSet<string> Tags);
    private sealed record ArchipelagoFallback(
        Dictionary<string, List<ArchipelagoFallbackCandidate>> KeysByItem,
        Dictionary<(string Prefix, string ItemName), Queue<ArchipelagoFallbackCandidate>> KeysByRegionAndItem);

    private static Dictionary<long, string> LoadLocationOverrides()
    {
        string appLocation = AppContext.BaseDirectory;
        string path = Path.Combine(appLocation, "dists", "Base", "ap_location_overrides.json");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(appLocation, "..", "..", "..", "dists", "Base", "ap_location_overrides.json"));

        if (!File.Exists(path))
            return new Dictionary<long, string>();

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? new Dictionary<string, string>();

        return raw
            .Where(kv => long.TryParse(kv.Key, out _))
            .ToDictionary(kv => long.Parse(kv.Key), kv => kv.Value);
    }

    private static ArchipelagoFallback BuildArchipelagoFallback(AnnotationData ann)
    {
        var keysByItem = new Dictionary<string, List<ArchipelagoFallbackCandidate>>(StringComparer.OrdinalIgnoreCase);
        var keysByRegionAndItem = new Dictionary<(string Prefix, string ItemName), Queue<ArchipelagoFallbackCandidate>>();

        foreach (var slot in ann.SlotsByAnnotationsKey.Values)
        {
            string? archipelagoRegion = null;
            if (!string.IsNullOrWhiteSpace(slot.Area) && ann.Areas.TryGetValue(slot.Area, out var area))
                archipelagoRegion = area.Archipelago;

            if (slot.DebugText == null)
                continue;

            foreach (var itemLine in slot.DebugText)
            {
                string itemName = ExtractDebugItemName(itemLine);
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                var candidate = new ArchipelagoFallbackCandidate(
                    slot.Key,
                    itemName,
                    IsShopLikeFallbackCandidate(slot, itemLine),
                    slot.TagList?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                if (!keysByItem.TryGetValue(itemName, out var itemKeys))
                {
                    itemKeys = new List<ArchipelagoFallbackCandidate>();
                    keysByItem[itemName] = itemKeys;
                }
                itemKeys.Add(candidate);

                if (string.IsNullOrWhiteSpace(archipelagoRegion))
                    continue;

                var regionKey = (archipelagoRegion, itemName);
                if (!keysByRegionAndItem.TryGetValue(regionKey, out var regionKeys))
                {
                    regionKeys = new Queue<ArchipelagoFallbackCandidate>();
                    keysByRegionAndItem[regionKey] = regionKeys;
                }
                regionKeys.Enqueue(candidate);
            }
        }

        return new ArchipelagoFallback(keysByItem, keysByRegionAndItem);
    }

    private static string? ResolveAnnotationKeyFromArchipelagoOrder(string locationName, ArchipelagoFallback fallback)
    {
        var parsed = ParseLocationName(locationName);
        if (parsed == null)
            return null;

        bool locationLooksShopLike = LocationLooksShopLike(parsed.Detail);

        if (fallback.KeysByItem.TryGetValue(parsed.ItemName, out var itemKeys) && itemKeys.Count == 1)
        {
            var candidate = itemKeys[0];
            if (CandidateAllowedForLocation(candidate, locationLooksShopLike))
                return candidate.Key;
        }

        var regionKey = (parsed.Prefix, parsed.ItemName);
        if (!fallback.KeysByRegionAndItem.TryGetValue(regionKey, out var regionKeys) || regionKeys.Count == 0)
            return null;

        var skipped = new List<ArchipelagoFallbackCandidate>();

        while (regionKeys.Count > 0)
        {
            var candidate = regionKeys.Dequeue();
            if (CandidateAllowedForLocation(candidate, locationLooksShopLike))
            {
                foreach (var skippedCandidate in skipped)
                    regionKeys.Enqueue(skippedCandidate);
                return candidate.Key;
            }

            skipped.Add(candidate);
        }

        foreach (var skippedCandidate in skipped)
            regionKeys.Enqueue(skippedCandidate);

        return null;
    }

    private static bool CandidateAllowedForLocation(ArchipelagoFallbackCandidate candidate, bool locationLooksShopLike)
    {
        if (candidate.Tags.Contains("ng+")
            || candidate.Tags.Contains("unused")
            || candidate.Tags.Contains("remove")
            || candidate.Tags.Contains("norandom"))
            return false;

        string noRandomTag = "norandom:" + CompactToken(candidate.ItemName);
        if (candidate.Tags.Contains(noRandomTag))
            return false;

        if (candidate.IsShopLike && !locationLooksShopLike)
            return false;

        return true;
    }

    private static bool LocationLooksShopLike(string detail)
    {
        string normalized = NormalizeText(detail);
        string[] shopHints =
        {
            "fujioka", "anayama", "blackhat", "badger", "memorial mob", "pot noble",
            "harunaga", "koremori", "peddler", "info broker", "sold", "shop"
        };
        return shopHints.Any(normalized.Contains);
    }

    private static bool IsShopLikeFallbackCandidate(AnnotationData.SlotAnnotation slot, string debugLine)
    {
        string text = NormalizeText($"{slot.Text} {debugLine}");
        return text.Contains(" for ") && text.Contains(" sen")
            || text.Contains("memorial mob")
            || text.Contains("peddler")
            || text.Contains("info broker")
            || text.Contains("pot noble");
    }

    private static string? ResolveAnnotationKeyFromLocationName(string locationName, AnnotationData ann)
    {
        var parsed = ParseLocationName(locationName);
        if (parsed == null)
            return null;

        var candidates = ann.SlotsByAnnotationsKey.Values
            .Select(slot => new
            {
                Slot = slot,
                Lines = MatchingDebugLines(slot, parsed.ItemName).ToList(),
                DebugText = slot.DebugText == null ? "" : string.Join(" ", slot.DebugText)
            })
            .Select(c => new
            {
                c.Slot,
                c.Lines,
                Score = ScoreFallbackCandidate(parsed, c.Slot, c.Lines, c.DebugText)
            })
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Slot.Key, StringComparer.Ordinal)
            .ToList();

        var best = candidates.FirstOrDefault();
        if (best == null || best.Score < 55)
            return null;

        return best.Slot.Key;
    }

    private static ParsedLocation? ParseLocationName(string locationName)
    {
        var prefixEnd = locationName.IndexOf(": ", StringComparison.Ordinal);
        if (prefixEnd < 0)
            return null;

        string prefix = locationName[..prefixEnd].Trim();
        string rest = locationName[(prefixEnd + 2)..].Trim();
        string itemName = rest;
        string detail = "";

        var dash = rest.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0)
        {
            itemName = rest[..dash].Trim();
            detail = rest[(dash + 3)..].Trim();
        }

        return new ParsedLocation(prefix, NormalizeItemName(itemName), detail, locationName);
    }

    private static int ScoreFallbackCandidate(ParsedLocation parsed, AnnotationData.SlotAnnotation slot, List<string> lines, string fullDebugText)
    {
        int score = 0;
        bool areaMatched = false;

        bool hasKnownArea = AreaPrefixes.TryGetValue(parsed.Prefix, out var areaPrefixes);
        if (hasKnownArea && areaPrefixes.Any(prefix => AreaMatches(slot.Area, prefix)))
        {
            areaMatched = true;
            score += 35;
        }

        string annotationText = slot.Text ?? "";
        string matchedDebugText = string.Join(" ", lines);
        string searchableText = $"{annotationText} {fullDebugText}";

        if (lines.Count > 0)
            score += 45;

        if (NormalizeText(searchableText).Contains(parsed.ItemName, StringComparison.OrdinalIgnoreCase))
            score += 30;

        score += WordOverlapScore(parsed.Detail, annotationText, 4, 36);
        score += WordOverlapScore(parsed.Detail, fullDebugText, 2, 20);
        score += WordOverlapScore(parsed.ItemName, searchableText, 5, 25);
        score += WordOverlapScore(parsed.Original, searchableText, 2, 24);

        string normalizedDetail = NormalizeText(parsed.Detail);
        string normalizedAnnotation = NormalizeText(annotationText);
        if (normalizedDetail.Length > 0 && normalizedAnnotation.Contains(normalizedDetail, StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        if (ContainsMerchantHint(parsed.Detail, fullDebugText) || ContainsMerchantHint(parsed.Detail, annotationText))
        {
            score += 25;
        }

        if (slot.TagList != null)
        {
            if (parsed.Detail.Contains("boss", StringComparison.OrdinalIgnoreCase) && slot.TagList.Contains("boss"))
                score += 15;
            if (parsed.Detail.Contains("miniboss", StringComparison.OrdinalIgnoreCase) && slot.TagList.Contains("miniboss"))
                score += 15;
            if (parsed.Detail.Contains("underwater", StringComparison.OrdinalIgnoreCase) && slot.TagList.Contains("underwater"))
                score += 10;
        }

        if (!areaMatched)
            score -= 30;

        return score;
    }

    private static bool AreaMatches(string? slotArea, string expectedArea)
    {
        if (string.IsNullOrWhiteSpace(slotArea))
            return false;

        if (string.Equals(expectedArea, "hirata", StringComparison.OrdinalIgnoreCase))
            return string.Equals(slotArea, expectedArea, StringComparison.OrdinalIgnoreCase);

        return string.Equals(slotArea, expectedArea, StringComparison.OrdinalIgnoreCase)
            || slotArea.StartsWith(expectedArea + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> MatchingDebugLines(AnnotationData.SlotAnnotation slot, string itemName)
    {
        if (slot.DebugText == null)
            yield break;

        foreach (var line in slot.DebugText)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string left = line.Split(" - ")[0].Trim(' ', '\'', '"');
            if (NormalizeItemName(left).Equals(itemName, StringComparison.OrdinalIgnoreCase))
                yield return line;
        }
    }

    private static string ExtractDebugItemName(string debugLine)
    {
        if (string.IsNullOrWhiteSpace(debugLine))
            return "";

        var dash = debugLine.IndexOf(" - ", StringComparison.Ordinal);
        var left = dash >= 0 ? debugLine[..dash] : debugLine;
        return NormalizeItemName(left.Trim(' ', '\'', '"'));
    }

    private static int WordOverlapScore(string left, string right, int perWord, int max)
    {
        var leftWords = Tokenize(left);
        if (leftWords.Count == 0)
            return 0;

        var rightWords = Tokenize(right);
        int overlap = leftWords.Count(word => rightWords.Contains(word));
        return Math.Min(max, overlap * perWord);
    }

    private static HashSet<string> Tokenize(string text)
    {
        return Regex.Matches(NormalizeText(text), "[a-z0-9]+")
            .Select(m => m.Value)
            .Where(word => word.Length > 2 && !StopWords.Contains(word))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeItemName(string itemName)
    {
        itemName = Regex.Replace(itemName, @"\s+x\d+$", "", RegexOptions.IgnoreCase);
        return NormalizeText(itemName);
    }

    private static string NormalizeText(string text)
    {
        return Regex.Replace(text ?? "", @"\s+", " ")
            .Replace("’", "'")
            .Trim()
            .ToLowerInvariant();
    }

    private static string CompactToken(string text)
    {
        return Regex.Replace(NormalizeText(text), @"[^a-z0-9]+", "");
    }

    private static bool ContainsMerchantHint(string detail, string text)
    {
        string normalizedDetail = NormalizeText(detail);
        string normalizedText = NormalizeText(text);
        string[] hints =
        {
            "fujioka", "anayama", "blackhat", "memorial mob", "pot noble",
            "harunaga", "koremori", "dungeon memorial", "toxic memorial",
            "battlefield memorial", "crow's bed", "offering box"
        };
        return hints.Any(hint => normalizedDetail.Contains(hint) && normalizedText.Contains(hint));
    }


    // "DT: Gourd Seed - Fujioka the Info Broker" -> "Gourd Seed"
    private static string ExtractApItemName(string apLocationName)
    {
        // Remove location prefix
        var idx = apLocationName.IndexOf(": ", StringComparison.Ordinal);
        var s = idx >= 0 ? apLocationName[(idx + 2)..] : apLocationName;

        // Standard format: "Item - place"
        var dash = s.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0)
            return s[..dash].Trim();

        // Format: "Item: place" (without " - ")
        var colon = s.IndexOf(':');
        if (colon >= 0)
            return s[..colon].Trim();

        return s.Trim();
    }

    // "Gourd Seed - 1100400[...], 1100450[...]" -> {1100400,1100450}
    private static HashSet<int> ExtractIdsFromDebugLine(string debugLine)
    {
        var set = new HashSet<int>();

        var dash = debugLine.IndexOf(" - ", StringComparison.Ordinal);
        if (dash < 0) return set;

        var tail = debugLine[(dash + 3)..];

        foreach (Match match in Regex.Matches(tail, @"(?:^|,\s*)(-?\d+)\["))
        {
            if (int.TryParse(match.Groups[1].Value, out var id))
                set.Add(id);
        }

        return set;
    }

    private static int ExtractEventIdFromAnnotationKey(string key)
    {
        // "00,0:50006160::"
        var parts = key.Split(':');
        if (parts.Length < 2)
            return -1;

        if (int.TryParse(parts[1], out var eventId))
            return eventId;

        return -1;
    }
}
