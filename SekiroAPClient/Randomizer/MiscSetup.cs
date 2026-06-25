using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SoulsIds;
using SoulsFormats;
using static SoulsIds.Events;

namespace RandomizerCommon
{
    public class MiscSetup
    {
        // https://stackoverflow.com/questions/217902/reading-writing-an-ini-file
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        private static HashSet<string> badModEngines = new HashSet<string>
        {
            // Sekiro 1.04 and before
            "dfff963c88e82dc19c5e8592f464b9ca",
            "81137f302c6905f42ddf76fc52287e8e",
            "b25ddefec3f78278be633b85101ecccb",
            "3b37d8bbbce586a6f71dff687a25eb9c",
            "22adc919abcdf24df0f6d81e00a9974e",
            "71e9e56c741ca6ff338c16f8cb37c0e4",
            "9168d85ef78d13e108c30bdb74984c46",
            "267153f2297ba189591304560aab3a3e",
            "fbf98322736d493c5048804cc7efb11c",
            // Sekiro 1.04 custom build, previously worked
            "d79551b08bee23ab1d190448894b86d1",
            // DS3 official ones
            "3977dce4190107754b3b31deaf5b3b8f",
            "ef66f24d523069504ef3ec06ed0725fe",
            "af7c8c795ac852175e7850bc03f526ca",
        };

        private static HashSet<string> justWorksModEngines = new HashSet<string>
        {
            // Sekiro 1.06
            "f785817a60c9a40f7cd57ff74f4256d3",
            // DS3 custom build
            "3405ca8f6cd084f10e46a967f2463f19",
        };

        public static bool CheckRequiredSekiroFiles(out string ret)
        {
            ret = null;
            if (!Directory.Exists("dists"))
            {
                ret = "Error: Can't find required metadata files.\r\nFor the randomizer to work, you must unpack it to disk and keep all of the files together";
            }
            else if (File.Exists("Sekiro.exe"))
            {
                ret = "Error: Running from same directory as Sekiro.exe\r\nThe randomizer and its files must be in a subdirectory";
            }
            else if (!File.Exists("oo2core_6_win64.dll"))
            {
                if (File.Exists(@"..\oo2core_6_win64.dll"))
                {
                    File.Copy(@"..\oo2core_6_win64.dll", "oo2core_6_win64.dll");
                }
                else if (File.Exists(@"C:\Program Files (x86)\Steam\steamapps\common\Sekiro\oo2core_6_win64.dll"))
                {
                    File.Copy(@"C:\Program Files (x86)\Steam\steamapps\common\Sekiro\oo2core_6_win64.dll", "oo2core_6_win64.dll");
                }
                else
                {
                    ret = "Error: Oodle not found\r\nCopy oo2core_6_win64.dll from Sekiro.exe directory into the randomizer directory";
                }
            }
            return ret == null;
        }

        // Note: Doesn't return error or not (use ret != null for that), returns if fatal or not
        public static bool CheckSekiroModEngine(out string ret)
        {
            ret = null;
            if (!File.Exists(@"..\Sekiro.exe"))
            {
                ret = "Error: Sekiro.exe not found in parent directory\r\nFor randomization to work, move the randomizer folder to your Sekiro install location";
                return true;
            }
            if (!File.Exists(@"..\dinput8.dll") || !File.Exists(@"..\modengine.ini"))
            {
                ret = "Error: Sekiro Mod Engine not found in parent directory\r\nDownload dinput8.dll and modengine.ini from Sekiro Mod Engine";
                return true;
            }
            // Check Mod Engine version
            string modEngineHash = GetMD5Hash(@"..\dinput8.dll");
            if (badModEngines.Contains(modEngineHash))
            {
                // ret = "Error: Sekiro Mod Engine needs to be the unofficial version from the Sekiro Randomizer Files section\r\nCopy its dinput8.dll into parent dir or else enemy randomization will definitely crash the game!";
                ret = "Error: Sekiro Mod Engine needs to be the official 0.1.16 release for Sekiro 1.06.\r\nDownload it and copy it dinput8.dll into the parent dir.";
                return true;
            }
            // Check ini variables
            string ini = new FileInfo(@"..\modengine.ini").FullName.ToString();
            StringBuilder useMods = new StringBuilder(255);
            GetPrivateProfileString("files", "useModOverrideDirectory", "", useMods, 255, ini);
            if (useMods.ToString() != "1")
            {
                ret = "Warning: Set useModOverrideDirectory to 1 in modengine.ini\r\nOtherwise, randomization may not apply to game";
                return false;
            }
            StringBuilder modDir = new StringBuilder(255);
            GetPrivateProfileString("files", "modOverrideDirectory", "", modDir, 255, ini);
            string dirName = new DirectoryInfo(Directory.GetCurrentDirectory()).Name;
            string expected = $@"\{dirName}";
            if (modDir.ToString().ToLowerInvariant() != expected.ToLowerInvariant())
            {
                ret = $"Warning: Set modOverrideDirectory to \"{expected}\" in modengine.ini\r\nOtherwise, randomization may not apply to game";
                return false;
            }
            // Finally a check for future versions of mod engine. This will probably result in a bunch of user issue reports either way.
            if (!justWorksModEngines.Contains(modEngineHash))
            {
                ret = "Warning: Unknown version of Sekiro Mod Engine detected\r\nUse the latest official release, and update the randomizer if there is an update";
                return false;
            }
            return false;
        }



