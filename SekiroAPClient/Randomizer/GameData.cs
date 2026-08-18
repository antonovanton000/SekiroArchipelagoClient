using SoulsFormats;
using SoulsIds;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Animation;
using static RandomizerCommon.LocationData;
using static RandomizerCommon.Util;
using static SoulsIds.GameSpec;

namespace RandomizerCommon
{
    public class GameData
    {
        private static readonly List<string> itemParams = new List<string>()
        {
            "EquipParamWeapon", "EquipParamProtector", "EquipParamAccessory", "EquipParamGoods", "EquipParamGem",
        };

        public readonly GameEditor Editor;
        public FromGame Type => Editor.Spec.Game;
        public bool Sekiro => Type == FromGame.SDT;

        public readonly string Dir;
        private readonly bool useAdditionalRegionLocks;

        private string? ModDir;

        private static readonly Dictionary<string, string> SekiroLocationNames = new Dictionary<string, string>
        {
            { "m10_00_00_00", "hirata" },
            { "m11_00_00_00", "ashinaoutskirts" },
            { "m11_01_00_00", "ashinacastle" },
            { "m11_02_00_00", "ashinareservoir" },
            { "m13_00_00_00", "dungeon" },
            { "m15_00_00_00", "mibuvillage" },
            { "m17_00_00_00", "sunkenvalley" },
            { "m20_00_00_00", "senpou" },
            { "m25_00_00_00", "fountainhead" },
        };
        private static Dictionary<string, string> SekiroMapNames = new Dictionary<string, string>
        {
            { "", "Global" },
            { "hirata", "Hirata Estate" },
            { "ashinaoutskirts", "Ashina Outskirts" },
            { "ashinacastle", "Ashina Castle" },
            { "ashinareservoir", "Ashina Reservoir" },
            { "dungeon", "Abandoned Dungeon" },
            { "mibuvillage", "Ashina Depths" },
            { "sunkenvalley", "Sunken Valley" },
            { "senpou", "Senpou Temple" },
            { "fountainhead", "Fountainhead Palace" },
        };
        private readonly static Dictionary<uint, ItemType> MaskLotItemTypes = new Dictionary<uint, ItemType>
        {
            [0x00000000] = ItemType.WEAPON,
            [0x10000000] = ItemType.ARMOR,
            [0x20000000] = ItemType.RING,
            [0x40000000] = ItemType.GOOD,
        };
        private readonly static Dictionary<uint, ItemType> ErLotItemTypes = new Dictionary<uint, ItemType>
        {
            [1] = ItemType.GOOD,
            [2] = ItemType.WEAPON,
            [3] = ItemType.ARMOR,
            [4] = ItemType.RING,
            [5] = ItemType.GEM,
        };

        public readonly Dictionary<string, string> Locations;
        public readonly Dictionary<string, string> RevLocations;
        public readonly Dictionary<string, string> LocationNames;
        public readonly Dictionary<uint, ItemType> LotItemTypes;
        // Currently unused, as int/byte conversions with equipType are valid... currently.
        // TODO see if gem is sellable in Elden Ring.
        public readonly Dictionary<int, ItemType> ShopItemTypes = new Dictionary<int, ItemType>
        {
            [0] = ItemType.WEAPON,
            [1] = ItemType.ARMOR,
            [2] = ItemType.RING,
            [3] = ItemType.GOOD,
        };

        // Actual data
        private Dictionary<string, PARAM.Layout> Layouts = new Dictionary<string, PARAM.Layout>();
        private Dictionary<string, PARAMDEF> Defs = new Dictionary<string, PARAMDEF>();
        public Dictionary<string, PARAM> Params = new Dictionary<string, PARAM>();
        public Dictionary<string, IMsb> Maps = new Dictionary<string, IMsb>();
        public Dictionary<string, EMEVD> Emevds = new Dictionary<string, EMEVD>();
        public Dictionary<string, FMG> ItemFMGs = new Dictionary<string, FMG>();
        public Dictionary<string, Dictionary<string, FMG>> OtherItemFMGs = new Dictionary<string, Dictionary<string, FMG>>();
        public Dictionary<string, FMG> MenuFMGs = new Dictionary<string, FMG>();
        public Dictionary<string, Dictionary<string, FMG>> OtherMenuFMGs = new Dictionary<string, Dictionary<string, FMG>>();
        public Dictionary<string, Dictionary<string, ESD>> Talk = new Dictionary<string, Dictionary<string, ESD>>();

        // Names
        private SortedDictionary<ItemKey, string> itemNames = new SortedDictionary<ItemKey, string>();
        private SortedDictionary<string, List<ItemKey>> revItemNames = new SortedDictionary<string, List<ItemKey>>();
        private SortedDictionary<int, string> qwcNames = new SortedDictionary<int, string>();
        private SortedDictionary<int, string> lotNames = new SortedDictionary<int, string>();
        private SortedDictionary<int, string> characterSplits = new SortedDictionary<int, string>();
        private SortedDictionary<string, string> modelNames = new SortedDictionary<string, string>();

        private List<string> writtenFiles = new List<string>();

        public GameData(string dir, FromGame game, bool useAdditionalRegionLocks = false)
        {
            Dir = dir;
            this.useAdditionalRegionLocks = useAdditionalRegionLocks;
            Editor = new GameEditor(game);
            Editor.Spec.GameDir = $@"{dir}";
            Editor.Spec.NameDir = $@"{dir}\Names";
            Editor.Spec.LayoutDir = $@"{dir}\Layouts";
            LotItemTypes = MaskLotItemTypes;
            // Locations. TODO load better
            if (Sekiro)
            {
                Locations = SekiroLocationNames;
                LocationNames = SekiroMapNames;
            }
            RevLocations = Locations.ToDictionary(e => e.Value, e => e.Key);
        }

        // The IMsb interface is not usable directly, so in lieu of making GameData extremely generic, add these casts        
        public Dictionary<string, MSBS> SekiroMaps => Maps.ToDictionary(e => e.Key, e => e.Value as MSBS);

