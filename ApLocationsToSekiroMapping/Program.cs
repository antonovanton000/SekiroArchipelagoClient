using RandomizerCommon;
using SoulsIds; // AnnotationData, GameSpec и т.д.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using static SoulsIds.GameSpec;

namespace SekiroApMappingTool
{
    class Program
    {
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Console.WriteLine("[AP Mapping Tool] Starting…");

            string annotationsPath = "dists\\Base\\annotations.txt";
            string locationsCsvPath = "locations.csv";
            string outputPath = "ap_location_to_annotation.json";

            if (!File.Exists(annotationsPath) || !File.Exists(locationsCsvPath))
            {
                Console.WriteLine("Missing annotations.txt or locations.csv");
                return;
            }

            // 1️⃣ Загружаем игру и annotations
            Console.WriteLine("[1/4] Loading annotations…");
            GameData game = new GameData("dists", FromGame.SDT);
            game.Load();
            SekiroLocationDataScraper scraper = new SekiroLocationDataScraper();
            LocationData data = scraper.FindItems(game);
            AnnotationData ann = new AnnotationData(game, data);
            ann.Load(new RandomizerOptions());

            var regionToArea = BuildRegionToAreas(ann);            

            // 2️⃣ Загружаем locations.csv
            Console.WriteLine("[2/4] Loading locations.csv…");
            var csvLocations = LoadLocationsCsv(locationsCsvPath);

            var cleanupNameToKeys = LoadCleanupSheet("cleanup_sheet.csv");
            // cleanupNameToKeys["AR1: Ornamental Letter - in starting well"] == { "01,0:51120000::" }


            // 3️⃣ Строим mapping
            Console.WriteLine("[3/4] Building mapping…");
            var mapping = BuildMapping(csvLocations, ann, regionToArea, cleanupNameToKeys);

            // 4️⃣ Сохраняем JSON
            File.WriteAllText(
                outputPath,
                JsonSerializer.Serialize(mapping, new JsonSerializerOptions
                {
                    WriteIndented = true
                })
            );

            Console.WriteLine($"[DONE] Written {mapping.Count} mappings to {outputPath}");
        }

        // ===================== CORE LOGIC =====================

        static Dictionary<string, string> BuildMapping(
            List<CsvLocation> locations,
            AnnotationData ann,
            Dictionary<string, List<string>> regionToAreas,
            Dictionary<string, List<string>> cleanupNameToKeys)
        {
            var result = new Dictionary<string, string>();

            int total = 0, direct = 0, heuristic = 0, ambiguous = 0, miss = 0;

            foreach (var loc in locations)
            {
                total++;

                // 0. Магазины пока пропускаем (можно отдельно обработать позже)
                // если хочешь, это условие можно убрать
                // if (loc.IsShop)
                //     continue;

                // 1. Пытаемся найти прямое совпадение по cleanup_sheet
                if (cleanupNameToKeys.TryGetValue(loc.Name, out var keysFromCleanup))
                {
                    if (keysFromCleanup.Count == 1)
                    {
                        result[loc.Name] = keysFromCleanup[0];
                        direct++;
                        continue;
                    }
                    else
                    {
                        // ОДНО имя → несколько res (редко, но бывает)
                        Console.WriteLine($"[CLEANUP-AMBIGUOUS] '{loc.Name}' → {string.Join(", ", keysFromCleanup)}");
                        ambiguous++;
                        continue;
                    }
                }

                // 2. Если в cleanup_sheet прямого совпадения нет — используем старую логику

                if (!regionToAreas.TryGetValue(loc.Region, out var areasInRegion) ||
                    areasInRegion.Count == 0)
                {
                    Log($"[SKIP] Unknown AP region '{loc.Region}' for '{loc.Name}'");
                    miss++;
                    continue;
                }

                var baseCandidates = ann.SlotsByAnnotationsKey.Values
                    .Where(s =>
                        areasInRegion.Contains(s.Area) &&
                        (s.Tags == null || !s.Tags.Contains("norandom")))
                    .ToList();

                if (baseCandidates.Count == 0)
                {
                    Log($"[MISS] No candidates for '{loc.Name}' in areas [{string.Join(",", areasInRegion)}]");
                    miss++;
                    continue;
                }

                AnnotationData.SlotAnnotation? match = null;

                if (loc.IsNpc)
                {
                    match = TryMatchNpcGiven(loc, baseCandidates);
                }
                else if (loc.IsBoss || loc.IsMiniboss || loc.IsDrop)
                {
                    match = TryMatchDrop(loc, baseCandidates);
                }
                else
                {
                    match = TryMatchTreasure(loc, baseCandidates);
                }

                if (match != null)
                {
                    if (!result.ContainsKey(loc.Name))
                    {
                        result[loc.Name] = match.Key;
                        heuristic++;
                    }
                    else if (result[loc.Name] != match.Key)
                    {
                        Log($"[WARN] Duplicate mapping for '{loc.Name}': '{result[loc.Name]}' vs '{match.Key}'");
                    }
                }
                else
                {
                    LogAmbiguous(loc, baseCandidates);
                    ambiguous++;
                }
            }

            Console.WriteLine($"[STATS] total={total}, direct={direct}, heuristic={heuristic}, ambiguous={ambiguous}, miss={miss}");
            return result;
        }