        private static readonly MD5 MD5 = MD5.Create();
        private static string GetMD5Hash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = MD5.ComputeHash(stream);
                return string.Join("", hash.Select(x => $"{x:x2}"));
            }
        }

        public static void CombineAI(List<string> maps, string outDir, bool mergeInfo)
        {
            // Merges AI into common and removes scripts from other maps.
            // Also looks at config directory for custom overrides.
            string commonPath = $@"{outDir}\..\script\aicommon.luabnd.dcx";
            BND4 aiCommon = BND4.Read(commonPath);
            HashSet<string> usedFiles = new HashSet<string>(aiCommon.Files.Select(f => f.Name));
            (LUAGNL, LUAINFO) parseMetadata(BND4 bnd)
            {
                if (!mergeInfo) return (null, null);
                BinderFile gnlFile = bnd.Files.Find(f => f.Name.EndsWith(".luagnl"));
                BinderFile infoFile = bnd.Files.Find(f => f.Name.EndsWith(".luainfo"));
                if (gnlFile == null) throw new Exception($"Missing required AI files [{gnlFile},{infoFile}]");
                return (LUAGNL.Read(gnlFile.Bytes), infoFile == null ? null : LUAINFO.Read(infoFile.Bytes));
            }
            void writeMetadata(BND4 bnd, LUAGNL gnl, LUAINFO info)
            {
                if (!mergeInfo) return;
                if (gnl != null) bnd.Files.Find(f => f.Name.EndsWith(".luagnl")).Bytes = gnl.Write();
                if (info != null) bnd.Files.Find(f => f.Name.EndsWith(".luainfo")).Bytes = info.Write();
            }
            void mergeMetadata(LUAGNL sourceGnl, LUAINFO sourceInfo, LUAGNL targetGnl, LUAINFO targetInfo)
            {
                if (!mergeInfo) return;
                if (sourceGnl != null)
                {
                    targetGnl.Globals = targetGnl.Globals.Union(sourceGnl.Globals).ToList();
                }
                if (sourceInfo != null)
                {
                    foreach (LUAINFO.Goal g in sourceInfo.Goals)
                    {
                        // Dedupe does not seem to be necessary, and tricky besides
                        // if (!sourceInfo.Goals.Any(h => h.ID == g.ID && h.Name == g.Name))
                        targetInfo.Goals.Add(g);
                    }
                }
            }
            (LUAGNL commonGnl, LUAINFO commonInfo) = parseMetadata(aiCommon);
            foreach (string map in maps)
            {
                string aiPath = $@"{outDir}\..\script\{map}.luabnd.dcx";
                if (!File.Exists(aiPath)) continue;

                BND4 ai = BND4.Read(aiPath);
                ai.Files = ai.Files.Where(file =>
                {
                    if (!file.Name.Contains("out")) return true;
                    if (!usedFiles.Contains(file.Name))
                    {
                        string overrideFile = $@"configs\dist\{Path.GetFileName(file.Name)}";
                        if (File.Exists(overrideFile))
                        {
                            Console.WriteLine("Override " + overrideFile);
                            file.Bytes = File.ReadAllBytes(overrideFile);
                        }
                        aiCommon.Files.Add(file);
                        usedFiles.Add(file.Name);
                    }
                    return false;
                }).ToList();
                (LUAGNL gnl, LUAINFO info) = parseMetadata(ai);
                mergeMetadata(gnl, info, commonGnl, commonInfo);
                ai.Write($@"{outDir}\script\{map}.luabnd.dcx");
            }
            writeMetadata(aiCommon, commonGnl, commonInfo);
            int startId = 2000;
            foreach (BinderFile file in aiCommon.Files)
            {
                if (file.ID < 3000) file.ID = startId++;
                Console.WriteLine(file);
            }
            aiCommon.Files.Sort((a, b) => a.ID.CompareTo(b.ID));
            aiCommon.Write($@"{outDir}\script\aicommon.luabnd.dcx");
        }

        public static void CombineDragonTpfs()
        {
            // Utility for creating Divine Dragon texbnd. Requires using Yabber to unpack these bnds, and repack after done.
            string gamePath = GameSpec.ForGame(GameSpec.FromGame.SDT).GameDir;
            string mainPath = $@"{gamePath}\chr\c5200-texbnd-dcx\chr\c5200\c5200.tpf";
            SFUtil.Backup(mainPath);
            TPF dragon = TPF.Read(mainPath);
            foreach (string p in Directory.GetFiles($@"{gamePath}\map\m25\m25_0000-tpfbhd", "m25_Dragon*.tpf.dcx"))
            {
                TPF t = TPF.Read(p);
                dragon.Textures.AddRange(t.Textures);
            }
            dragon.Write(mainPath);
        }

        public static bool CheckSFX()
        {
            string customPath = @"sfx\sfxbnd_commoneffects.ffxbnd.dcx";
            if (!File.Exists(customPath)) return false;
            // 1.04 original size: 64,029,504. New size: 78,592,543
            // 1.06 original size: 64,319,424. New size: 79,142,507
            if (new FileInfo(customPath).Length < 75000000) return false;
            return true;
        }

        public static bool CombineSFX(List<string> maps, string outDir, bool ds3 = false)
        {
            string inDir = new DirectoryInfo($@"{outDir}\..\sfx").FullName;
            string prefix = ds3 ? "frpg_" : "";
            // Note: DS3 files are 6 MB and 295 MB respectively, so, we need a more selective strategy for resources.
            string[] suffixes = ds3 ? new[] { "_effect", "_resource" } : new[] { "" };
            foreach (string suffix in suffixes)
            {
                string commonPath = $@"{inDir}\{prefix}sfxbnd_commoneffects{suffix}.ffxbnd.dcx";
                if (!File.Exists(commonPath)) return false;
                Console.WriteLine(new FileInfo(commonPath).FullName);
                BND4 sfxCommon = BND4.Read(commonPath);
                HashSet<string> sfxFiles = new HashSet<string>(sfxCommon.Files.Select(f => f.Name));
                Console.WriteLine(string.Join(",", maps));
                foreach (string map in maps.Select(m => m.Substring(0, 3)).Distinct())
                {
                    string path = $@"{inDir}\{prefix}sfxbnd_{map}{suffix}.ffxbnd.dcx";
                    if (!File.Exists(path)) continue;

                    BND4 sfx = BND4.Read(path);
                    sfx.Files = sfx.Files.Where(file =>
                    {
                        Console.WriteLine(file.Name);
                        if (!sfxFiles.Contains(file.Name))
                        {
                            sfxCommon.Files.Add(file);
                            sfxFiles.Add(file.Name);
                            return false;
                        }
                        else
                        {
                            return false;
                        }
                    }).ToList();
                    sfx.Write($@"{outDir}\sfx\{prefix}sfxbnd_{map}{suffix}.ffxbnd.dcx");
                }
                int startId = 0;
                foreach (BinderFile file in sfxCommon.Files)
                {
                    // Ignore prefixes here
                    file.ID = startId++;
                }
                sfxCommon.Files.Sort((a, b) => a.ID.CompareTo(b.ID));
                sfxCommon.Write($@"{outDir}\sfx\{prefix}sfxbnd_commoneffects{suffix}.ffxbnd.dcx");
            }
            return true;
        }



        public static void SekiroCommonPass(GameData game, Events events, RandomizerOptions opt)
        {
            Dictionary<string, PARAM> Params = game.Params;

            // Snap (for convenience, but can also softlock the player)
            if (opt["snap"]) Params["EquipParamGoods"][3980]["goodsUseAnim"].Value = (sbyte)84;
            // No tutorials
            if (Params.ContainsKey("MenuTutorialParam")) Params["MenuTutorialParam"].Rows = Params["MenuTutorialParam"].Rows.Where(r => r.ID == 0).ToList();
            // Memos pop up and don't just disappear mysteriously
            Params["EquipParamGoods"][9221]["Unk20"].Value = (byte)6;
            Params["EquipParamGoods"][9223]["Unk20"].Value = (byte)6;
            Params["EquipParamGoods"][9225]["Unk20"].Value = (byte)6;

            //// Boss lots that grant display-memory (5400)
            var bossMemoryLots = new HashSet<int>
            {
                2002, 2012, 2018, 2022, 2032,
                2042, 2052, 2062, 2072, 2082,
                2092, 2102, 2122, 2132
            };

            foreach (var lotId in bossMemoryLots)
            {
                if (!game.Params["ItemLotParam"].Rows.Any(r => (int)r.ID == lotId))
                    continue;

                var row = game.Params["ItemLotParam"][lotId];
                game.Params["ItemLotParam"].Rows.Remove(row);
            }

            // These are just always deleted
            HashSet<string> deleteCommands = new HashSet<string>
            {
                "Show Tutorial Text", "Show Hint Box", "Show Small Hint Box", "Award Achievement"
            };
            HashSet<int> deleteEvents = new HashSet<int>
            {
                // Putting away sword in areas
                20006200,
            };

            // Add fallback text to every language FMG used by in-game messaging.
            AddInGameMessagingText(game.MenuFMGs);
            foreach (var langFmgs in game.OtherMenuFMGs.Values)
            {
                AddInGameMessagingText(langFmgs);
            }
            AddEnglishFallbackText(game);

            if (!opt["earlyreq"])
            {
                AddEarlyReqNewShopRows(game);
                PatchEarlyReqCommonEvents(game, events);
            }

            // Slowless slow walk
            if (opt["headlesswalk"]) deleteEvents.Add(20005431);

            foreach (KeyValuePair<string, EMEVD> entry in game.Emevds)
            {
                foreach (EMEVD.Event e in entry.Value.Events)
                {
                    bool commonInit = entry.Key == "common" && e.ID == 0;
                    int maxPermSlot = 0;
                    for (int i = 0; i < e.Instructions.Count; i++)
                    {
                        Instr instr = events.Parse(e.Instructions[i]);
                        bool delete = false;
                        if (instr.Init)
                        {
                            if (deleteEvents.Contains(instr.Callee)) delete = true;
                            else if (commonInit && instr.Callee == 750 && instr.Offset == 2) maxPermSlot = Math.Max(maxPermSlot, (int)instr[0]);
                        }
                        else
                        {
                            if (deleteCommands.Contains(instr.Name)) delete = true;
                        }
                        if (delete)
                        {
                            EMEVD.Instruction newInstr = new EMEVD.Instruction(1014, 69);
                            e.Instructions[i] = newInstr;
                            // Just in case...
                            e.Parameters = e.Parameters.Where(p => p.InstructionIndex != i).ToList();
                        }
                    }
                    // Add permanent shop placement flags. Also.... abuse this for headless ape bestowal lot, if enemy rando is enabled.
                    if (opt["enemy"] && opt["bosses"] && commonInit)
                    {
                        entry.Value.Events[0].Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object> { maxPermSlot + 1, (uint)750, (uint)9307, (uint)9314 }));
                    }
                }
            }
                       
        }

        private static void AddEarlyReqNewShopRows(GameData game)
        {
            AddConfettiShopRow(game, 1100516, "Confetti_1", 71102360, 9136);
            AddConfettiShopRow(game, 1100517, "Confetti_2", 71102370, 9137);
        }

        private static void AddConfettiShopRow(GameData game, int rowId, string name, int eventFlag, int qwcId)
        {
            PARAM shops = game.Param("ShopLineupParam");
            PARAM.Row row = shops[rowId] ?? game.AddRow("ShopLineupParam", rowId);
            row.Name = name;
            SetParamCell(row, "EquipId", 3450);
            SetParamCell(row, "value", 50);
            SetParamCell(row, "mtrlId", -1);
            SetParamCell(row, "EventFlag", eventFlag);
            SetParamCell(row, "qwcID", qwcId);
            SetParamCell(row, "sellQuantity", 1);
            SetParamCell(row, "shopType", 0);
            SetParamCell(row, "equipType", 3);
            SetParamCell(row, "value_SAN", -1);
            SetParamCell(row, "PriceRate", 1f);
        }

        private static void SetParamCell(PARAM.Row row, string fieldName, object value)
        {
            PARAM.Cell cell = row[fieldName];
            object current = cell.Value;
            if (current is sbyte) cell.Value = Convert.ToSByte(value);
            else if (current is byte) cell.Value = Convert.ToByte(value);
            else if (current is short) cell.Value = Convert.ToInt16(value);
            else if (current is ushort) cell.Value = Convert.ToUInt16(value);
            else if (current is int) cell.Value = Convert.ToInt32(value);
            else if (current is uint) cell.Value = Convert.ToUInt32(value);
            else if (current is float) cell.Value = Convert.ToSingle(value);
            else cell.Value = value;
        }

        private static void PatchEarlyReqCommonEvents(GameData game, Events events)
        {
            if (!game.Emevds.TryGetValue("common", out EMEVD common))
            {
                throw new Exception("Missing common emevd required for earlyreq-disabled patch");
            }

            EMEVD.Event init = RequireEvent(common, 0, "earlyreq-disabled");
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Event (25, 720, 6790, 9136, 9300, -1)"));
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Event (26, 720, 6791, 9137, 9300, -1)"));
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Event (32, 750, 71102360, 6790)"));
            AddInstructionIfMissing(init, events.ParseAdd("Initialize Event (33, 750, 71102370, 6791)"));

            PatchOfferingBoxEvent714(common, events);
            PatchEvent9009(common, events);
        }

        private static void PatchOfferingBoxEvent714(EMEVD common, Events events)
        {
            EMEVD.Event ev = RequireEvent(common, 714, "earlyreq-disabled");
            List<(int EventFlag, int QwcFlag)> offeringBoxPairs = ExtractOfferingBoxPairs(events, ev);
            AddOfferingBoxPairIfMissing(offeringBoxPairs, 71102360, 9136);
            AddOfferingBoxPairIfMissing(offeringBoxPairs, 71102370, 9137);

            ev.Instructions.Clear();
            ev.Parameters.Clear();
            foreach (string command in BuildOfferingBoxEvent714(offeringBoxPairs))
            {
                ev.Instructions.Add(events.ParseAdd(command));
            }
        }

        private static List<(int EventFlag, int QwcFlag)> ExtractOfferingBoxPairs(Events events, EMEVD.Event ev)
        {
            List<(int EventFlag, int QwcFlag)> pairs = new List<(int, int)>();
            HashSet<int> knownQwcFlags = new HashSet<int>
            {
                9020, 9021, 9022, 9023, 9024, 9025, 9026, 9027, 9028, 9029, 9034, 9035, 9136, 9137,
            };

            for (int i = 0; i + 1 < ev.Instructions.Count; i++)
            {
                string first = events.Parse(ev.Instructions[i]).ToString();
                string second = events.Parse(ev.Instructions[i + 1]).ToString();
                if (!TryParseIfEventFlag(first, resultGroup: 2, desiredState: 0, out int eventFlag)
                    || !TryParseIfEventFlag(second, resultGroup: 2, desiredState: 1, out int qwcFlag)
                    || !knownQwcFlags.Contains(qwcFlag))
                {
                    continue;
                }

                AddOfferingBoxPairIfMissing(pairs, eventFlag, qwcFlag);
            }

            if (pairs.Count == 0)
            {
                throw new Exception("Could not extract existing Event 714 Offering Box flag pairs before earlyreq-disabled patch");
            }

            return pairs;
        }

        private static bool TryParseIfEventFlag(string command, int resultGroup, int desiredState, out int eventFlag)
        {
            eventFlag = 0;
            Match match = Regex.Match(command, @"^IF Event Flag \((-?\d+), (-?\d+), 0, (-?\d+)\)$");
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups[1].Value, out int parsedResultGroup)
                || !int.TryParse(match.Groups[2].Value, out int parsedDesiredState)
                || !int.TryParse(match.Groups[3].Value, out int parsedEventFlag)
                || parsedResultGroup != resultGroup
                || parsedDesiredState != desiredState)
            {
                return false;
            }

            eventFlag = parsedEventFlag;
            return true;
        }

        private static void AddOfferingBoxPairIfMissing(List<(int EventFlag, int QwcFlag)> pairs, int eventFlag, int qwcFlag)
        {
            if (pairs.Any(pair => pair.QwcFlag == qwcFlag))
                return;

            pairs.Add((eventFlag, qwcFlag));
        }

        private static IEnumerable<string> BuildOfferingBoxEvent714(IReadOnlyList<(int EventFlag, int QwcFlag)> offeringBoxPairs)
        {
            int[] allOfferingBoxFlags =
            {
                6719, 6766, 6767, 6500, 6508, 6723, 6741, 6760, 6762, 6769, 6506, 6780, 6790, 6791,
            };

            List<string> commands = new List<string>();
            commands.Add("Set Event Flag (9008, 0)");

            foreach (int flag in allOfferingBoxFlags)
            {
                commands.Add($"IF Event Flag (1, 1, 0, {flag})");
            }

            commands.Add("END IF Condition Group State (Uncompiled) (0, 1, 1)");

            foreach ((int eventFlag, int qwcFlag) in offeringBoxPairs)
            {
                commands.Add($"IF Event Flag (2, 0, 0, {eventFlag})");
                commands.Add($"IF Event Flag (2, 1, 0, {qwcFlag})");
                commands.Add("GOTO IF Condition Group State (Uncompiled) (0, 1, 2)");
                commands.Add("IF Condition Group (0, 0, 2)");
                commands.Add("Clear Compiled Condition Group State (0)");
            }

            commands.Add("GOTO Unconditionally (20)");
            commands.Add("Label 0 ()");
            commands.Add("Set Event Flag (9008, 1)");
            commands.Add("Label 20 ()");

            foreach ((int eventFlag, _) in offeringBoxPairs)
            {
                commands.Add($"IF Event Flag (-1, 2, 0, {eventFlag})");
            }

            foreach ((_, int qwcFlag) in offeringBoxPairs)
            {
                commands.Add($"IF Event Flag (-1, 2, 0, {qwcFlag})");
            }

            commands.Add("IF Condition Group (0, 1, -1)");
            commands.Add("END Unconditionally (1)");

            return commands;
        }

        private static void PatchEvent9009(EMEVD common, Events events)
        {
            EMEVD.Event ev = RequireEvent(common, 9009, "earlyreq-disabled");
            ev.Instructions.Clear();
            ev.Parameters.Clear();
            ev.Instructions.AddRange(new[]
            {
                "IF Batch Event Flags (-1, 2, 0, 9020, 9035)",
                "IF Batch Event Flags (-1, 2, 0, 9136, 9137)",
                "IF Condition Group (0, 1, -1)",
                "Set Event Flag (9009, 1)",
            }.Select(events.ParseAdd));
        }

        private static EMEVD.Event RequireEvent(EMEVD emevd, long eventId, string patchName)
        {
            EMEVD.Event ev = emevd.Events.FirstOrDefault(e => e.ID == eventId);
            if (ev == null)
            {
                throw new Exception($"Missing event required for {patchName} patch: {eventId}");
            }
            return ev;
        }

        private static void AddInstructionIfMissing(EMEVD.Event ev, EMEVD.Instruction instruction)
        {
            if (ev.Instructions.Any(i => InstructionMatches(i, instruction))) return;
            ev.Instructions.Add(instruction);
        }

        private static bool InstructionMatches(EMEVD.Instruction left, EMEVD.Instruction right)
        {
            return left.Bank == right.Bank
                && left.ID == right.ID
                && left.ArgData.SequenceEqual(right.ArgData);
        }

        private static void AddInGameMessagingText(Dictionary<string, FMG> menuFmgs)
        {
            if (!menuFmgs.TryGetValue("イベントテキスト", out var eventFmg))
                return;

            eventFmg[15100005] = "Notification";
            eventFmg[15100006] = "Death Link";
            eventFmg[15000450] = "Additional Options";
            eventFmg[15100450] = "Additional option for idols.";
        }

        private static void AddEnglishFallbackText(GameData game)
        {
            int[] goodsFallbackIds = { 9406, 9407, 9408, 9409, 9410 };
            int[] eventFallbackIds = { 12000003, 12000004, 12000005, 12000006, 12000008, 10010154, 10010155, 10010156, 10010157, 10010186, 10010187, 10010188, 10010189 };

            foreach (Dictionary<string, FMG> langFmgs in game.OtherItemFMGs.Values)
            {
                CopyMissingFmgEntries(game.ItemFMGs, langFmgs, goodsFallbackIds);
            }

            foreach (Dictionary<string, FMG> langFmgs in game.OtherMenuFMGs.Values)
            {
                CopyMissingFmgEntries(game.MenuFMGs, langFmgs, eventFallbackIds);
            }
        }

        private static void CopyMissingFmgEntries(
            Dictionary<string, FMG> englishFmgs,
            Dictionary<string, FMG> targetFmgs,
            IEnumerable<int> ids)
        {
            foreach (KeyValuePair<string, FMG> entry in englishFmgs)
            {
                if (!targetFmgs.TryGetValue(entry.Key, out FMG targetFmg))
                    continue;

                foreach (int id in ids)
                {
                    string englishText = entry.Value[id];
                    if (string.IsNullOrEmpty(englishText) || !string.IsNullOrEmpty(targetFmg[id]))
                        continue;

                    targetFmg[id] = englishText;
                }
            }
        }

        public static readonly List<string> Langs = new List<string>
        {
            "deude", "engus", "frafr", "itait", "jpnjp", "korkr", "polpl", "porbr", "rusru", "spaar", "spaes", "thath", "zhocn", "zhotw",
        };
        public static readonly List<string> NoDS3Langs = new List<string> { "thath" };

        private static readonly List<string> fileDirs = new List<string>
        {
            @".",
            @"action",
            @"action\script",
            @"chr",
            @"cutscene",
            @"event",
            @"map\mapstudio",
            @"menu",
            @"menu\hi",
            @"menu\hi\mapimage",
            @"menu\low",
            @"menu\low\mapimage",
            @"menu\knowledge",
            @"menu\$lang",
            @"msg\$lang",
            @"mtd",
            @"obj",
            @"other",
            @"param\drawparam",
            @"param\gameparam",
            @"param\graphicsconfig",
            @"parts",
            @"script",  // This should be a no-op with enemy rando
            @"script\talk",
            @"sfx",  // This should be a no-op with enemy rando
            @"shader",
            @"sound",
        }.SelectMany(t => t.Contains("$lang") ? Langs.Select(l => t.Replace("$lang", l)) : new[] { t }).ToList();
        private static List<string> extensions = new List<string>
        {
            ".hks", ".dcx", ".gfx", ".dds", ".fsb", ".fev", ".itl", ".tpf", ".entryfilelist", ".hkxbdt", ".hkxbhd", "Data0.bdt"
        };
        private static Regex extensionRe = new Regex(string.Join("|", extensions.Select(e => e + "$")));
        public static List<string> GetGameFiles(string dir, bool sekiro)
        {
            List<string> allFiles = new List<string>();
            foreach (string subdir in fileDirs)
            {
                if (subdir == "script" || subdir == "sfx") continue;
                string fulldir = $@"{dir}\{subdir}";
                if (Directory.Exists(fulldir))
                {
                    foreach (string path in Directory.GetFiles(fulldir))
                    {
                        if (extensionRe.IsMatch(path))
                        {
                            string filename = Path.GetFileName(path);
                            allFiles.Add($@"{subdir}\{filename}");
                        }
                    }
                }
            }
            return allFiles;
        }
    }
}
