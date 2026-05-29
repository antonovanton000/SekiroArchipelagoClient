using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RandomizerCommon;
using SekiroAPClient.Models;
using SoulsFormats;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using YamlDotNet.Serialization;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.LocationData.ItemScope;
using static SoulsIds.GameSpec;
using static System.Runtime.InteropServices.JavaScript.JSType;
using GameData = RandomizerCommon.GameData;


namespace SekiroAPClient.Classes;

public partial class RandomizerHelper : ObservableObject
{
    [ObservableProperty]
    bool isBusy = false;

    [ObservableProperty]
    string errorMessage = "";

    [ObservableProperty]
    bool hasErrors;

    [ObservableProperty]
    bool isFatalError;

    [ObservableProperty]
    string status = "";

    [ObservableProperty]
    bool isSuccess;
    public ApRandomizationState? State { get; private set; }

    private const int ItemNameLimit = 32;

    public Preset? SelectedPreset { get; set; } = null;

    public RandomizerOptions Options { get; private set; } = new();

    public string LogFilePath { get; private set; } = default!;

    public RandomizerHelper()
    {
        SetStatus(null);
        State = new();
    }


    private static SemanticVersioning.Version Version
    {
        get
        {
            return new SemanticVersioning.Version(App.AppVersion);
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

    public async Task<ApRandomizationState?> RandomizeArchipelago(ArchipelagoSession session)
    {
        if (IsBusy) return null;
        ErrorMessage = "";
        HasErrors = false;
        IsFatalError = false;
        IsSuccess = false;
        var outPath = Path.Combine(App.Location, "randomizer");

        var locations = session.Locations
            .ScoutLocationsAsync(session.Locations.AllLocations.ToArray())
            .Result
            .Values
            .OrderBy(location => location.LocationId)
            .ToList();
        var slotData = await session.DataStorage.GetSlotDataAsync();

        var apIdsToItemIds = ((JObject)slotData["apIdsToItemIds"]).ToObject<Dictionary<string, int>>()
            .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
        var apIdsToKeys = ((JObject)slotData["locationIdsToKeys"])
            .ToObject<Dictionary<string, string>>()
            .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);
        var itemCounts = ((JObject)slotData["itemCounts"]).ToObject<Dictionary<string, int>>()
            .ToDictionary(entry => long.Parse(entry.Key), entry => entry.Value);


        //CheckVersionRange(slotData);
        var options = ((JObject)slotData["options"]).ToObject<Dictionary<string, int>>();

        Options = ConvertRandomizerOptions(options);
        var seed = HashStringToInt(session.RoomState.Seed) + session.ConnectionInfo.Slot;
        Options.Seed = (uint)seed;

        SelectedPreset = ReadWorldEnemyPreset(slotData);
        if (SelectedPreset == null && !string.IsNullOrEmpty(Properties.Settings.Default.SelectedPreset))
        {
            try
            {
                var presetName = Properties.Settings.Default.SelectedPreset;
                var enemyName = Properties.Settings.Default.SelectedEnemy;
                SelectedPreset = Preset.LoadPreset(presetName, presetName.StartsWith("Oops All"));
                if (!string.IsNullOrEmpty(enemyName))
                {
                    SelectedPreset.OopsAll = enemyName.StartsWith("- ") ? enemyName.Substring(2) : enemyName;
                }
            }
            catch (Exception ex)
            {
                App.Logger.Warn($"Error loading preset: {ex}");
                SelectedPreset = null;
            }
        }

        if (Properties.Settings.Default.IsDebug)
        {
            File.WriteAllText(Path.Combine(App.Location, "debug_apIdsToItemIds.json"), JsonConvert.SerializeObject(apIdsToItemIds, Formatting.Indented));
            File.WriteAllText(Path.Combine(App.Location, "debug_apIdsToKeys.json"), JsonConvert.SerializeObject(apIdsToKeys, Formatting.Indented));
            File.WriteAllText(Path.Combine(App.Location, "debug_itemCounts.json"), JsonConvert.SerializeObject(itemCounts, Formatting.Indented));
            File.WriteAllText(Path.Combine(App.Location, "debug_locations.json"), JsonConvert.SerializeObject(locations, Formatting.Indented));
        }


        string distDir = ResolveDistDir();
        if (!Directory.Exists(distDir))
        {
            throw new Exception("Missing data directory");
        }

        State = await Task.Factory.StartNew(() =>
        {
            ApRandomizationState state = null;
            string spoilerLogDir = Path.Combine(App.Location, "spoiler_logs");
            Directory.CreateDirectory(spoilerLogDir);
            string runId = $"{DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss")}_log_{Options.Seed}_{Options.ConfigHash()}.txt";
            LogFilePath = Path.Combine(spoilerLogDir, runId);
            TextWriter log = File.CreateText(LogFilePath);
            TextWriter stdout = Console.Out;
            Console.SetOut(log);
            try
            {
                Console.WriteLine($"Options and seed: {Options}");
                Console.WriteLine();

                SetStatus("Loading game data");
                GameData game = new GameData(distDir, FromGame.SDT);
                game.Load(null);

                if (Options["enemy"])
                {
                    Console.WriteLine("Ctrl+F 'Boss placements' or 'Miniboss placements' or 'Basic placements' to see enemy placements.");
                }
                Console.WriteLine();
                for (int i = 0; i < 50; i++) Console.WriteLine();
                // Slightly different high-level algorithm for each game.

                Events events = new Events($@"{game.Dir}\Base\sekiro-common.emedf.json");
                EventConfig eventConfig;
                using (var reader = File.OpenText($@"{game.Dir}\Base\events.txt"))
                {
                    IDeserializer deserializer = new DeserializerBuilder().Build();
                    eventConfig = deserializer.Deserialize<EventConfig>(reader);
                }

                EnemyLocations? enemyLocations = null;
                if (Options["enemy"])
                {
                    SetStatus("Randomizing enemies");
                    Options["edittext"] = true;
                    enemyLocations = new EnemyRandomizer(game, events, eventConfig).Run(Options, SelectedPreset);
                    if (!Options["enemytoitem"])
                    {
                        enemyLocations = null;
                    }
                }

                SetStatus("Randomizing items");
                Options["item"] = false;
                SekiroLocationDataScraper scraper = new SekiroLocationDataScraper();
                LocationData data = scraper.FindItems(game);
                AnnotationData anns = new AnnotationData(game, data);
                anns.Load(Options);
                var mapping = ArchipelagoMappingSekiro.ArchipelagoSlotsSekiro(apIdsToKeys, anns, data, locations);
                var apLocationToSlotKey = mapping.LocationToSlot;
                var apLocationToLotIds = mapping.LocationToLotIds;

                var writer = new PermutationWriter(game, data, anns, events, eventConfig);
                var forcedLotQuantities = new Dictionary<int, int>();
                var forcedShopQuantities = new Dictionary<int, int>();
                var forcedLotEventFlags = new Dictionary<int, int>();
                var forcedShopEventFlags = new Dictionary<int, int>();
                var items = new Dictionary<SlotKey, List<SlotKey>>();
                var itemsToRemove = new Dictionary<SlotKey, List<SlotKey>>();
                var apLotMap = new List<ApLotEntry>();
                foreach (var info in locations)
                {
                    var quantity = itemCounts.TryGetValue(info.ItemId, out var q) ? q : 1;

                    if (!apLocationToSlotKey.TryGetValue(info.LocationId, out var targetSlotKey))
                    {
                        Console.WriteLine($"[AP] No SlotKey for AP location {info.LocationId} ({info.LocationName}), skipping");
                        continue;
                    }

                    var itemLoc = data.Location(targetSlotKey);
                    if (itemLoc == null)
                    {
                        Console.WriteLine(
                            $"[AP] No LocationData for slot {targetSlotKey} (AP location {info.LocationId}), skipping");
                        continue;
                    }

                    HashSet<int> apLotIds = null;
                    apLocationToLotIds?.TryGetValue(info.LocationId, out apLotIds);

                    var lotKeys = itemLoc.Keys
                        .Where(k => k.Type == LocationKey.LocationType.LOT)
                        .ToList();

                    var shopKeys = itemLoc.Keys
                        .Where(k => k.Type == LocationKey.LocationType.SHOP)
                        .ToList();

                    if (apLotIds != null && apLotIds.Count > 0)
                    {
                        var scopedKeys = data.Locations.TryGetValue(itemLoc.LocScope, out var scopeSlots)
                            ? scopeSlots
                            : new List<SlotKey>();

                        lotKeys = scopedKeys
                            .SelectMany(slot => data.Location(slot).Keys)
                            .Where(k => k.Type == LocationKey.LocationType.LOT && apLotIds.Contains(k.ID))
                            .GroupBy(k => (k.Type, k.ID, k.BaseID))
                            .Select(g => g.First())
                            .ToList();

                        shopKeys = scopedKeys
                            .SelectMany(slot => data.Location(slot).Keys)
                            .Where(k => k.Type == LocationKey.LocationType.SHOP && apLotIds.Contains(k.ID))
                            .GroupBy(k => (k.Type, k.ID, k.BaseID))
                            .Select(g => g.First())
                            .ToList();
                    }

                    // Some finite locations can also be obtained from a linked shop
                    // entry, most notably Prayer Beads transferred to the Offering
                    // Box. The AP location name may identify only the enemy lot, but
                    // the alternate shop route must contain the same AP item.
                    if (lotKeys.Count > 0)
                    {
                        shopKeys = shopKeys
                            .Concat(itemLoc.Keys.Where(k => k.Type == LocationKey.LocationType.SHOP))
                            .GroupBy(k => (k.Type, k.ID, k.BaseID))
                            .Select(g => g.First())
                            .ToList();
                    }

                    int sharedShopEventFlag = GetSharedShopEventFlag(game, shopKeys);

                    // If the location has neither LOT nor SHOP keys, skip it
                    if (lotKeys.Count == 0 && shopKeys.Count == 0)
                    {
                        Console.WriteLine(
                            $"[AP] Location {info.LocationId} ({info.LocationName}) has no LOT or SHOP keys, skipping");
                        continue;
                    }

                    bool isForeign = !info.IsReceiverRelatedToActivePlayer;
                    int expectedGoodId;
                    int expectedFullId;
                    LocationData.SlotKey sourceSlotKey;

                    // Get information about the player who owns this item (used for foreign item descriptions)
                    var player = session.Players.Players[session.ConnectionInfo.Team]
                        .First(p => p.Slot == info.Player);

                    // -------------------------------------------------------
                    // Create the source item exactly once per AP location
                    // -------------------------------------------------------
                    if (isForeign)
                    {
                        // Create a synthetic item for foreign items (items from other players)
                        var token = writer.AddSyntheticItem(
                            name: SyntheticItemName(info),
                            shortDescription: ItemDescriptionHelper.GetRandomItemDescription(player.Game),
                            longDescription: null,
                            iconId: 510,
                            sortId: 200_000 + (uint)info.Player.Slot * 10_000 + (uint)(info.ItemId % 10_000),
                            archipelagoLocationId: info.LocationId
                        );

                        var tokenItemKey = new ItemKey(token.Item.Type, token.Item.ID);
                        data.AddLocationlessItem(tokenItemKey);
                        quantity = 1;
                        sourceSlotKey = new SlotKey(tokenItemKey, new ItemScope(ScopeType.SPECIAL, -1));
                        expectedFullId = tokenItemKey.FullID;
                        expectedGoodId = tokenItemKey.ID;
                    }
                    else
                    {
                        // Use the original in-game item for local items
                        if (!apIdsToItemIds.TryGetValue(info.ItemId, out var originalfullId))
                        {
                            Console.WriteLine($"[AP] No apIdsToItemIds mapping for ItemId={info.ItemId} ({info.ItemName}), skipping");
                            continue;
                        }

                        expectedFullId = originalfullId;
                        expectedGoodId = originalfullId & 0x0FFFFFFF;
                        var originalKey = new ItemKey(originalfullId);
                        int sourceScopeId = checked((int)info.LocationId);
                        data.AddLocationlessItem(originalKey, sourceScopeId);

                        sourceSlotKey = new SlotKey(originalKey, new ItemScope(ScopeType.SPECIAL, sourceScopeId));
                    }

                    data.Location(sourceSlotKey).ForcedQuantity = quantity;

                    // -------------------------------------------------------
                    // LOT branch (regular pickups, boss drops, offering box)
                    // -------------------------------------------------------
                    foreach (var lk in lotKeys)
                    {
                        int lotId = lk.ID;
                        int vanillaLotEventFlag = GetLotEventFlag(game, lotId, lk.BaseID, -1);
                        int lotEventFlag = sharedShopEventFlag > 0
                            ? sharedShopEventFlag
                            : GetForcedLotEventFlag(lotId, vanillaLotEventFlag, info.LocationId, expectedGoodId);

                        var targetLotSlotKey = FindSlotKeyByLotId(data, itemLoc.LocScope, lotId);
                        if (targetLotSlotKey == null)
                        {
                            Console.WriteLine($"[AP] [LOT] Could not find SlotKey for lotId={lotId} in scope {itemLoc.LocScope} (AP {info.LocationId})");
                            continue;
                        }

                        // Mark the original vanilla lot for removal
                        Util.AddMulti(itemsToRemove, targetLotSlotKey, targetLotSlotKey);

                        // Assign our source item to this lot
                        Util.AddMulti(items, targetLotSlotKey, sourceSlotKey);

                        // Assign quantity
                        forcedLotQuantities[lotId] = quantity;
                        forcedLotEventFlags[lotId] = lotEventFlag;

                        // Register this lot for audit / runtime tracking
                        apLotMap.Add(new ApLotEntry
                        {
                            BaseLotId = lk.BaseID,
                            LotId = lotId,
                            EventFlagId = lotEventFlag,
                            LocationId = info.LocationId,
                            IsShop = false,
                            IsForeign = isForeign,
                            GoodId = expectedGoodId,
                            FullId = expectedFullId,
                            Quantity = quantity,
                            LocationName = info.LocationName,
                            DebugText = lk.Text
                        });
                    }

                    // -------------------------------------------------------
                    // SHOP branch (merchant entries, discounted variants, etc.)
                    // -------------------------------------------------------
                    foreach (var shopKey in shopKeys)
                    {
                        int shopId = shopKey.ID;
                        int shopEventFlag = sharedShopEventFlag > 0 ? sharedShopEventFlag : GetShopEventFlag(game, shopId);

                        var targetShopSlotKey = FindSlotKeyByShopId(data, itemLoc.LocScope, shopId);
                        if (targetShopSlotKey == null)
                        {
                            Console.WriteLine($"[AP] [SHOP] Could not find SlotKey for shopId={shopId} in scope {itemLoc.LocScope} (AP {info.LocationId})");
                            continue;
                        }

                        forcedShopQuantities[shopId] = quantity;
                        forcedShopEventFlags[shopId] = shopEventFlag;

                        // Register shop entry for audit / runtime tracking
                        apLotMap.Add(new ApLotEntry
                        {
                            LotId = shopId,
                            BaseLotId = shopKey.BaseID,
                            EventFlagId = shopEventFlag,
                            LocationId = info.LocationId,
                            IsShop = true,
                            IsForeign = isForeign,
                            GoodId = expectedGoodId,
                            FullId = expectedFullId,
                            Quantity = quantity,
                            LocationName = info.LocationName,
                            DebugText = shopKey.Text
                        });

                        // Remove vanilla shop entry
                        Util.AddMulti(itemsToRemove, targetShopSlotKey, targetShopSlotKey);

                        // Assign the same source item (important: no second synthetic creation)
                        Util.AddMulti(items, targetShopSlotKey, sourceSlotKey);
                    }
                }

                if (Properties.Settings.Default.IsDebug)
                {
                    var mfs = File.CreateText("missingApLocaitons.txt");
                    foreach (var item in locations)
                    {
                        var gameLoc = apLotMap.FirstOrDefault(i => i.LocationId == item.LocationId);
                        if (gameLoc == null)
                            mfs.WriteLine($"apLocId: {item.LocationId} - {item.LocationName}");
                    }
                    mfs.Close();
                }




                //------------------------------
                //Randomize NON - AP shop slots
                // ------------------------------

                // shop rows already reserved by Archipelago
                var apShopLotIds = apLotMap
                    .Where(e => e.IsShop)
                    .Select(e => e.LotId)
                    .ToHashSet();
                var apTargetSlots = items.Keys.ToHashSet();
                var apShopGoodsByMerchant = apLotMap
                    .Where(e => e.IsShop)
                    .GroupBy(e => GetShopMerchantId(checked((int)e.LotId)))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.GoodId).ToHashSet());
                var shopParam = game.Param("ShopLineupParam");

                var rnd = new Random(seed + 1);

                int randomizedShopRows = 0;
                int resolvedShopCollisions = 0;
                int skippedLinkedApTargets = 0;
                int skippedApRows = 0;
                int skippedNotWhitelisted = 0;

                // For each group (regular + discounted) we store the selected filler item
                var shopGroupItems = new Dictionary<int, ItemKey>();

                // Iterate through ALL shop rows in the game
                foreach (var locEntry in data.Data.SelectMany(kvp => kvp.Value.Locations.Values).ToList())
                {
                    foreach (var shopKey in locEntry.Keys.Where(k => k.Type == LocationKey.LocationType.SHOP))
                    {
                        int shopId = shopKey.ID;
                        int merchantId = GetShopMerchantId(shopId);
                        apShopGoodsByMerchant.TryGetValue(merchantId, out var reservedApGoods);
                        var sourceShopRow = shopParam[shopId];
                        bool collidesWithApItem = reservedApGoods != null
                            && sourceShopRow != null
                            && reservedApGoods.Contains((int)sourceShopRow["EquipId"].Value);

                        // Also replace a non-AP row if it would merge with an AP item in the merchant UI.
                        if (!RandomizedShopIds.Contains(shopId) && !collidesWithApItem)
                        {
                            skippedNotWhitelisted++;
                            continue;
                        }

                        // Do not touch slots occupied by Archipelago
                        if (apShopLotIds.Contains(shopId))
                        {
                            skippedApRows++;
                            continue;
                        }

                        forcedShopQuantities[shopId] = rnd.Next(1, 5);

                        // Find the SlotKey for THIS shopId
                        var scope = locEntry.LocScope;

                        if (!data.Locations.TryGetValue(scope, out var scopeSlots))
                            continue;

                        var targetSlotKey = scopeSlots
                            .FirstOrDefault(sk =>
                            {
                                var l = data.Location(sk);
                                return l.Keys.Any(k => k.Type == LocationKey.LocationType.SHOP && k.ID == shopId);
                            });

                        if (targetSlotKey == null)
                            continue;

                        // A shop row can be an alternate route for an AP lot in
                        // the same ItemLocation. Do not let filler assignment
                        // overwrite the already reserved AP lot.
                        if (apTargetSlots.Contains(targetSlotKey))
                        {
                            skippedLinkedApTargets++;
                            continue;
                        }

                        // Determine the group considering discount pairs
                        int groupId = GetShopGroupId(shopId);

                        // Get (or create) a filler item for this group
                        if (!shopGroupItems.TryGetValue(groupId, out var fillerItem))
                        {
                            ItemKey[] availableFillerItems = reservedApGoods == null
                                ? fillerItems
                                : fillerItems.Where(item => !reservedApGoods.Contains(item.ID)).ToArray();
                            if (availableFillerItems.Length == 0)
                                availableFillerItems = fillerItems;

                            fillerItem = availableFillerItems[rnd.Next(availableFillerItems.Length)];
                            data.AddLocationlessItem(fillerItem);
                            shopGroupItems[groupId] = fillerItem;
                        }

                        var sourceSlotKey = new SlotKey(
                            fillerItem,
                            new ItemScope(ScopeType.SPECIAL, -1)
                        );

                        Util.AddMulti(items, targetSlotKey, sourceSlotKey);

                        randomizedShopRows++;
                        if (collidesWithApItem)
                            resolvedShopCollisions++;
                    }
                }

                Console.WriteLine($"[SHOP RNG] Randomized: {randomizedShopRows}");
                Console.WriteLine($"[SHOP RNG] Resolved AP item collisions: {resolvedShopCollisions}");
                Console.WriteLine($"[SHOP RNG] Skipped linked AP targets: {skippedLinkedApTargets}");
                Console.WriteLine($"[SHOP RNG] Skipped AP: {skippedApRows}");
                Console.WriteLine($"[SHOP RNG] Skipped not whitelisted: {skippedNotWhitelisted}");

                //Randomize skills if option enabled. 
                SkillSplitter.Assignment split = null;
                if (!Options["norandom_skills"] && Options["splitskills"])
                {
                    split = new SkillSplitter(game, data, anns, events).SplitAll();
                }

                // Create permutation strictly for Archipelago
                var perm = new Permutation(game, data, anns, explain: true);

                // Fill it with our permutation
                perm.Forced(items, itemsToRemove);

                Options["edittext"] = false;
                anns.HintCategories.Clear();
                perm.Hints.Clear();

                writer.Write(new Random(seed + 1), perm, Options, forcedLotQuantities, forcedShopQuantities, forcedLotEventFlags, forcedShopEventFlags);

                if (!Options["norandom_skills"])
                {
                    SkillWriter skills = new SkillWriter(game, data, anns);
                    skills.RandomizeTrees(new Random(seed + 2), perm, split);
                }

                MiscSetup.SekiroCommonPass(game, events, Options);
                RestoreForcedShopQuantities(game, forcedShopQuantities);
                SetStatus("Writing game files");
                game.SaveSekiro(outPath);


                SetStatus($"Done! Hints and spoilers in spoiler_logs directory as {runId} - Restart your game!!", success: true);

                state = new ApRandomizationState
                {
                    ServerAddress = session.Socket.Uri.ToString(),
                    RoomName = "",
                    SlotName = session.ConnectionInfo.Slot.ToString(),
                    Game = session.ConnectionInfo.Game,
                    Seed = session.RoomState.Seed,
                    LocalSeed = seed,
                    ApLotMap = apLotMap
                };
                SaveRoomOptions(state);
                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                var path = Path.Combine(App.Location, "ap_randomization_state.json");
                File.WriteAllText(path, json, Encoding.UTF8);
                if (File.Exists(Path.Combine(App.Location, "randomizer\\localItemsStore.json")))
                {
                    File.Delete(Path.Combine(App.Location, "randomizer\\localItemsStore.json"));
                }
                if (Properties.Settings.Default.IsDebug)
                {

                    RandomizerAudit.Start(state);
                    var fs = File.CreateText("apLocationToLotId.txt");
                    foreach (var item in apLotMap)
                    {
                        var loc = locations.FirstOrDefault(i => i.LocationId == item.LocationId);
                        if (loc != null)
                        {
                            var quantityByItemId = 0;
                            var quantityByLocationId = 0;
                            itemCounts.TryGetValue(loc.ItemId, out quantityByItemId);
                            itemCounts.TryGetValue(loc.LocationId, out quantityByLocationId);
                            fs.WriteLine($"apLocId: {item.LocationId}\tGameLotId:{item.LotId} (Base: {item.BaseLotId})\tEventFlagId:{item.EventFlagId}\tGoodId: {item.GoodId}\tQuantity:{item.Quantity}\rapItemName:{loc.ItemDisplayName}\tItemId: {loc.ItemId}\tQuantityByItemId:{quantityByItemId}\tQuantityByLocationId:{quantityByLocationId}\rApLocationText: {item.LocationName}\rGameLocationText: {item.DebugText}\r------------------------------------------------------------\r");
                        }
                    }
                    fs.Close();

                }
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                SetError($"Error encountered: {ex.Message}\r\nIt may work to try again with a different seed. See most recent file in spoiler_logs directory for the full error.");
                SetStatus($"Error! Partial log in spoiler_logs directory as {runId}", true);
            }
            finally
            {
                State = null;
                log.Close();
                Console.SetOut(stdout);
            }
            return state;
        });
        return State;
    }

    SlotKey? FindSlotKeyByShopId(LocationData data, LocationScope scope, int shopId)
    {
        if (!data.Locations.TryGetValue(scope, out var scopeSlots) || scopeSlots == null)
            return null;

        foreach (var sk in scopeSlots)
        {
            var loc = data.Location(sk);
            if (loc == null) continue;

            if (loc.Keys.Any(k => k.Type == LocationKey.LocationType.SHOP && k.ID == shopId))
                return sk;
        }

        return null;
    }

    SlotKey? FindSlotKeyByLotId(LocationData data, LocationScope scope, int lotId)
    {
        if (!data.Locations.TryGetValue(scope, out var scopeSlots) || scopeSlots == null)
            return null;

        SlotKey? byBase = null;

        foreach (var sk in scopeSlots)
        {
            var loc = data.Location(sk);
            if (loc == null) continue;

            if (loc.Keys.Any(k => k.Type == LocationKey.LocationType.LOT && k.ID == lotId))
                return sk;

            if (byBase == null && loc.Keys.Any(k => k.Type == LocationKey.LocationType.LOT && k.BaseID == lotId))
                byBase = sk;
        }
        return byBase;
    }

    int GetLotEventFlag(GameData game, int lotId, int baseLotId, int fallbackEventFlag)
    {
        try
        {
            var itemLots = game.Param("ItemLotParam");
            var row = itemLots[lotId] ?? itemLots[baseLotId];
            if (row != null)
            {
                int flag = (int)row["getItemFlagId"].Value;
                if (flag > 0)
                    return flag;
            }
        }
        catch
        {
            // The tracker can still work without a flag, but reports should show it as missing.
        }

        return fallbackEventFlag > 0 ? fallbackEventFlag : -1;
    }

    int GetShopEventFlag(GameData game, int shopId)
    {
        try
        {
            var shops = game.Param("ShopLineupParam");
            var row = shops[shopId];
            if (row != null)
            {
                int flag = (int)row["EventFlag"].Value;
                if (flag > 0)
                    return flag;
            }
        }
        catch
        {
            // See GetLotEventFlag.
        }

        return -1;
    }

    int GetSharedShopEventFlag(GameData game, IReadOnlyCollection<LocationKey> shopKeys)
    {
        if (shopKeys.Count == 0)
            return -1;

        foreach (var shopKey in shopKeys.Where(k => !IsOfferingBoxShop(k.ID)))
        {
            int flag = GetShopEventFlag(game, shopKey.ID);
            if (flag > 0)
                return flag;
        }

        foreach (var shopKey in shopKeys)
        {
            int flag = GetShopEventFlag(game, shopKey.ID);
            if (flag > 0)
                return flag;
        }

        return -1;
    }

    static bool IsOfferingBoxShop(int shopId)
    {
        return shopId / 100 == 11005;
    }

    static int GetArchipelagoLotEventFlag(long archipelagoLocationId)
    {
        return checked(79_100_000 + (int)archipelagoLocationId);
    }

    static int GetForcedLotEventFlag(int lotId, int vanillaEventFlag, long archipelagoLocationId, int goodId)
    {
        if (RequiresPrivateArchipelagoFlag(lotId))
            return GetArchipelagoLotEventFlag(archipelagoLocationId);

        if (IsSekiroPermanentItemFlag(vanillaEventFlag))
            return GetArchipelagoLotEventFlag(archipelagoLocationId);

        return IsSekiroMapTreasureFlag(vanillaEventFlag)
            ? vanillaEventFlag
            : GetArchipelagoLotEventFlag(archipelagoLocationId);
    }

    static bool RequiresPrivateArchipelagoFlag(int lotId)
    {
        // The apworld now exposes the dungeon and Senpou Kotaro outcomes as
        // separate AP locations, but vanilla gives both rewards the same flag.
        return lotId is 52650 or 62600;
    }

    static bool IsSekiroPermanentItemFlag(int eventFlag)
    {
        return eventFlag >= 6500 && eventFlag < 6800;
    }

    static bool IsSekiroMapTreasureFlag(int eventFlag)
    {
        return eventFlag >= 50_000_000;
    }


    ItemKey[] fillerItems = new[]
{
    // -----------------------
    // Consumables
    // -----------------------
    new ItemKey(ItemType.GOOD, 3020), // Pellet
    new ItemKey(ItemType.GOOD, 3040), // Divine Grass
    new ItemKey(ItemType.GOOD, 3050), // Red Lump
    new ItemKey(ItemType.GOOD, 3070), // Persimmon
    new ItemKey(ItemType.GOOD, 3200), // Antidote Powder
    new ItemKey(ItemType.GOOD, 3210), // Dousing Powder
    new ItemKey(ItemType.GOOD, 3220), // Pacifying Agent
    new ItemKey(ItemType.GOOD, 3230), // Eel Liver
    new ItemKey(ItemType.GOOD, 3250), // Contact Medicine
    new ItemKey(ItemType.GOOD, 3290), // Bite Down
    new ItemKey(ItemType.GOOD, 3400), // Ako’s Sugar
    new ItemKey(ItemType.GOOD, 3410), // Gokan’s Sugar
    new ItemKey(ItemType.GOOD, 3420), // Ungo’s Sugar
    new ItemKey(ItemType.GOOD, 3430), // Yashariku’s Sugar
    new ItemKey(ItemType.GOOD, 3440), // Gachiin’s Sugar
    new ItemKey(ItemType.GOOD, 3450), // Divine Confetti
    new ItemKey(ItemType.GOOD, 3500), // Ceramic Shard
    new ItemKey(ItemType.GOOD, 3510), // Fistful of Ash
    new ItemKey(ItemType.GOOD, 3520), // Oil
    new ItemKey(ItemType.GOOD, 3530), // Snap Seed
    new ItemKey(ItemType.GOOD, 3600), // Mibu Balloon of Wealth
    new ItemKey(ItemType.GOOD, 3610), // Mibu Balloon of Spirit
    new ItemKey(ItemType.GOOD, 3620), // Mibu Balloon of Soul
    new ItemKey(ItemType.GOOD, 3630), // Mibu Balloon of Possession
    new ItemKey(ItemType.GOOD, 3720), // Bundled Jizo Statue
    new ItemKey(ItemType.GOOD, 5600), // Dragon's Blood Droplet
    // -----------------------
    // Materials
    // -----------------------
    new ItemKey(ItemType.GOOD, 6000), // Scrap Iron
    new ItemKey(ItemType.GOOD, 6010), // Scrap Magnetite
    new ItemKey(ItemType.GOOD, 6020), // Adamantite Scrap
    new ItemKey(ItemType.GOOD, 6100), // Black Gunpowder
    new ItemKey(ItemType.GOOD, 6200), // Yellow Gunpowder
    new ItemKey(ItemType.GOOD, 6210), // Fulminated Mercury
    new ItemKey(ItemType.GOOD, 6300), // Lump of Fat Wax
    new ItemKey(ItemType.GOOD, 6310), // Lump of Grave Wax
};


    private static readonly (int full, int discount)[] ShopDiscountPairs =
    {
    (1100001, 1100051),
    (1100003, 1100053),
    (1100002, 1100052),

    (1100102, 1100152),
    (1100103, 1100153),

    (1100207, 1100257),
    (1100208, 1100258),

    (1100203, 1100253),
    (1100201, 1100251),
    (1100202, 1100252),

    (1100401, 1100451),

    (1100403, 1100453),
    (1100405, 1100455),

    (1110002, 1110052),
    (1110003, 1110053),
    (1110006, 1110056),

    (1111404, 1111454),
    (1111403, 1111453),

    (1500001, 1500051),
    (1500003, 1500053),
    (1500002, 1500052),

    (1700001, 1700051),
    (1700003, 1700053),

    (2000004, 2000054),
    (2000002, 2000052),
    (2000003, 2000053),
    (2000001, 2000051),
};

    private static readonly Dictionary<int, int> ShopDiscountPartner =
        ShopDiscountPairs
            .SelectMany(p => new[]
            {
            new KeyValuePair<int,int>(p.full,     p.discount),
            new KeyValuePair<int,int>(p.discount, p.full),
            })
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static readonly HashSet<int> RandomizedShopIds =
        ShopDiscountPairs
            .SelectMany(p => new[] { p.full, p.discount })
            .ToHashSet();

    private static int GetShopGroupId(int shopId)
    {
        if (ShopDiscountPartner.TryGetValue(shopId, out var other))
            return Math.Min(shopId, other);

        // Dynamic collision replacements may affect rows outside the regular
        // filler whitelist; discounted shop rows use a +50 ID offset.
        if (shopId % 100 >= 50)
            return shopId - 50;

        return shopId;
    }

    private static int GetShopMerchantId(int shopId)
    {
        return shopId / 100;
    }

    private static void RestoreForcedShopQuantities(GameData game, IReadOnlyDictionary<int, int> forcedShopQuantities)
    {
        if (forcedShopQuantities.Count == 0)
            return;

        var shops = game.Param("ShopLineupParam");
        int fixedRows = 0;

        foreach (var (shopId, quantity) in forcedShopQuantities)
        {
            if (!shops.Rows.Any(r => (int)r.ID == shopId))
                continue;

            var row = shops[shopId];
            if (!row.Cells.Any(cell => cell.Def.InternalName == "sellQuantity"))
                continue;

            short safeQuantity = (short)Math.Max(1, quantity);
            if ((short)row["sellQuantity"].Value != safeQuantity)
                fixedRows++;

            row["sellQuantity"].Value = safeQuantity;
        }

        if (fixedRows > 0)
            Console.WriteLine($"[SHOP QTY] Restored sellQuantity for {fixedRows} shop rows before writing files");
    }

    private void SetError(string text, bool fatal = false)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            ErrorMessage = text ?? "";
            HasErrors = string.IsNullOrEmpty(ErrorMessage);
            IsFatalError = fatal;
        });
    }

    private void SetStatus(string msg, bool error = false, bool success = false)
    {
        if (msg == null)
        {
            DateTime now = DateTime.Now;
            msg = "";
        }
        App.Current.Dispatcher.Invoke(() =>
        {
            Status = msg;
        });
    }

    public string GetOptionsString()
    {
        return Options.FullString();
    }

    private static void CheckVersionRange(Dictionary<string, object> slotData)
    {
        if (!slotData.ContainsKey("versions"))
        {
            throw new Exception(
                "The server's version of the Sekiro apworld doesn't include any version " +
                "information, which means it's not compatible with this client." +
                (Version?.IsPreRelease ?? false
                    ? " Make sure you use the apworld that comes with this version to " +
                      "generate the multiworld."
                    : "")
            );
        }
        var range = new SemanticVersioning.Range((string)slotData["versions"]);

        // This should only be the case during development.
        if (Version == null) return;

        // Until we actually make server-side changes for v4, declare ourselves compatible with
        // the 3.x.x branch.

        if (range.IsSatisfied(Version, includePrerelease: true) ||
            range.IsSatisfied(App.CompatibleApWorldVersion, includePrerelease: true))
        {
            return;
        }


        throw new Exception(
            $"The server's version of the Sekiro apworld supports Sekiro AP versions {range}, " +
            $"but this randomizer is version {Version}."
        );
    }

    private RandomizerOptions ConvertRandomizerOptions(Dictionary<string, int> archiOptions)
    {
        var opt = new RandomizerOptions();
        if (archiOptions["randomize_enemies"] == 1)
        {
            opt["enemy"] = true;
            opt["bosses"] = true;
            opt["minibosses"] = true;
            opt["enemies"] = true;
            opt["headlessmove"] = archiOptions["randomize_headless"] == 1;
        }
        opt["phases"] = archiOptions["similar_boss_phases"] == 1;
        opt["phasebuff"] = archiOptions["balanced_endgame_boss_phases"] == 1;
        opt["earlyreq"] = archiOptions["simple_early_minibosses"] == 1;
        opt["scale"] = archiOptions["scale_enemies"] == 1;
        opt["carpsanity"] = archiOptions["carpsanity"] == 1;
        opt["death_link"] = archiOptions["death_link"] != 0;
        opt["death_link_full"] = archiOptions["death_link"] == 1;
        opt["norandom_skills"] = archiOptions["randomize_skills_and_prosthetics"] == 0;
        opt["headlesswalk"] = archiOptions["remove_headless_slow_walk"] == 1;
        opt["splitskills"] = false;//FOR NOW ITS OFF
        opt["openstart"] = false;
        return opt;
    }

    private static Preset? ReadWorldEnemyPreset(Dictionary<string, object> slotData)
    {
        if (!slotData.TryGetValue("random_enemy_preset", out var serializedPreset) ||
            serializedPreset is not string json ||
            string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var presetData = JObject.Parse(json);
        if (!presetData.HasValues)
            return null;

        var preset = presetData.ToObject<Preset>();
        if (preset == null)
            return null;

        preset.Name = "World Options";
        return preset;
    }

    private void SaveRoomOptions(ApRandomizationState state)
    {
        if (state == null) return;
        state.RoomRandomizerOptions.RandomizeEnemies = Options["enemy"];
        state.RoomRandomizerOptions.RandomizeBosses = Options["bosses"];
        state.RoomRandomizerOptions.RandomizeMiniBosses = Options["minibosses"];
        state.RoomRandomizerOptions.RandomizeRegularEnemies = Options["enemies"];
        state.RoomRandomizerOptions.RandomizeHeadless = Options["headlessmove"];
        state.RoomRandomizerOptions.SimilarBossPhases = Options["phases"];
        state.RoomRandomizerOptions.BalancedBossPhases = Options["phasebuff"];
        state.RoomRandomizerOptions.SimpleEarlyMinibosses = Options["earlyreq"];
        state.RoomRandomizerOptions.ScaleEnemies = Options["scale"];
        state.RoomRandomizerOptions.Carpsanity = Options["carpsanity"];
        state.RoomRandomizerOptions.DeathLink = Options["death_link"];
        state.RoomRandomizerOptions.DeathLinkOnFullDeath = Options["death_link_full"];
        state.RoomRandomizerOptions.RandomSkills = !Options["norandom_skills"];
        state.RoomRandomizerOptions.HeadlessSlowWalk = Options["headlesswalk"];
        state.RoomRandomizerOptions.PresetName = SelectedPreset?.DisplayName ?? "None";
    }

    /// <summary>
    /// Computes a stable hash of the given string and reduces it to a single integer.
    /// </summary>
    public static int HashStringToInt(string str)
    {
        using (var hash = SHA256.Create())
        {
            return BitConverter.ToInt32(hash.ComputeHash(Encoding.UTF8.GetBytes(str)), 0);
        }
    }

    /// <returns>A human-readable name for a foreign item.</returns>
    private static string SyntheticItemName(ScoutedItemInfo info)
    {
        // Use the player's entire name, if it fits.
        var name = $"{info.Player.Alias}'s {info.ItemName}";
        if (name.Length <= ItemNameLimit) return name;

        // If the player's name doesn't fit, trim it. Don't trim below four characters in case
        // it becomes unrecognizable. This may still result in a string longer than the maximum,
        // but in that case the item name will automatically get trimmed by the game as
        // necessary.
        var charactersToTrim = name.Length - ItemNameLimit;
        var trimmedPlayerName = info.Player.Alias[
            ..Math.Min(
                info.Player.Alias.Length,
                Math.Max(info.Player.Alias.Length - charactersToTrim, 4)
            )
        ];
        return $"{trimmedPlayerName} {info.ItemName}";
    }
}