        public void Load(string? modDir = null)
        {
            this.ModDir = modDir;
            LoadNames();
            LoadLayouts();
            LoadParams();
            LoadMapData();
            LoadTalk();
            LoadScripts();
            LoadText();
        }

        public void UnDcx(string dir)
        {
            foreach (string path in Directory.GetFiles(dir, "*.dcx"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                byte[] f = DCX.Decompress(path);
                File.WriteAllBytes($@"{dir}\dcx\{name}", f);
            }
        }

        public void ReDcx(string dir, string ext)
        {
            foreach (string path in Directory.GetFiles($@"{dir}\dcx", "*." + ext))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                DCX.Compress(File.ReadAllBytes(path), (DCX.Type)DCX.DefaultType.Sekiro, $@"{dir}\{name}.{ext}.dcx");
            }
        }

        public PARAM Param(string name)
        {
            return Params[name];
        }

        public PARAM.Row Item(ItemKey key)
        {
            if (!Sekiro) key = NormalizeWeapon(key);
            return Params[itemParams[(int)key.Type]][key.ID];
        }

        public PARAM.Row AddRow(string name, int id)
        {
            PARAM param = Params[name];
            if (param[id] != null)
            {
                // This can get quadratic? But eh good to check
                throw new Exception($"Trying to add id {id} in {name} but already exists");
            }
            PARAM.Row row = new PARAM.Row(id, "", param.AppliedParamdef);
            param.Rows.Add(row);
            return row;
        }

        public StreamReader NewAnnotationReader()
        {
            string testFile = $@"{Dir}\Base\annotations.txt";
            if (File.Exists(testFile)) return File.OpenText(testFile);
            return File.OpenText($@"{Dir}\Base\annotations.yaml");
        }

        private static ItemKey NormalizeWeapon(ItemKey key)
        {
            // Maybe can put this logic in ItemKey itself
            if (key.Type == ItemType.WEAPON && key.ID % 100 != 0)
            {
                return new ItemKey(key.Type, key.ID - (key.ID % 100));
            }
            return key;
        }

        public string Name(ItemKey key)
        {
            string suffix = "";
            if (key.Type == ItemType.WEAPON && key.ID % 100 != 0)
            {
                suffix = $" +{key.ID % 100}";
                key = new ItemKey(key.Type, key.ID - (key.ID % 100));
            }
            // suffix += $" {key.ID}";
            return (itemNames.ContainsKey(key) ? itemNames[key] : $"?ITEM?" + $" ({(int)key.Type}:{key.ID})") + suffix;
        }

        private static readonly Dictionary<ItemKey, string> customNames = new Dictionary<ItemKey, string>
        {
            { new ItemKey(ItemType.GOOD, 2123), "Cinders of a Lord (Abyss Watchers)" },
            { new ItemKey(ItemType.GOOD, 2124), "Cinders of a Lord (Aldrich)" },
            { new ItemKey(ItemType.GOOD, 2125), "Cinders of a Lord (Yhorm)" },
            { new ItemKey(ItemType.GOOD, 2126), "Cinders of a Lord (Lothric)" },
        };

        public string DisplayName(ItemKey key)
        {
            return Name(key);
        }

        public ItemKey ItemForName(string name)
        {
            if (!revItemNames.ContainsKey(name)) throw new Exception($"Internal error: missing name {name}");
            if (revItemNames[name].Count != 1) throw new Exception($"Internal error: ambiguous name {name} could be {string.Join(" or ", revItemNames[name])}");
            return revItemNames[name][0];
        }

        public SortedDictionary<ItemKey, string> Names()
        {
            return itemNames;
        }

        public string LotName(int id)
        {
            return lotNames.ContainsKey(id) ? lotNames[id] : "?LOT?";
        }

        public string QwcName(int id)
        {
            return qwcNames.ContainsKey(id) ? qwcNames[id] : $"after {id}";
        }

        public string CharacterName(int id)
        {
            int chType = 0;
            foreach (KeyValuePair<int, string> entry in characterSplits)
            {
                if (entry.Key > id)
                {
                    break;
                }
                chType = entry.Key;
            }
            string name = characterSplits[chType];
            return name == "UNUSED" ? null : name;
        }

        public string ModelName(string chr)
        {
            return modelNames.TryGetValue(chr, out string m) ? m : chr;
        }

        public string ModelCharacterName(string chr, int id)
        {
            return id > 0 ? (CharacterName(id) ?? ModelName(chr)) : ModelName(chr);
        }

        public List<string> GetModelNames()
        {
            return modelNames.Values.ToList();
        }

        public string EntityName(EntityId entity, bool detail = false)
        {
            int split = entity.EntityName.IndexOf('_');
            if (split == -1)
            {
                return entity.EntityName;
            }
            // What is even happening here. This is horrific
            string model = entity.EntityName.Substring(0, split);
            string modelName = model;
            if (model == "c0000")
            {
                modelName = CharacterName(entity.CharaInitID) ?? "c0000";
            }
            if (modelNames.ContainsKey(modelName))
            {
                modelName = modelNames[modelName];
            }
            if (!detail)
            {
                // Note this doesn't do a CharacterName override, so using sparingly, or fix this
                return modelName;
            }
            List<string> details = new List<string>();
            if (modelName != model)
            {
                details.Add(modelName);
            }
            if (entity.EntityID > 0)
            {
                details.Add($"id {entity.EntityID}");
            }
            if (entity.GroupIds != null && entity.GroupIds.Count > 0)
            {
                details.Add($"group {string.Join(",", entity.GroupIds)}");
            }
            return (entity.Type == null ? "" : $"{entity.Type} ")
                + entity.EntityName
                + (details.Count > 0 ? $" ({string.Join(" - ", details)})" : "");
        }