        // ===================== MATCHING =====================

        static string Normalize(string s)
        {
            return s
                .ToLowerInvariant()
                .Replace("-", " ")
                .Replace(",", "")
                .Replace("  ", " ")
                .Trim();
        }

        static List<string> Tokenize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return new List<string>();

            var sb = new System.Text.StringBuilder();
            var tokens = new List<string>();

            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                }
            }

            if (sb.Length > 0)
                tokens.Add(sb.ToString());

            return tokens;
        }

        static int TokenOverlapScore(List<string> a, List<string> b)
        {
            if (a.Count == 0 || b.Count == 0)
                return 0;

            var setB = new HashSet<string>(b);
            int score = 0;

            foreach (var t in a)
            {
                if (setB.Contains(t))
                    score++;
            }

            return score;
        }

        static AnnotationData.SlotAnnotation? MatchByPlace(
    List<AnnotationData.SlotAnnotation> candidates,
    string placeHint)
        {
            if (string.IsNullOrWhiteSpace(placeHint))
                return null;

            var hintTokens = Tokenize(placeHint);
            if (hintTokens.Count == 0)
                return null;

            AnnotationData.SlotAnnotation? best = null;
            int bestScore = 0;
            bool multipleBest = false;

            foreach (var s in candidates)
            {
                var texts = new List<string>();
                if (!string.IsNullOrEmpty(s.Text))
                    texts.Add(s.Text);
                if (s.DebugText != null)
                    texts.AddRange(s.DebugText);

                var combined = string.Join(" ", texts);
                var candTokens = Tokenize(combined);
                int score = TokenOverlapScore(hintTokens, candTokens);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                    multipleBest = false;
                }
                else if (score == bestScore && score > 0)
                {
                    multipleBest = true;
                }
            }

            if (bestScore > 0 && !multipleBest)
                return best;

            return null;
        }


        static AnnotationData.SlotAnnotation? TryMatchTreasure(
    CsvLocation loc,
    List<AnnotationData.SlotAnnotation> candidates)
        {
            // Treasure: обычные подбора с земли
            var (_, place) = ParseLocationName(loc.Name);
            var defaultItem = loc.DefaultItemName;

            // 1) Если есть default_item_name — сузим по нему через DebugText
            if (!string.IsNullOrWhiteSpace(defaultItem))
            {
                string normItem = defaultItem.Trim();

                var filtered = candidates
                    .Where(s =>
                        s.DebugText != null &&
                        s.DebugText.Any(dt =>
                            dt != null &&
                            dt.StartsWith(normItem + " -", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (filtered.Count == 1)
                    return filtered[0];

                if (filtered.Count > 1)
                {
                    // Несколько одинаковых item в одной области -> пробуем placeHint уже на них
                    var exactFromFiltered = MatchByPlace(filtered, place);
                    if (exactFromFiltered != null)
                        return exactFromFiltered;

                    // Если всё равно несколько — считаем неразрешимым
                    return null;
                }
                // если filtered.Count == 0 — продолжаем с исходными candidates
            }

            // 2) Без default_item_name — пробуем матч по placeHint
            return MatchByPlace(candidates, place);
        }

        static AnnotationData.SlotAnnotation? TryMatchNpcGiven(
    CsvLocation loc,
    List<AnnotationData.SlotAnnotation> candidates)
        {
            // Пример имени:
            // "AC1: Anti-Air Deathblow Text - Blackhat Badger"
            var (_, tail) = ParseLocationName(loc.Name);
            var npcHint = tail; // "Blackhat Badger" или "Emma" и т.п.
            var defaultItem = loc.DefaultItemName;

            var filtered = candidates;

            // 1) Если знаем предмет — сузим по default_item_name
            if (!string.IsNullOrWhiteSpace(defaultItem))
            {
                string normItem = defaultItem.Trim();

                filtered = candidates
                    .Where(s =>
                        s.DebugText != null &&
                        s.DebugText.Any(dt =>
                            dt != null &&
                            dt.StartsWith(normItem + " -", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (filtered.Count == 1)
                    return filtered[0];

                if (filtered.Count == 0)
                    filtered = candidates;
            }

            // 2) Пытаемся найти NPC по хвосту имени
            if (!string.IsNullOrWhiteSpace(npcHint))
            {
                string npcNorm = npcHint.ToLowerInvariant();

                var npcMatches = filtered
                    .Where(s =>
                        s.DebugText != null &&
                        s.DebugText.Any(dt =>
                            dt != null &&
                            dt.ToLowerInvariant().Contains(npcNorm)))
                    .ToList();

                if (npcMatches.Count == 1)
                    return npcMatches[0];
            }

            // 3) Фоллбек: NPC-локации довольно специфичны — если всё равно >1,
            // лучше вернуть null и пусть попадёт в лог ambiguous.
            return null;
        }

        static AnnotationData.SlotAnnotation? TryMatchDrop(
    CsvLocation loc,
    List<AnnotationData.SlotAnnotation> candidates)
        {
            // Пример:
            // "AR1: Gourd Seed - Shigenori Yamauchi"
            var (_, tail) = ParseLocationName(loc.Name);
            var bossHint = tail; // "Shigenori Yamauchi"
            var defaultItem = loc.DefaultItemName;

            // 1) Фильтруем только те слоты, где DebugText начинается с "Dropped by"
            var dropCandidates = candidates
                .Where(s =>
                    s.DebugText != null &&
                    s.DebugText.Any(dt =>
                        dt != null &&
                        dt.StartsWith("Dropped by", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (dropCandidates.Count == 0)
                return null;

            // 2) Сузим по имени босса из tail
            if (!string.IsNullOrWhiteSpace(bossHint))
            {
                string bossNorm = bossHint.ToLowerInvariant();

                var bossMatches = dropCandidates
                    .Where(s =>
                        s.DebugText != null &&
                        s.DebugText.Any(dt =>
                            dt != null &&
                            dt.ToLowerInvariant().Contains(bossNorm)))
                    .ToList();

                if (bossMatches.Count == 1)
                    return bossMatches[0];
                if (bossMatches.Count > 1)
                    return null; // неоднозначно
            }

            // 3) Если ничего не нашли по имени, но остался один dropCandidate
            if (dropCandidates.Count == 1)
                return dropCandidates[0];

            return null;
        }

        // ===================== PARSING =====================
        // "AR1: Ornamental Letter - in starting well"
        static (string item, string place) ParseLocationName(string name)
        {
            var rest = name;

            // отрезаем "AR1:" если есть
            int colon = rest.IndexOf(':');
            if (colon >= 0)
                rest = rest[(colon + 1)..].Trim();

            int dash = rest.IndexOf('-');

            var item = dash >= 0 ? rest[..dash].Trim() : rest;
            var place = dash >= 0 ? rest[(dash + 1)..].Trim() : "";

            return (item, place);
        }


        // ===================== CSV =====================

        static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result;
        }

        static bool ParseBool(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim().ToUpperInvariant();
            return s == "TRUE" || s == "1" || s == "YES";
        }

        static List<CsvLocation> LoadLocationsCsv(string path)
        {
            var list = new List<CsvLocation>();
            var lines = File.ReadAllLines(path);

            if (lines.Length < 2)
                return list;

            var header = ParseCsvLine(lines[0]);

            int regionIndex = header.IndexOf("region");
            int nameIndex = header.IndexOf("name");
            int defaultIndex = header.IndexOf("default_item_name");
            int npcIndex = header.IndexOf("npc");
            int bossIndex = header.IndexOf("boss");
            int minibossIndex = header.IndexOf("miniboss");
            int dropIndex = header.IndexOf("drop");
            int shopIndex = header.IndexOf("shop");

            if (regionIndex == -1 || nameIndex == -1)
                throw new Exception("CSV must contain 'region' and 'name' columns");

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = ParseCsvLine(line);

                if (parts.Count <= Math.Max(regionIndex, nameIndex))
                    continue;

                string region = parts[regionIndex].Trim();
                string name = parts[nameIndex].Trim();
                string defItem = defaultIndex >= 0 && defaultIndex < parts.Count
                    ? parts[defaultIndex].Trim()
                    : "";

                bool npc = npcIndex >= 0 && npcIndex < parts.Count && ParseBool(parts[npcIndex]);
                bool boss = bossIndex >= 0 && bossIndex < parts.Count && ParseBool(parts[bossIndex]);
                bool miniboss = minibossIndex >= 0 && minibossIndex < parts.Count && ParseBool(parts[minibossIndex]);
                bool drop = dropIndex >= 0 && dropIndex < parts.Count && ParseBool(parts[dropIndex]);
                bool shop = shopIndex >= 0 && shopIndex < parts.Count && ParseBool(parts[shopIndex]);

                list.Add(new CsvLocation
                {
                    Region = region,
                    Name = name,
                    DefaultItemName = defItem,
                    IsNpc = npc,
                    IsBoss = boss,
                    IsMiniboss = miniboss,
                    IsDrop = drop,
                    IsShop = shop
                });
            }

            return list;
        }


        static Dictionary<string, List<string>> LoadCleanupSheet(string path)
        {
            var map = new Dictionary<string, List<string>>();

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2)
                return map;

            var header = ParseCsvLine(lines[0]);

            int keyIndex = header.IndexOf("res ");
            int hintIndex = header.IndexOf("Hint text (adapt from column C)");

            if (keyIndex == -1 || hintIndex == -1)
                throw new Exception("cleanup_sheet.csv must contain 'res ' and 'Hint text (adapt from column C)' columns");

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = ParseCsvLine(line);
                if (parts.Count <= Math.Max(keyIndex, hintIndex))
                    continue;

                var key = parts[keyIndex].Trim();
                var hint = parts[hintIndex].Trim();

                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(hint))
                    continue;

                // иногда там есть строка типа "LINK TO STYLE GUIDE" и пустой key — мы её отсекаем
                if (hint.StartsWith("LINK TO STYLE GUIDE", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!map.TryGetValue(hint, out var list))
                {
                    list = new List<string>();
                    map[hint] = list;
                }

                if (!string.IsNullOrEmpty(key))
                    list.Add(key);
            }

            return map;
        }


        // ===================== LOGGING =====================

        static void Log(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        static void LogAmbiguous(CsvLocation loc, List<AnnotationData.SlotAnnotation> candidates)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[AMBIGUOUS] '{loc.Name}' (region {loc.Region}) candidates:");
            File.AppendAllText("ambiguous.txt", $"[AMBIGUOUS] '{loc.Name}' (region {loc.Region})\r\n");
            foreach (var c in candidates)
            {
                Console.WriteLine($"  {c.Key} | Area={c.Area} | Text='{c.Text}'");
            }
            Console.ResetColor();
        }

        // ===================== DATA =====================

        static Dictionary<string, List<string>> BuildRegionToAreas(AnnotationData ann)
        {
            var map = new Dictionary<string, List<string>>();

            foreach (var kv in ann.Areas)
            {
                string areaName = kv.Key;          // "ashinareservoir_start"
                var areaAnn = kv.Value;
                var apRegion = areaAnn.Archipelago; // "AR1", "DT", ...

                if (string.IsNullOrEmpty(apRegion))
                    continue;

                if (!map.TryGetValue(apRegion, out var list))
                {
                    list = new List<string>();
                    map[apRegion] = list;
                }

                list.Add(areaName);
            }

            return map;
        }



        class CsvLocation
        {
            public string Region;
            public string Name;
            public string DefaultItemName;

            public bool IsNpc;
            public bool IsBoss;
            public bool IsMiniboss;
            public bool IsDrop;
            public bool IsShop;
        }

        class CleanupEntry
        {
            public string Key;       // res: "01,0:51120000::"
            public string HintName;  // "AR1: Ornamental Letter - in starting well"
        }

    }
}
