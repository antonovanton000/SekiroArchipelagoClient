using RandomizerCommon;
using SekiroAPClient.Models;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static SoulsIds.GameSpec;

namespace SekiroAPClient.Classes;

public class RandomizerAudit
{
    public static void Start(ApRandomizationState state)
    {
        using var fs = File.Open(Path.Combine(App.Location, "randoaudit.txt"), FileMode.Create, FileAccess.Write, FileShare.Read);
        using var sw = new StreamWriter(fs);

        try
        {
            var distDir = ResolveDistDir();
            GameData game = new GameData(distDir, FromGame.SDT);
            game.Load(Path.Combine(App.Location, "randomizer"));

            var itemLotParam = game.Params["ItemLotParam"];
            var shopParam = game.Params["ShopLineupParam"];

            sw.WriteLine("====== AUDIT ======");
            sw.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sw.WriteLine();

            AuditLots(sw, state, itemLotParam);
            sw.WriteLine();
            AuditShops(sw, state, shopParam);
            sw.WriteLine();
            AuditEventFlags(sw, state, itemLotParam, shopParam);
            sw.WriteLine();

            sw.WriteLine("====== END ======");
        }
        catch (Exception ex)
        {
            sw.WriteLine("[Audit] ERROR:");
            sw.WriteLine(ex.ToString());
        }
    }

    private static string ResolveDistDir()
    {
        string publishedDist = Path.Combine(App.Location, "dists");
        if (Directory.Exists(publishedDist))
            return publishedDist;

        string devDist = Path.GetFullPath(Path.Combine(App.Location, "..", "..", "..", "dists"));
        if (Directory.Exists(devDist))
            return devDist;

        return publishedDist;
    }