        public void SaveSekiro(string outPath)
        {
            Console.WriteLine("Writing to " + outPath);
            writtenFiles.Clear();

            CopyLogosAndIcons(outPath, $@"{Dir}\Base\menu");
            CopySfx(outPath, $@"{Dir}\Base\sfx");
            ClearGeneratedDirectory(outPath, "msg");
            CopyScripts(outPath, $@"{Dir}\Base\script");
            CopyAdditionalRegionLockMapFiles(outPath);
            CopyAdditionalRegionLockObjFiles(outPath);
            foreach (KeyValuePair<string, IMsb> entry in Maps)
            {
                if (!Locations.ContainsKey(entry.Key)) continue;
                string path = $@"{outPath}\map\mapstudio\{entry.Key}.msb.dcx";
                AddModFile(path);
                entry.Value.Write(path, (DCX.Type)DCX.DefaultType.Sekiro);
            }

            foreach (KeyValuePair<string, Dictionary<string, ESD>> entry in Talk)
            {
                if (!Locations.ContainsKey(entry.Key) && entry.Key != "m00_00_00_00") continue;
                string baseTalkPath = Path.Combine(Dir, "Base", "basegame", "talk", $"{entry.Key}.talkesdbnd.dcx");
                if (!File.Exists(baseTalkPath))
                    baseTalkPath = Path.Combine(Dir, "Base", $"{entry.Key}.talkesdbnd.dcx");

                WriteModDependentBnd(outPath, baseTalkPath, $@"script\talk\{entry.Key}.talkesdbnd.dcx", entry.Value);
            }
            foreach (KeyValuePair<string, EMEVD> entry in Emevds)
            {
                string path = $@"{outPath}\event\{entry.Key}.emevd.dcx";
                AddModFile(path);
                entry.Value.Write(path, (DCX.Type)DCX.DefaultType.Sekiro);
#if DEBUG
                string scriptFile = path + ".js";
                if (File.Exists(scriptFile))
                {
                    Console.WriteLine($"Deleting {scriptFile}");
                    File.Delete(scriptFile);
                }
#endif
            }

            WriteModDependentBnd(outPath, $@"{Dir}\Base\gameparam.parambnd.dcx", $@"param\gameparam\gameparam.parambnd.dcx", Params);
            WriteModDependentBnd(outPath, $@"{Dir}\Base\item.msgbnd.dcx", $@"msg\engus\item.msgbnd.dcx", ItemFMGs);
            WriteModDependentBnd(outPath, $@"{Dir}\Base\menu.msgbnd.dcx", $@"msg\engus\menu.msgbnd.dcx", MenuFMGs);

            foreach (KeyValuePair<string, Dictionary<string, FMG>> entry in OtherItemFMGs)
            {
                WriteModDependentBnd(outPath, $@"{Dir}\Base\msg\{entry.Key}\item.msgbnd.dcx", $@"msg\{entry.Key}\item.msgbnd.dcx", entry.Value);
            }
            foreach (KeyValuePair<string, Dictionary<string, FMG>> entry in OtherMenuFMGs)
            {
                WriteModDependentBnd(outPath, $@"{Dir}\Base\msg\{entry.Key}\menu.msgbnd.dcx", $@"msg\{entry.Key}\menu.msgbnd.dcx", entry.Value);
            }
            MergeMods(outPath);
            Console.WriteLine("Success!");
        }

        private static string FullName(string path)
        {
            return new FileInfo(path).FullName;
        }

        private void AddModFile(string path)
        {
            path = FullName(path);
#if !DEBUG
            Console.WriteLine($"Writing {path}");
#endif
            writtenFiles.Add(path);
        }

        /// <returns>The name of the given item, without any upgrades if it's a weapon.</returns>
        public string BaseName(ItemKey key)
        {
            var id = key.ID;
            // Lower IDs include arrows, which use different IDs differently.
            if (key.Type == ItemType.WEAPON && id >= 800000) id = id / 10000 * 10000;
            key = new ItemKey(key.Type, id);
            return itemNames.ContainsKey(key) ? itemNames[key] : $"?ITEM? ({(int)key.Type}:{key.ID})";
        }


        void CopyScripts(string outPath, string basePath)
        {
            var destPath = Path.Combine(outPath, "script");
            if (Directory.Exists(basePath))
            {
                Directory.CreateDirectory(destPath);
                CopyFilesRecursively(new DirectoryInfo(basePath), new DirectoryInfo(destPath), overwrite: true);
            }
        }

        void CopySfx(string outPath, string basePath)
        {
            var destPath = Path.Combine(outPath, "sfx");
            CopyGeneratedDirectory(basePath, destPath);
        }

        void CopyLogosAndIcons(string outPath, string basePath)
        {
            var destPath = Path.Combine(outPath, "menu");
            CopyGeneratedDirectory(basePath, destPath);
        }

        void CopyGeneratedDirectory(string sourcePath, string destPath)
        {
            if (!Directory.Exists(sourcePath)) return;

            if (Directory.Exists(destPath))
            {
                Directory.Delete(destPath, recursive: true);
            }

            Directory.CreateDirectory(destPath);
            CopyFilesRecursively(new DirectoryInfo(sourcePath), new DirectoryInfo(destPath), overwrite: true);
        }

        void ClearGeneratedDirectory(string outPath, string relativePath)
        {
            string destPath = Path.Combine(outPath, relativePath);
            if (Directory.Exists(destPath))
            {
                Directory.Delete(destPath, recursive: true);
            }
        }

        void CopyAdditionalRegionLockMapFiles(string outPath)
        {
            if (!Sekiro || !useAdditionalRegionLocks) return;

            string basePath = Path.Combine(Dir, "Base", "blockers", "mapadd");
            if (!Directory.Exists(basePath)) return;

            string destPath = Path.Combine(outPath, "map");
            Directory.CreateDirectory(destPath);
            CopyFilesRecursively(new DirectoryInfo(basePath), new DirectoryInfo(destPath), overwrite: true);
        }

        void CopyAdditionalRegionLockObjFiles(string outPath)
        {
            if (!Sekiro || !useAdditionalRegionLocks) return;

            string basePath = Path.Combine(Dir, "Base", "blockers", "obj");
            if (!Directory.Exists(basePath)) return;

            string destPath = Path.Combine(outPath, "obj");
            Directory.CreateDirectory(destPath);
            CopyFilesRecursively(new DirectoryInfo(basePath), new DirectoryInfo(destPath), overwrite: true);
        }

        void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target, bool overwrite = false)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name), overwrite);
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name), overwrite);
        }

        void WriteModDependentBnd<T>(string outPath, string basePath, string relOutputPath, Dictionary<string, T> diffData)
            where T : SoulsFile<T>, new()
        {
            if (ModDir != null)
            {
                string modPath = $@"{ModDir}\{relOutputPath}";
                if (File.Exists(modPath)) basePath = modPath;
            }
            string path = $@"{outPath}\{relOutputPath}";
            AddModFile(path);
            Editor.OverrideBnd(basePath, Path.GetDirectoryName(path), diffData, f => f.Write());
        }

        private void MergeMods(string outPath)
        {
            Console.WriteLine("Processing extra mod files...");
            bool work = false;
            if (ModDir != null)
            {
                foreach (string gameFile in MiscSetup.GetGameFiles(ModDir, Sekiro))
                {
                    string source = FullName($@"{ModDir}\{gameFile}");
                    string target = FullName($@"{outPath}\{gameFile}");
                    if (writtenFiles.Contains(target)) continue;
                    Console.WriteLine($"Copying {source}");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, true);
                    writtenFiles.Add(target);
                    work = true;
                }
            }
            foreach (string gameFile in MiscSetup.GetGameFiles(outPath, Sekiro))
            {
                string target = FullName($@"{outPath}\{gameFile}");
                if (writtenFiles.Contains(target)) continue;
                Console.WriteLine($"Found extra file (delete it if you don't want it): {target}");
                work = true;
            }
            if (!work) Console.WriteLine("No extra files found");
        }

        private void LoadNames()
        {
            modelNames = new SortedDictionary<string, string>(Editor.LoadNames("ModelName", n => n, false));
            characterSplits = new SortedDictionary<int, string>(Editor.LoadNames("CharaInitParam", n => int.Parse(n), true));
            lotNames = new SortedDictionary<int, string>(Editor.LoadNames("ItemLotParam", n => int.Parse(n), true));
            qwcNames = new SortedDictionary<int, string>(Editor.LoadNames("ShopQwc", n => int.Parse(n), true));
            for (int i = 0; i < itemParams.Count; i++)
            {
                if (itemParams[i] == "EquipParamGem") continue;
                foreach (KeyValuePair<ItemKey, string> entry in Editor.LoadNames(itemParams[i], n => new ItemKey((ItemType)i, int.Parse(n)), true))
                {
                    itemNames[entry.Key] = entry.Value;
                    AddMulti(revItemNames, entry.Value, entry.Key);
                }
            }
            if (characterSplits.Count == 0)
            {
                characterSplits[0] = "UNUSED";
            }
        }

        // https://github.com/JKAnderson/Yapped/blob/master/Yapped/FormMain.cs
        private void LoadLayouts()
        {
            if (Editor.Spec.LayoutDir == null)
            {
                Defs = Editor.LoadDefs();
            }
            else
            {
                Layouts = Editor.LoadLayouts();
            }
        }

        private void LoadParams()
        {
            string path;

            path = $@"{Dir}\Base\gameparam.parambnd.dcx";
            string modPath = $@"{ModDir}\param\gameparam\gameparam.parambnd.dcx";
            if (File.Exists(modPath))
            {
                Console.WriteLine($"Using modded file {modPath}");
                path = modPath;
            }
            if (!File.Exists(path))
            {
                throw new Exception($"Missing param file: {path}");
            }
            if (Sekiro)
            {
                Params = Editor.LoadParams(path, Layouts, allowError: false);
            }
        }

        private void LoadMapData()
        {
            if (Sekiro)
            {
                Maps = LoadSekiroBaseAndAdditionalRegionLockFiles(path => (IMsb)MSBS.Read(path), "map", "*.msb.dcx");
                MaybeOverrideFromModDir(Maps, name => $@"map\MapStudio\{name}.msb.dcx", path => MSBS.Read(path));
                List<string> missing = Locations.Keys.Except(Maps.Keys).ToList();
                if (missing.Count != 0) throw new Exception($@"Missing msbs in dist\Base\basegame\map: {string.Join(", ", missing)}");
            }
        }

        private void LoadTalk()
        {
            Talk = LoadSekiroBaseAndAdditionalRegionLockBnds((data, path) => ESD.Read(data), "talk", "*.talkesdbnd.dcx");
            MaybeOverrideFromModDir(Talk, name => $@"script\talk\{name}.talkesdbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => ESD.Read(data)));
            List<string> missing = Locations.Keys.Concat(new[] { "m00_00_00_00" }).Except(Talk.Keys).ToList();
            if (missing.Count != 0) throw new Exception($@"Missing talkesdbnds in dist\Base\basegame\talk: {string.Join(", ", missing)}");
        }

        private void LoadScripts()
        {
            Emevds = Editor.Load(@"Base\basegame\event", path => EMEVD.Read(path), "*.emevd.dcx");
            MaybeOverrideFromModDir(Emevds, name => $@"event\{name}.emevd.dcx", path => EMEVD.Read(path));
            List<string> missing = Locations.Keys.Concat(new[] { "common", "common_func" }).Except(Emevds.Keys).ToList();
            if (missing.Count != 0) throw new Exception($@"Missing emevds in dist\Base\basegame\event: {string.Join(", ", missing)}");
            ApplyAdditionalRegionLockEmevdPatches();
        }

        private Dictionary<string, T> LoadSekiroBaseAndAdditionalRegionLockFiles<T>(Func<string, T> reader, string subdir, string ext)
        {
            Dictionary<string, T> ret = Editor.Load($@"Base\basegame\{subdir}", reader, ext);
            if (!useAdditionalRegionLocks) return ret;

            string blockerDir = Path.Combine(Dir, "Base", "blockers", subdir);
            if (!Directory.Exists(blockerDir)) return ret;

            foreach (KeyValuePair<string, T> entry in Editor.Load($@"Base\blockers\{subdir}", reader, ext))
            {
                ret[entry.Key] = entry.Value;
            }
            return ret;
        }

        private Dictionary<string, Dictionary<string, T>> LoadSekiroBaseAndAdditionalRegionLockBnds<T>(Func<byte[], string, T> reader, string subdir, string ext)
        {
            Dictionary<string, Dictionary<string, T>> ret = Editor.LoadBnds($@"Base\basegame\{subdir}", reader, ext);
            if (!useAdditionalRegionLocks) return ret;

            string blockerDir = Path.Combine(Dir, "Base", "blockers", subdir);
            if (!Directory.Exists(blockerDir)) return ret;

            foreach (KeyValuePair<string, Dictionary<string, T>> entry in Editor.LoadBnds($@"Base\blockers\{subdir}", reader, ext))
            {
                ret[entry.Key] = entry.Value;
            }
            return ret;
        }

        private void ApplyAdditionalRegionLockEmevdPatches()
        {
            if (!Sekiro || !useAdditionalRegionLocks) return;

            Events events = new Events($@"{Dir}\Base\sekiro-common.emedf.json");
            PatchAshinaCastleBlockers(events);
            PatchAshinaReservoirBlockers(events);
            PatchAbandonedDungeonBlockers(events);
            PatchSenpouBlockers(events);
        }

        private void PatchAshinaCastleBlockers(Events events)
        {
            EMEVD emevd = RequireEmevd("m11_01_00_00");
            EMEVD.Event init = RequireEvent(emevd, 50);
            AddInitializeEventIfMissing(init, 11115950);
            AddInitializeEventIfMissing(init, 11110999);
            AddInitializeEventIfMissing(init, 11110998);
            AddInitializeEventIfMissing(init, 11110997);
            AddInitializeEventIfMissing(init, 11110996);
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Common Event (20005610, 1100030, 1121399, 12000003, 12000004)"));
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Common Event (20005610, 1100030, 1121318, 12000005, 12000006)"));

            AddEventIfMissing(emevd, events, 11110999, EMEVD.Event.RestBehaviorType.Restart, BuildAshinaCastleSfxEvent());
            AddEventIfMissing(emevd, events, 11110998, EMEVD.Event.RestBehaviorType.Restart, BuildDoorLockEvent(
                objectId: 1121799,
                objActParamId: 999976,
                openedFlag: 61110989,
                requiredGoodId: 9407,
                actionButtonId: 9610,
                successDialogId: 10010155,
                failureDialogId: 10010187));
            AddEventIfMissing(emevd, events, 11110997, EMEVD.Event.RestBehaviorType.Default, new[]
            {
                "IF Character Has SpEffect (1, 10000, 3995, 1, 0, 1)",
                "IF In/Outside Area (1, 1, 10000, 1129398, 1)",
                "IF Condition Group (0, 1, 1)",
                "WAIT Fixed Time (Seconds) (1)",
                "Set Event Flag (1102999, 1)",
                "WAIT Fixed Time (Seconds) (1)",
                "Display Generic Dialog (12000008, 1, 1, 1129397, 3)",
                "END Unconditionally (0)",
            });
            AddEventIfMissing(emevd, events, 11110996, EMEVD.Event.RestBehaviorType.Restart, BuildBlazingBullCastleDoorEvent());
            PatchBlazingBullVictoryEvents(emevd, events);
        }

        private void PatchAshinaReservoirBlockers(Events events)
        {
            EMEVD emevd = RequireEmevd("m11_02_00_00");
            AddInitializeEventIfMissing(RequireEvent(emevd, 0), 11120999);
            EMEVD.Event init = RequireEvent(emevd, 0);
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Common Event (20005610, 1100030, 1102980, 12000005, 12000007)"));
            AddEventIfMissing(emevd, events, 11120999, EMEVD.Event.RestBehaviorType.Restart, BuildDoorLockEvent(
                objectId: 1121798,
                objActParamId: 999900,
                openedFlag: 61110696,
                requiredGoodId: 9407,
                actionButtonId: 9610,
                successDialogId: 10010155,
                failureDialogId: 10010187));
        }

        private void PatchAbandonedDungeonBlockers(Events events)
        {
            EMEVD emevd = RequireEmevd("m13_00_00_00");
            AddInitializeEventIfMissing(RequireEvent(emevd, 0), 11309999);
            AddEventIfMissing(emevd, events, 11309999, EMEVD.Event.RestBehaviorType.Restart, BuildAbandonedDungeonBlockerEvent());
        }

        private void PatchSenpouBlockers(Events events)
        {
            EMEVD emevd = RequireEmevd("m20_00_00_00");
            AddInitializeEventIfMissing(RequireEvent(emevd, 0), 12009999);
            AddEventIfMissing(emevd, events, 12009999, EMEVD.Event.RestBehaviorType.Restart, BuildDoorLockEvent(
                objectId: 2009900,
                objActParamId: 999921,
                openedFlag: 62001111,
                requiredGoodId: 9406,
                actionButtonId: 9600,
                successDialogId: 10010154,
                failureDialogId: 10010186));
        }

        private EMEVD RequireEmevd(string name)
        {
            if (!Emevds.TryGetValue(name, out EMEVD emevd))
            {
                throw new Exception($"Missing emevd required for additional_region_locks: {name}");
            }
            return emevd;
        }

        private static EMEVD.Event RequireEvent(EMEVD emevd, long eventId)
        {
            EMEVD.Event ev = emevd.Events.FirstOrDefault(e => e.ID == eventId);
            if (ev == null)
            {
                throw new Exception($"Missing event required for additional_region_locks patch: {eventId}");
            }
            return ev;
        }

        private static void AddEventIfMissing(EMEVD emevd, Events events, long eventId, EMEVD.Event.RestBehaviorType restBehavior, IEnumerable<string> commands)
        {
            if (emevd.Events.Any(e => e.ID == eventId)) return;

            EMEVD.Event ev = new EMEVD.Event(eventId, restBehavior);
            ev.Instructions.AddRange(commands.Select(events.ParseAdd));
            emevd.Events.Add(ev);
        }

        private static void AddInitializeEventIfMissing(EMEVD.Event initEvent, int eventId)
        {
            AddInstructionIfMissing(initEvent, new EMEVD.Instruction(2000, 0, new List<object> { 0, (uint)eventId, (uint)0 }));
        }

        private static void AddInstructionIfMissing(EMEVD.Event ev, EMEVD.Instruction instruction)
        {
            if (ev.Instructions.Any(i => InstructionMatches(i, instruction))) return;
            ev.Instructions.Add(instruction);
        }

        private static void ReplaceInstruction(EMEVD.Event ev, Events events, string oldCommand, string newCommand)
        {
            EMEVD.Instruction oldInstruction = events.ParseAdd(oldCommand);
            EMEVD.Instruction newInstruction = events.ParseAdd(newCommand);
            int index = ev.Instructions.FindIndex(i => InstructionMatches(i, oldInstruction));
            if (index == -1)
            {
                if (ev.Instructions.Any(i => InstructionMatches(i, newInstruction))) return;
                throw new Exception($"Missing instruction required for additional_region_locks patch in event {ev.ID}: {oldCommand}");
            }
            ev.Instructions[index] = newInstruction;
        }

        private static void AddInstructionAfterLastIfMissing(EMEVD.Event ev, Events events, string afterCommand, string addCommand)
        {
            EMEVD.Instruction afterInstruction = events.ParseAdd(afterCommand);
            EMEVD.Instruction addInstruction = events.ParseAdd(addCommand);
            int index = ev.Instructions.FindLastIndex(i => InstructionMatches(i, afterInstruction));
            if (index == -1)
            {
                throw new Exception($"Missing insertion point required for additional_region_locks patch in event {ev.ID}: {afterCommand}");
            }
            if (index + 1 < ev.Instructions.Count && InstructionMatches(ev.Instructions[index + 1], addInstruction)) return;
            ev.Instructions.Insert(index + 1, addInstruction);
        }

        private static void InsertInstructionsBeforeIfMissing(EMEVD.Event ev, Events events, string beforeCommand, IEnumerable<string> addCommands)
        {
            List<EMEVD.Instruction> instructions = addCommands.Select(events.ParseAdd).ToList();
            int existingCount = instructions.Count(addInstruction => ev.Instructions.Any(i => InstructionMatches(i, addInstruction)));
            if (existingCount == instructions.Count) return;
            if (existingCount != 0)
            {
                throw new Exception($"Found partial additional_region_locks insertion in event {ev.ID} before: {beforeCommand}");
            }

            EMEVD.Instruction beforeInstruction = events.ParseAdd(beforeCommand);
            int index = ev.Instructions.FindIndex(i => InstructionMatches(i, beforeInstruction));
            if (index == -1)
            {
                throw new Exception($"Missing insertion point required for additional_region_locks patch in event {ev.ID}: {beforeCommand}");
            }

            ev.Instructions.InsertRange(index, instructions);
        }

        private static void ReplaceInstructionArgValue(EMEVD.Event ev, Events events, int oldValue, int newValue, int expectedCount)
        {
            List<(Events.Instr Instr, int ArgIndex, bool IsUInt)> matches = new List<(Events.Instr, int, bool)>();
            foreach (EMEVD.Instruction instruction in ev.Instructions)
            {
                Events.Instr parsed = events.Parse(instruction);
                for (int i = 0; i < parsed.Args.Count; i++)
                {
                    object arg = parsed.Args[i];
                    if (arg is int intValue && intValue == oldValue)
                    {
                        matches.Add((parsed, i, false));
                    }
                    else if (arg is uint uintValue && uintValue == oldValue)
                    {
                        matches.Add((parsed, i, true));
                    }
                }
            }

            if (matches.Count == 0 && CountInstructionArgValue(ev, events, newValue) >= expectedCount) return;
            if (matches.Count != expectedCount)
            {
                throw new Exception($"Expected to replace {expectedCount} reference(s) to {oldValue} in event {ev.ID}, but found {matches.Count}");
            }

            foreach ((Events.Instr instr, int argIndex, bool isUInt) in matches)
            {
                instr[argIndex] = isUInt ? (object)(uint)newValue : newValue;
                instr.Save();
            }
        }

        private static int CountInstructionArgValue(EMEVD.Event ev, Events events, int value)
        {
            int count = 0;
            foreach (EMEVD.Instruction instruction in ev.Instructions)
            {
                Events.Instr parsed = events.Parse(instruction);
                foreach (object arg in parsed.Args)
                {
                    if (ArgMatchesValue(arg, value))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static bool ArgMatchesValue(object arg, int value)
        {
            return arg is int intValue && intValue == value
                || arg is uint uintValue && uintValue == value;
        }

        private static bool InstructionMatches(EMEVD.Instruction left, EMEVD.Instruction right)
        {
            return left.Bank == right.Bank
                && left.ID == right.ID
                && left.ArgData.SequenceEqual(right.ArgData);
        }

        private static IEnumerable<string> BuildAshinaCastleSfxEvent()
        {
            yield return "(De)activate Object (1129397, 1)";
            yield return "Delete Object-following SFX (1129397, 1)";
            yield return "WAIT Fixed Time (Frames) (1)";

            for (int dummypolyId = 101; dummypolyId <= 139; dummypolyId++)
            {
                if (dummypolyId == 120) continue;
                yield return $"Create Object-following SFX (1129397, {dummypolyId}, 11)";
            }

            yield return "IF Event Flag (0, 1, 0, 1102999)";
            yield return "(De)activate Object (1129397, 0)";
            yield return "Delete Object-following SFX (1129397, 1)";

            yield return "END Unconditionally (0)";
        }

        private static IEnumerable<string> BuildBlazingBullCastleDoorEvent()
        {
            return new[]
            {
                "(De)activate Object (1121398, 1)",
                "GOTO IF Event Flag (1, 1, 0, 61110990)",
                "IF Event Flag (0, 1, 0, 11110440)",
                "Set ObjAct State (1111400, 999920, 1)",
                "Set Object Interaction (1111400, 1, 0)",
                "IF Action Button (0, 7102, 1111400)",
                "IF Player Has/Doesn't Have Item (1, 3, 9410, 1)",
                "GOTO IF Condition Group State (Uncompiled) (0, 0, 1)",
                "Force Animation Playback (10000, 710900, 0, 0, 0, 0, 1)",
                "WAIT Fixed Time (Seconds) (3)",
                "Display Generic Dialog (10010157, 1, 1, 1111400, 3)",
                "Set Event Flag (61110990, 1)",
                "END Unconditionally (0)",
                "Label 0 ()",
                "Display Generic Dialog (10010189, 1, 1, 1111400, 3)",
                "WAIT Fixed Time (Seconds) (3)",
                "END Unconditionally (1)",
                "Label 1 ()",
                "Set ObjAct State (1111400, 999920, 0)",
                "END Unconditionally (0)",
            };
        }

        private static void PatchBlazingBullVictoryEvents(EMEVD emevd, Events events)
        {
            EMEVD.Event event11115200 = RequireEvent(emevd, 11115200);
            ReplaceInstruction(
                event11115200,
                events,
                "IF Event Flag (0, 1, 0, 11110440)",
                "IF Event Flag (0, 1, 0, 61110990)");
            InsertInstructionsBeforeIfMissing(
                event11115200,
                events,
                "Set Player Respawn Point (1112950)",
                BuildBlazingBullCastleHitSwapCommands());

            EMEVD.Event event11115203 = RequireEvent(emevd, 11115203);
            ReplaceInstructionArgValue(event11115203, events, 11110440, 61110990, 1);
            ReplaceInstructionArgValue(event11115203, events, 11110441, 11110440, 1);

            EMEVD.Event event11115205 = RequireEvent(emevd, 11115205);
            ReplaceInstruction(
                event11115205,
                events,
                "Force Animation Playback (1111442, 0, 0, 0, 0, 0, 1)",
                "Reproduce Object Animation (1111442, 10)");
            AddInstructionAfterLastIfMissing(
                event11115205,
                events,
                "Delete Object-following SFX (1111440, 1)",
                "Reproduce Object Animation (1111442, 10)");
        }

        private static IEnumerable<string> BuildBlazingBullCastleHitSwapCommands()
        {
            for (int hitId = 1114200; hitId <= 1114205; hitId++)
            {
                yield return $"Activate Hit ({hitId}, 0)";
            }

            for (int hitId = 1114210; hitId <= 1114215; hitId++)
            {
                yield return $"Activate Hit ({hitId}, 1)";
            }

            for (int hitId = 1114200; hitId <= 1114205; hitId++)
            {
                yield return $"Activate Hit and Create Navimesh ({hitId}, 0)";
            }

            for (int hitId = 1114210; hitId <= 1114215; hitId++)
            {
                yield return $"Activate Hit and Create Navimesh ({hitId}, 1)";
            }

            yield return "Set Event Flag (11110442, 1)";
        }

        private static IEnumerable<string> BuildDoorLockEvent(
            int objectId,
            int objActParamId,
            int openedFlag,
            int requiredGoodId,
            int actionButtonId,
            int successDialogId,
            int failureDialogId)
        {
            return new[]
            {
                $"GOTO IF Event Flag (1, 0, 0, {openedFlag})",
                $"Set Object Interaction ({objectId}, 0, 0)",
                $"Set Object Interaction ({objectId}, 10, 1)",
                $"Set ObjAct State ({objectId}, {objActParamId}, 0)",
                $"Reproduce Object Animation ({objectId}, 1)",
                $"IF Object Backread (0, {objectId}, 1, 0, 1)",
                "END Unconditionally (0)",
                "Label 1 ()",
                $"Set ObjAct State ({objectId}, {objActParamId}, 0)",
                $"IF Action Button (0, {actionButtonId}, {objectId})",
                $"IF Player Has/Doesn't Have Item (1, 3, {requiredGoodId}, 1)",
                "GOTO IF Condition Group State (Uncompiled) (0, 0, 1)",
                $"Force Use ObjAct (10000, {objectId}, {objActParamId}, -1)",
                "WAIT Fixed Time (Seconds) (0.5)",
                $"Display Generic Dialog ({successDialogId}, 1, 1, {objectId}, 3)",
                $"Set Event Flag ({openedFlag}, 1)",
                "END Unconditionally (0)",
                "Label 0 ()",
                $"Display Generic Dialog ({failureDialogId}, 1, 1, {objectId}, 3)",
                "WAIT Fixed Time (Seconds) (3)",
                "END Unconditionally (1)",
            };
        }

        private static IEnumerable<string> BuildAbandonedDungeonBlockerEvent()
        {
            return new[]
            {
                "GOTO IF Event Flag (1, 1, 0, 61301991)",
                "(De)activate Object (1300985, 1)",
                "(De)activate Object (1300986, 0)",
                "IF Action Button (0, 1000671, 1300985)",
                "IF Player Has/Doesn't Have Item (1, 3, 9408, 1)",
                "GOTO IF Condition Group State (Uncompiled) (0, 0, 1)",
                "Force Use ObjAct (10000, 1300985, 109951, 0)",
                "Display Generic Dialog (10010156, 1, 1, 1791550, 3)",
                "WAIT Fixed Time (Seconds) (1.5)",
                "Set Menu Fade (1, 0.5)",
                "WAIT Fixed Time (Seconds) (0.5)",
                "(De)activate Object (1300985, 0)",
                "(De)activate Object (1300986, 1)",
                "Set Event Flag (61301991, 1)",
                "WAIT Fixed Time (Seconds) (1.5)",
                "Set Menu Fade (0, 0.5)",
                "WAIT Fixed Time (Seconds) (0.5)",
                "WAIT Fixed Time (Seconds) (1)",
                "END Unconditionally (1)",
                "Label 0 ()",
                "Display Generic Dialog (10010188, 1, 1, 1791550, 3)",
                "WAIT Fixed Time (Seconds) (3)",
                "END Unconditionally (1)",
                "Label 1 ()",
                "(De)activate Object (1300985, 0)",
                "(De)activate Object (1300986, 1)",
                "END Unconditionally (0)",
            };
        }

        private void LoadText()
        {
            if (Sekiro)
            {
                ItemFMGs = Editor.LoadBnd($@"{Dir}\Base\item.msgbnd.dcx", (data, path) => FMG.Read(data));
                ItemFMGs = MaybeOverrideFromModDir(ItemFMGs, @"msg\engus\item.msgbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => FMG.Read(data)));
                MenuFMGs = Editor.LoadBnd($@"{Dir}\Base\menu.msgbnd.dcx", (data, path) => FMG.Read(data));
                MenuFMGs = MaybeOverrideFromModDir(MenuFMGs, @"msg\engus\menu.msgbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => FMG.Read(data)));
                foreach (string lang in MiscSetup.Langs)
                {
                    if (lang == "engus") continue;
                    OtherItemFMGs[lang] = Editor.LoadBnd($@"{Dir}\Base\msg\{lang}\item.msgbnd.dcx", (data, path) => FMG.Read(data));
                    OtherItemFMGs[lang] = MaybeOverrideFromModDir(OtherItemFMGs[lang], $@"msg\{lang}\item.msgbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => FMG.Read(data)));
                    OtherMenuFMGs[lang] = Editor.LoadBnd($@"{Dir}\Base\msg\{lang}\menu.msgbnd.dcx", (data, path) => FMG.Read(data));
                    OtherMenuFMGs[lang] = MaybeOverrideFromModDir(OtherMenuFMGs[lang], $@"msg\{lang}\menu.msgbnd.dcx", path => Editor.LoadBnd(path, (data, path2) => FMG.Read(data)));
                }
            }
        }

        // TODO: Instead of doing this, make the paths themselves more editable?
        private T MaybeOverrideFromModDir<T>(T original, string path, Func<string, T> parser)
        {
            if (ModDir == null) return original;
            string modPath = $@"{ModDir}\{path}";
            if (File.Exists(modPath))
            {
                Console.WriteLine($"Using modded file {modPath}");
                return parser(modPath);
            }
            return original;
        }

        private void MaybeOverrideFromModDir<T>(Dictionary<string, T> files, Func<string, string> relpath, Func<string, T> parser)
        {
            if (ModDir == null) return;
            foreach (string key in files.Keys.ToList())
            {
                files[key] = MaybeOverrideFromModDir(files[key], relpath(key), parser);
            }
        }

        // Some helper functionality things
        public void SearchParamInt(uint id, string field = null)
        {
            bool matches(string cell)
            {
                // if (cell == id.ToString()) return true;
                if (cell.Contains(id.ToString())) return true;
                // if (int.TryParse(cell, out int val)) return val >= 11000000 && val <= 13000000 && (val / 1000) % 10 == 5;
                return false;
            }
            Console.WriteLine($"-- Searching params for {id}");
            foreach (KeyValuePair<string, PARAM> param in Params)
            {
                foreach (PARAM.Row row in param.Value.Rows)
                {
                    if (field == null && row.ID == id)
                    {
                        Console.WriteLine($"{param.Key}[{row.ID}]");
                    }
                    foreach (PARAM.Cell cell in row.Cells)
                    {
                        if ((field == null || cell.Def.InternalName == field) && cell.Value != null && matches(cell.Value.ToString()))
                        {
                            Console.WriteLine($"{param.Key}[{row.ID}].{cell.Def.InternalName} = {cell.Value}");
                        }
                    }
                }
            }
        }

        public void SearchParamFloat(float id)
        {
            Console.WriteLine($"-- Searching params for {id}");
            foreach (KeyValuePair<string, PARAM> param in Params)
            {
                foreach (PARAM.Row row in param.Value.Rows)
                {
                    foreach (PARAM.Cell cell in row.Cells)
                    {
                        if (cell.Value != null && cell.Value.GetType() == 0f.GetType() && Math.Abs((float)cell.Value - id) < 0.0001)
                        {
                            Console.WriteLine($"{param.Key}[{row.ID}].{cell.Def.InternalName} = {cell.Value}");
                        }
                    }
                }
            }
        }

        public void DumpMessages(string dir)
        {
            foreach (string path in Directory.GetFiles(dir, "*.msgbnd*"))
            {
                if (path.Contains("dlc1")) continue;
                string name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
                try
                {
                    IBinder bnd = BND3.Is(path) ? (IBinder)BND3.Read(path) : BND4.Read(path);
                    foreach (BinderFile file in bnd.Files)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name));
                        string uname = fileName;
                        // uname = System.Text.RegularExpressions.Regex.Replace(uname, @"[^\x00-\x7F]", c => string.Format(@"u{0:x4}", (int)c.Value[0]));
                        string fname = $"{name}_{uname}.txt";
                        // Console.WriteLine(fname);
                        // string fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file.Name));
                        FMG fmg = FMG.Read(file.Bytes);
                        if (fmg.Entries != null)
                        {
                            File.WriteAllLines($@"{dir}\{fname}", fmg.Entries.Select(e => $"{e.ID} {(e.Text == null ? "" : e.Text.Replace("\r", "").Replace("\n", "\\n"))}"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load file: {name}: {path}\r\n\r\n{ex}");
                }
            }
            foreach (string path in Directory.GetFiles(dir, "*.fmg"))
            {
                FMG fmg = FMG.Read(path);
                string fname = Path.GetFileNameWithoutExtension(path);
                if (fmg.Entries != null)
                {
                    File.WriteAllLines($@"{dir}\{fname}.txt", fmg.Entries.Select(e => $"{e.ID} {(e.Text == null ? "" : e.Text.Replace("\r", "").Replace("\n", "\\n"))}"));
                }
            }
        }
    }
}