    // ------------------------------------------------------------
    // LOTS (ItemLotParam): audit with *grouping by AP location*
    // - prints full contents for each lot row
    // - checks expected GoodId presence in ItemLotIdN/NewLotItemIdN
    // - checks quantity at the matched slot index (if found)
    // - helps diagnose swaps: shows what GoodIds are actually present
    // ------------------------------------------------------------
    private static void AuditLots(StreamWriter sw, ApRandomizationState state, PARAM itemLotParam)
    {
        sw.WriteLine("------ LOTS (ItemLotParam) ------");

        var entries = state.ApLotMap
            .Where(e => !e.IsShop)
            .OrderBy(e => e.LocationId)
            .ThenBy(e => e.LotId)
            .ToList();

        WriteDuplicateTargets(sw, "[LOT][DUPLICATE-TARGET]", entries);

        var rowsById = itemLotParam.Rows.ToDictionary(r => (int)r.ID, r => r);

        var expectedByApLoc = entries
            .GroupBy(e => e.LocationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var apLocsByExpectedGood = entries
            .GroupBy(e => (int)e.GoodId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.LocationId).Distinct().ToList()
            );

        int missingRow = 0;
        int ok = 0;
        int goodMismatch = 0;
        int qtyMismatch = 0;
        int missingFlag = 0;
        int progressOffsetMismatch = 0;

        foreach (var group in expectedByApLoc)
        {
            long apLoc = group.Key;
            var groupEntries = group.Value;

            bool headerPrinted = false;

            foreach (var info in groupEntries)
            {
                if (!rowsById.TryGetValue((int)info.LotId, out var row))
                {
                    missingRow++;

                    if (!headerPrinted)
                    {
                        headerPrinted = true;
                        sw.WriteLine();
                        sw.WriteLine($"[AP-LOC] {apLoc} (entries={groupEntries.Count})");
                        foreach (var e in groupEntries)
                            sw.WriteLine($"  expected: lotId={e.LotId} good={e.GoodId} qty={e.Quantity} foreign={e.IsForeign}");
                    }

                    sw.WriteLine($"  [LOT][MISSING] lotId={info.LotId} expectedGood={info.GoodId} expectedQty={info.Quantity}");
                    continue;
                }

                var slotIds = ReadIntSlots(row, "ItemLotId");
                var newSlotIds = ReadIntSlots(row, "NewLotItemId");
                var slotNums = ReadIntSlots(row, "LotItemNum");
                var newSlotNums = ReadIntSlots(row, "NewLotItemNum");

                int getItemFlagId = ReadInt(row, "getItemFlagId", fallback: int.MinValue);
                int isProgressOffset = ReadInt(row, "LotItemNum1", fallback: 0);

                var match = FindMatchForGoodId((int)info.GoodId, (int)info.Quantity, slotIds, slotNums, newSlotIds, newSlotNums);

                if (isProgressOffset != 0)
                {
                    progressOffsetMismatch++;

                    if (!headerPrinted)
                    {
                        headerPrinted = true;
                        sw.WriteLine();
                        sw.WriteLine($"[AP-LOC] {apLoc} (entries={groupEntries.Count})");
                        foreach (var e in groupEntries)
                            sw.WriteLine($"  expected: lotId={e.LotId} good={e.GoodId} qty={e.Quantity} foreign={e.IsForeign}");
                    }

                    sw.WriteLine($"  [LOT][PROGRESS-OFFSET] lotId={info.LotId} expectedGood={info.GoodId} value={isProgressOffset}");
                }

                if (getItemFlagId <= 0 && RequiresPersistentLotFlag(info))
                {
                    missingFlag++;

                    if (!headerPrinted)
                    {
                        headerPrinted = true;
                        sw.WriteLine();
                        sw.WriteLine($"[AP-LOC] {apLoc} (entries={groupEntries.Count})");
                        foreach (var e in groupEntries)
                            sw.WriteLine($"  expected: lotId={e.LotId} good={e.GoodId} qty={e.Quantity} foreign={e.IsForeign}");
                    }

                    sw.WriteLine($"  [LOT][FLAG-MISSING] lotId={info.LotId} expectedGood={info.GoodId} expectedQty={info.Quantity} flag={getItemFlagId}");
                    sw.WriteLine($"    ItemLot: {FormatSlots(slotIds, slotNums)}");
                    sw.WriteLine($"    NewLot : {FormatSlots(newSlotIds, newSlotNums)}");
                }

                if (!match.Found)
                {
                    goodMismatch++;

                    if (!headerPrinted)
                    {
                        headerPrinted = true;
                        sw.WriteLine();
                        sw.WriteLine($"[AP-LOC] {apLoc} (entries={groupEntries.Count})");
                        foreach (var e in groupEntries)
                            sw.WriteLine($"  expected: lotId={e.LotId} good={e.GoodId} qty={e.Quantity} foreign={e.IsForeign}");
                    }

                    var actualGoods = slotIds.Values
                        .Concat(newSlotIds.Values)
                        .Where(v => v != 0)
                        .Distinct()
                        .ToList();

                    sw.WriteLine($"  [LOT][GOOD-MISMATCH] lotId={info.LotId} expectedGood={info.GoodId} expectedQty={info.Quantity} flag={getItemFlagId}");
                    sw.WriteLine($"    actualGoods: {string.Join(", ", actualGoods)}");

                    foreach (var gId in actualGoods)
                    {
                        if (apLocsByExpectedGood.TryGetValue(gId, out var owners))
                        {
                            var ownersStr = owners.Count <= 5
                                ? string.Join(", ", owners)
                                : string.Join(", ", owners.Take(5)) + $" ...(+{owners.Count - 5})";

                            sw.WriteLine($"    hint: goodId {gId} is expected by apLoc(s): {ownersStr}");
                        }
                    }

                    sw.WriteLine($"    ItemLot: {FormatSlots(slotIds, slotNums)}");
                    sw.WriteLine($"    NewLot : {FormatSlots(newSlotIds, newSlotNums)}");
                    continue;
                }

                // GOOD совпал — теперь проверяем количество
                if (match.Quantity.HasValue && match.Quantity != (int)info.Quantity)
                {
                    qtyMismatch++;

                    if (!headerPrinted)
                    {
                        headerPrinted = true;
                        sw.WriteLine();
                        sw.WriteLine($"[AP-LOC] {apLoc} (entries={groupEntries.Count})");
                        foreach (var e in groupEntries)
                            sw.WriteLine($"  expected: lotId={e.LotId} good={e.GoodId} qty={e.Quantity} foreign={e.IsForeign}");
                    }

                    sw.WriteLine($"  [LOT][QTY-MISMATCH] lotId={info.LotId} good={info.GoodId} expectedQty={info.Quantity} actualQty={match.Quantity.Value} flag={getItemFlagId}");
                    sw.WriteLine($"    ItemLot: {FormatSlots(slotIds, slotNums)}");
                    sw.WriteLine($"    NewLot : {FormatSlots(newSlotIds, newSlotNums)}");
                    continue;
                }

                ok++;
            }
        }

        sw.WriteLine();
        sw.WriteLine($"[LOT] TotalEntries={entries.Count} OK={ok} GoodMismatch={goodMismatch} QtyMismatch={qtyMismatch} MissingFlag={missingFlag} ProgressOffsetMismatch={progressOffsetMismatch} MissingRows={missingRow}");
        sw.WriteLine("------ END LOTS ------");
    }

    private static bool RequiresPersistentLotFlag(ApLotEntry info)
    {
        string debugText = info.DebugText ?? "";

        // Treasure Carp drops use non-respawning enemies rather than ItemLotParam pickup flags.
        // Their vanilla item lots intentionally have getItemFlagId = -1.
        if (debugText.Contains("Treasure Carp", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }


    // ------------------------------------------------------------
    // SHOPS (ShopLineupParam): check EquipId + sellQuantity/value/EventFlag
    // ------------------------------------------------------------
    private static void AuditShops(StreamWriter sw, ApRandomizationState state, PARAM shopParam)
    {
        sw.WriteLine("------ SHOPS (ShopLineupParam) ------");

        var entries = state.ApLotMap.Where(e => e.IsShop).ToList();
        WriteDuplicateTargets(sw, "[SHOP][DUPLICATE-TARGET]", entries);

        int missingRow = 0;
        int ok = 0;
        int mismatch = 0;

        foreach (var info in entries)
        {
            var row = shopParam.Rows.FirstOrDefault(r => (int)r.ID == info.LotId);
            if (row == null)
            {
                missingRow++;
                sw.WriteLine($"[SHOP][MISSING] apLoc={info.LocationId} shopId={info.LotId} expectedGood={info.GoodId} expectedQty={info.Quantity} foreign={info.IsForeign}");
                continue;
            }

            int equipId = ReadInt(row, "EquipId", fallback: int.MinValue);
            int sellQty = ReadInt(row, "sellQuantity", fallback: int.MinValue);
            int value = ReadInt(row, "value", fallback: int.MinValue);
            int evFlag = ReadInt(row, "EventFlag", fallback: int.MinValue);

            if (evFlag != info.EventFlagId)
            {
                mismatch++;
                sw.WriteLine($"[SHOP][FLAG-MISMATCH] apLoc={info.LocationId} shopId={info.LotId} good={info.GoodId} expectedFlag={info.EventFlagId} actualFlag={evFlag}");
                continue;
            }

            if (equipId == info.GoodId && sellQty == info.Quantity)
            {
                ok++;
                continue;
            }

            mismatch++;
            sw.WriteLine($"[SHOP][GOOD-MISMATCH] apLoc={info.LocationId} shopId={info.LotId} expectedGood={info.GoodId} expectedQty={info.Quantity} foreign={info.IsForeign}");
            sw.WriteLine($"  InGame: EquipId={equipId} sellQuantity={sellQty} value={value} EventFlag={evFlag}");
        }

        sw.WriteLine();
        sw.WriteLine($"[SHOP] Total={entries.Count} OK={ok} Mismatch={mismatch} MissingRows={missingRow}");
        sw.WriteLine("------ END SHOPS ------");
    }

    // ------------------------------------------------------------
    // EVENT FLAGS: AP placements must not share completion flags across
    // different AP locations. Multiple rows for the same AP location are ok
    // (normal/discount shop rows, alternate reward rows, etc.).
    // ------------------------------------------------------------
    private static void AuditEventFlags(StreamWriter sw, ApRandomizationState state, PARAM itemLotParam, PARAM shopParam)
    {
        sw.WriteLine("------ EVENT FLAGS ------");

        var rows = new List<FlagAuditEntry>();

        foreach (var info in state.ApLotMap)
        {
            int actualFlag = info.IsShop
                ? ReadEventFlagFromShop(shopParam, info)
                : ReadEventFlagFromLot(itemLotParam, info);

            int expectedPrivateFlag = ExpectedArchipelagoFlag(info);
            rows.Add(new FlagAuditEntry(
                info.LocationId,
                info.LocationName,
                info.LotId,
                info.IsShop,
                info.GoodId,
                info.EventFlagId,
                actualFlag,
                expectedPrivateFlag));
        }

        int missing = 0;
        int mismatch = 0;
        int nonPrivate = 0;
        int duplicate = 0;
        int semanticLeak = 0;

        foreach (var row in rows.OrderBy(r => r.LocationId).ThenBy(r => r.TargetId))
        {
            if (row.ActualFlag <= 0)
            {
                missing++;
                sw.WriteLine($"[EVENTFLAG][MISSING] apLoc={row.LocationId} target={row.TargetId} shop={row.IsShop} good={row.GoodId} expectedStateFlag={row.StateFlag}");
                continue;
            }

            if (row.ActualFlag != row.StateFlag)
            {
                mismatch++;
                sw.WriteLine($"[EVENTFLAG][STATE-MISMATCH] apLoc={row.LocationId} target={row.TargetId} shop={row.IsShop} good={row.GoodId} stateFlag={row.StateFlag} actualFlag={row.ActualFlag}");
            }

            if (row.StateFlag == row.ExpectedPrivateFlag && row.ActualFlag != row.ExpectedPrivateFlag)
            {
                nonPrivate++;
                sw.WriteLine($"[EVENTFLAG][NOT-AP-PRIVATE] apLoc={row.LocationId} target={row.TargetId} shop={row.IsShop} good={row.GoodId} expectedPrivate={row.ExpectedPrivateFlag} actualFlag={row.ActualFlag}");
            }

            if (!row.IsShop)
            {
                int expectedGoodForSemanticFlag = PermanentGoodToFlagCollection.GetExpectedItemForPermanentFlag(row.ActualFlag);
                if (expectedGoodForSemanticFlag > 0 && expectedGoodForSemanticFlag != row.GoodId)
                {
                    semanticLeak++;
                    sw.WriteLine($"[EVENTFLAG][SEMANTIC-FLAG-LEAK] apLoc={row.LocationId} target={row.TargetId} good={row.GoodId} actualFlag={row.ActualFlag} expectedGoodForFlag={expectedGoodForSemanticFlag}");
                }
            }
        }

        var duplicateGroups = rows
            .Where(r => r.ActualFlag > 0)
            .GroupBy(r => r.ActualFlag)
            .Where(g => g.Select(r => r.LocationId).Distinct().Count() > 1)
            .OrderBy(g => g.Key)
            .ToList();

        duplicate = duplicateGroups.Count;
        foreach (var group in duplicateGroups)
        {
            sw.WriteLine($"[EVENTFLAG][DUPLICATE-AP-LOCATIONS] flag={group.Key} apLocs={string.Join(", ", group.Select(r => r.LocationId).Distinct().OrderBy(id => id))}");
            foreach (var row in group.OrderBy(r => r.LocationId).ThenBy(r => r.TargetId))
            {
                sw.WriteLine($"  apLoc={row.LocationId} target={row.TargetId} shop={row.IsShop} good={row.GoodId} text={row.LocationName}");
            }
        }

        sw.WriteLine();
        sw.WriteLine($"[EVENTFLAG] Total={rows.Count} Missing={missing} StateMismatch={mismatch} NotPrivate={nonPrivate} DuplicateFlags={duplicate} SemanticLeaks={semanticLeak}");
        sw.WriteLine("------ END EVENT FLAGS ------");
    }

    private static int ReadEventFlagFromLot(PARAM itemLotParam, ApLotEntry info)
    {
        var row = itemLotParam.Rows.FirstOrDefault(r => (int)r.ID == info.LotId);
        return row == null ? int.MinValue : ReadInt(row, "getItemFlagId", fallback: int.MinValue);
    }

    private static int ReadEventFlagFromShop(PARAM shopParam, ApLotEntry info)
    {
        var row = shopParam.Rows.FirstOrDefault(r => (int)r.ID == info.LotId);
        return row == null ? int.MinValue : ReadInt(row, "EventFlag", fallback: int.MinValue);
    }

    private static int ExpectedArchipelagoFlag(ApLotEntry info)
    {
        return checked((info.IsShop ? 79_000_000 : 79_100_000) + (int)info.LocationId);
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private sealed record FlagAuditEntry(
        long LocationId,
        string LocationName,
        long TargetId,
        bool IsShop,
        long GoodId,
        int StateFlag,
        int ActualFlag,
        int ExpectedPrivateFlag);

    private static void WriteDuplicateTargets(StreamWriter sw, string label, List<ApLotEntry> entries)
    {
        var duplicateTargets = entries
            .GroupBy(e => e.LotId)
            .Where(g => g.Select(e => e.LocationId).Distinct().Count() > 1)
            .OrderBy(g => g.Key)
            .ToList();

        if (duplicateTargets.Count == 0)
            return;

        sw.WriteLine($"{label} count={duplicateTargets.Count}");
        foreach (var target in duplicateTargets)
        {
            sw.WriteLine($"  targetId={target.Key} apLocs={string.Join(", ", target.Select(e => e.LocationId).Distinct().OrderBy(id => id))}");
            foreach (var e in target.OrderBy(e => e.LocationId))
                sw.WriteLine($"    apLoc={e.LocationId} good={e.GoodId} qty={e.Quantity} base={e.BaseLotId} text={e.LocationName}");
        }
        sw.WriteLine();
    }

    private static int ReadInt(PARAM.Row row, string internalName, int fallback)
    {
        var cell = row.Cells.FirstOrDefault(c =>
            string.Equals(c.Def.InternalName, internalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Def.DisplayName, internalName, StringComparison.OrdinalIgnoreCase));

        if (cell?.Value == null) return fallback;

        try
        {
            return Convert.ToInt32(cell.Value);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Reads slots like ItemLotId1..N / LotItemNum1..N by prefix.
    /// Uses InternalName match first, then DisplayName.
    /// </summary>
    private static Dictionary<int, int> ReadIntSlots(PARAM.Row row, string prefix)
    {
        // key: index (1..N), value: int
        var dict = new Dictionary<int, int>();

        foreach (var cell in row.Cells)
        {
            string name = cell.Def.InternalName ?? cell.Def.DisplayName;
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = name.Substring(prefix.Length);
            if (!int.TryParse(suffix, out int idx))
                continue;

            if (cell.Value == null) continue;

            int v;
            try { v = Convert.ToInt32(cell.Value); }
            catch { continue; }

            dict[idx] = v;
        }

        return dict;
    }

    private readonly struct LotMatch
    {
        public bool Found { get; init; }
        public string Where { get; init; } // "ItemLot" or "NewLot"
        public int Index { get; init; }    // slot index N
        public int? Quantity { get; init; }
    }

    private static LotMatch FindMatchForGoodId(
        int goodId,
        int expectedQuantity,
        Dictionary<int, int> itemLotIds,
        Dictionary<int, int> itemLotNums,
        Dictionary<int, int> newLotIds,
        Dictionary<int, int> newLotNums)
    {
        var matches = new List<LotMatch>();

        for (int idx = 1; idx <= 8; idx++)
        {
            if (newLotIds.TryGetValue(idx, out var newId) && newId == goodId)
            {
                int? qty = null;
                if (newLotNums.TryGetValue(idx, out var q))
                    qty = q;

                matches.Add(new LotMatch
                {
                    Found = true,
                    Where = "NewLot",
                    Index = idx,
                    Quantity = qty
                });
            }

            if (itemLotIds.TryGetValue(idx, out var baseId) && baseId == goodId)
            {
                int? qty = null;
                if (newLotNums.TryGetValue(idx, out var newQty))
                    qty = newQty;
                else if (itemLotNums.TryGetValue(idx, out var baseQty))
                    qty = baseQty;

                matches.Add(new LotMatch
                {
                    Found = true,
                    Where = "ItemLot",
                    Index = idx,
                    Quantity = qty
                });
            }
        }

        var exactQuantityMatch = matches.FirstOrDefault(m => m.Quantity == expectedQuantity);
        if (exactQuantityMatch.Found)
            return exactQuantityMatch;

        var anyMatch = matches.FirstOrDefault();
        if (anyMatch.Found)
            return anyMatch;

        return new LotMatch
        {
            Found = false,
            Where = "",
            Index = 0,
            Quantity = null
        };
    }

    private static string FormatSlots(Dictionary<int, int> ids, Dictionary<int, int> nums)
    {
        // Format: "1:6000401 x1; 2:0 x0; 3:2420 x2"
        var parts = ids
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                int idx = kv.Key;
                int id = kv.Value;
                nums.TryGetValue(idx, out int num);
                return $"{idx}:{id} x{num}";
            });

        return string.Join("; ", parts);
    }
}
