using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace SilksongDeathCounter
{
    internal static class DeathTracker
    {
        private static int runDeaths = 0;
        private static bool dataDirty = false;
        private const double saveIntervalSeconds = 10.0;
        private static double nextSaveTime = 0.0;
        private static int currentSaveSlot = 1;
        private static int cachedTotalDeaths = 0;

        // Boss tracking for deaths
        private static string currentBossJournalName = null;
        private static string currentBossDisplayName = null;
        private static string currentBossSpriteName = null;
        private static string currentBossModKey = null;  // Dodane dla Mod_Key
        private static bool isInBossFight = false;

        /// <summary>
        /// Publiczny getter dla integracji z innymi modami (np. Rich Presence)
        /// Zwraca całkowitą liczbę śmierci dla aktualnego save'a
        /// </summary>
        public static int GetTotalDeaths() => cachedTotalDeaths;

        /// <summary>
        /// Publiczny getter dla liczby śmierci w bieżącej sesji
        /// </summary>
        public static int GetRunDeaths() => runDeaths;

        public static void Init()
        {
            nextSaveTime = Time.realtimeSinceStartupAsDouble + saveIntervalSeconds;
            // Load current save slot and total deaths
            RefreshCurrentSaveData();
        }

        public static void RefreshCurrentSaveData()
        {
            try
            {
                currentSaveSlot = GetCurrentSaveSlot();
                cachedTotalDeaths = GetTotalDeathsForCurrentSave();
                DeathCounterPlugin.LogManager.Info($"📊 DeathTracker initialized: Save {currentSaveSlot}, Total Deaths: {cachedTotalDeaths}");
            }
            catch (System.Exception e)
            {
                // During early initialization, GameManager might not exist yet - that's OK
                DeathCounterPlugin.LogManager.Info($"📊 DeathTracker early init: GameManager not ready yet, using defaults. Error: {e.Message}");
                currentSaveSlot = 1;
                cachedTotalDeaths = 0;
            }
        }

        public static void SetCurrentBoss(string journalName, string displayName, string spriteName, string modKey = null)
        {
            currentBossJournalName = journalName;
            currentBossDisplayName = displayName;
            currentBossSpriteName = spriteName;
            currentBossModKey = modKey;  // Zapisujemy Mod_Key jeśli został podany
            isInBossFight = true;

            DeathCounterPlugin.LogManager.Info($"🐉 BOSS FIGHT STARTED:");
            DeathCounterPlugin.LogManager.Info($"   Journal: {journalName}");
            DeathCounterPlugin.LogManager.Info($"   Display: {displayName}");
            DeathCounterPlugin.LogManager.Info($"   Sprite: {spriteName}");
            DeathCounterPlugin.LogManager.Info($"   Mod_Key: {modKey ?? "will be determined from mapping"}");
        }

        public static void ClearCurrentBoss()
        {
            if (isInBossFight)
            {
                DeathCounterPlugin.LogManager.Info($"🏆 BOSS FIGHT ENDED: {currentBossDisplayName ?? currentBossJournalName ?? "Unknown Boss"}");
            }

            currentBossJournalName = null;
            currentBossDisplayName = null;
            currentBossSpriteName = null;
            currentBossModKey = null;
            isInBossFight = false;
        }

        public static void Update()
        {
            if (ConfigManager.ResetKey.Value.IsDown())
            {
                runDeaths = 0;
                UIManager.UpdateDeathText(cachedTotalDeaths, runDeaths);
                DeathCounterPlugin.LogManager.Info("Run deaths reset.");
            }

            if (ConfigManager.ToggleCounterKey.Value.IsDown())
            {
                ConfigManager.CounterVisible.Value = !ConfigManager.CounterVisible.Value;
                UIManager.UpdateCounterVisibility();
                DeathCounterPlugin.LogManager.Info($"Death counter visibility toggled: {ConfigManager.CounterVisible.Value}");
            }

            if (dataDirty && Time.realtimeSinceStartupAsDouble >= nextSaveTime)
            {
                SaveCurrentDeathData();
                dataDirty = false;
                nextSaveTime = Time.realtimeSinceStartupAsDouble + saveIntervalSeconds;
                DeathCounterPlugin.LogManager.Info("DeathCounter: data saved (periodic).");
            }
        }

        public static void AddDeath()
        {
            cachedTotalDeaths++;
            runDeaths++;
            dataDirty = true;
            nextSaveTime = Time.realtimeSinceStartupAsDouble + saveIntervalSeconds;
            UIManager.UpdateDeathText(cachedTotalDeaths, runDeaths);

            // If we're in a boss fight, record the boss death
            if (isInBossFight && !string.IsNullOrEmpty(currentBossJournalName))
            {
                AddBossDeath(currentBossJournalName, currentBossDisplayName, currentBossSpriteName);
                // Clear boss state after death - player will respawn far away
                ClearCurrentBoss();
                DeathCounterPlugin.LogManager.Info("💀 Player died to boss - clearing boss fight state");
            }

            // Save immediately to file
            SaveCurrentDeathData();
        }

        private static void AddBossDeath(string journalName, string displayName, string spriteName)
        {
            try
            {
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath)) return;

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{currentSaveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                var saveData = LoadDeathCounterData(saveFilePath) ?? new DeathCounterSaveData
                {
                    SaveSlot = currentSaveSlot,
                    TotalDeaths = cachedTotalDeaths,
                    LastUpdated = System.DateTime.Now,
                    BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                };

                // Boss key to klucz z gry (np. "BLUE_ASSISTANT", "LACE", "LACE_2", "GIANT_BONE_FLYER", "GIANT_BONE_FLYER_2")
                string bossKey = journalName ?? "Unknown_Boss";

                // Używamy Mod_Key z currentBossModKey jeśli został ustawiony
                string modKey = currentBossModKey;
                DeathCounterPlugin.LogManager.Info($"🔍 Boss Death - Boss_Key: '{bossKey}', currentBossModKey: '{currentBossModKey ?? "NULL"}'");

                // Jeśli nie ma currentBossModKey, szukamy w mapowaniu
                if (string.IsNullOrEmpty(modKey))
                {
                    modKey = GetModKeyForBossKey(bossKey);
                    DeathCounterPlugin.LogManager.Info($"🗺️ Mod_Key from mapping: '{modKey ?? "NULL"}'");
                }
                else
                {
                    DeathCounterPlugin.LogManager.Info($"✅ Using currentBossModKey: '{modKey}'");
                }

                if (saveData.BossDeaths.ContainsKey(bossKey))
                {
                    // Boss już istnieje - aktualizuj licznik i datę
                    saveData.BossDeaths[bossKey].DeathsCount++;
                    saveData.BossDeaths[bossKey].LastDeath = System.DateTime.Now;

                    // WAŻNE: Ustaw FirstDeath jeśli nie był ustawiony (DateTime.MinValue)
                    if (saveData.BossDeaths[bossKey].FirstDeath == System.DateTime.MinValue)
                    {
                        saveData.BossDeaths[bossKey].FirstDeath = System.DateTime.Now;
                    }

                    // WAŻNE: Zachowaj/aktualizuj Mod_Key jeśli mamy lepszy
                    if (!string.IsNullOrEmpty(modKey) && string.IsNullOrEmpty(saveData.BossDeaths[bossKey].ModKey))
                    {
                        saveData.BossDeaths[bossKey].ModKey = modKey;
                        DeathCounterPlugin.LogManager.Info($"🔧 Updated Mod_Key for {bossKey}: {modKey}");
                    }
                }
                else
                {
                    // Nowy boss - utwórz rekord
                    saveData.BossDeaths[bossKey] = new BossDeathInfo
                    {
                        BossKey = bossKey,
                        ModKey = modKey ?? bossKey, // Fallback do boss key jeśli nie znaleziono
                        DeathsCount = 1,
                        FirstDeath = System.DateTime.Now,
                        LastDeath = System.DateTime.Now
                    };
                    DeathCounterPlugin.LogManager.Info($"📝 Created new boss record: {bossKey} -> {modKey}");
                }

                saveData.LastUpdated = System.DateTime.Now;
                saveData.TotalDeaths = cachedTotalDeaths;
                SaveDeathCounterData(saveFilePath, saveData);

                DeathCounterPlugin.LogManager.Info($"📊 BOSS DEATH RECORDED: {bossKey} -> {saveData.BossDeaths[bossKey].ModKey} (Total: {saveData.BossDeaths[bossKey].DeathsCount})");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error recording boss death: {e}");
            }
        }

        // Mapowanie Boss_Key -> Mod_Key (taka sama logika jak w Patcher.LoadBossDataTemplate)
        private static string GetModKeyForBossKey(string bossKey)
        {
            var bossMapping = new System.Collections.Generic.Dictionary<string, string>
            {
                { "MOSSBONE_MOTHER", "Moss_Mother" },
                { "BELLBEAST", "Bell_Beast" },
                { "LACE", "Lace_1" },
                { "LACE_2", "Lace_2" },
                { "SONG_GOLEM", "Fourth_Chorus" },
                { "GIANT_BONE_FLYER", "Savage_Beastfly_1" },
                { "GIANT_BONE_FLYER_2", "Savage_Beastfly_2" },
                { "SPLINTER_QUEEN", "Sister_Splinter" },
                { "SKULL_KING", "Skull_Tyrant" },
                { "VAMPIRE_GNAT", "Moorwing" },
                { "DRILLERS", "Conchfly" },
                { "PHANTOM", "Phantom" },
                { "LAST_JUDGE", "Last_Judge" },
                { "COGWORK_DANCERS", "Cogwork_Dancers" },
                { "TROBBIO", "Trobbio" },
                { "DOCK_GUARD_SOLO", "Forebrothers_Signis_Gron" },
                { "DOCK_GUARD_THROWER", "Forebrothers_Signis_Gron" },
                { "FOREBROTHERS_FIGHT", "Forebrothers_Signis_Gron" },
                { "CHEF", "Disgraced_Chef_Lugoli" },
                { "WICKER_BUG", "Father_Of_The_Flame" },
                { "SWAMP_SHAMAN", "Groal_The_Great" },
                { "ZAPNEST_BOSS", "Voltvyrm" },
                { "DRILLER_SOLO", "Great_Conchfly" },
                { "FIRST_WEAVER", "First_Sinner" },
                { "BROODMOTHER", "Broodmother" },
                { "CROWFATHER", "Crawfather" },
                { "SONG_KNIGHT", "Second_Sentinel" },
                { "GARMOND_BT", "Garmond_BT" },
                { "CLOVER_DANCERS", "Clover_Dancers" },
                { "TORMENTED_TROBBIO", "Tormented_Trobbio" },
                { "SPINNER", "Widow" },
                { "HUNTER_TRAPPER", "Gurr_The_Outcast" },
                { "ABYSS_MASS", "Summoned_Saviour" },
                { "WHITE_CLOVERSTAG", "Palestag" },
                { "GREY_CORAL_WARRIOR", "Watcher_At_The_Edge" },
                { "FLOWER_QUEEN", "Nyleth" },
                { "HUNTER_QUEEN", "Skarrsinger_Karmelita" },
                { "CORAL_KING", "Crust_King_Khann" },
                { "BIG_CENTIPEDE", "Bell_Eater" },
                { "BLUE_ASSISTANT", "Plasmified_Zango" },
                { "PINSTRESS", "Pinstress" },
                { "SETH", "Shrine_Guardian_Seth" },
                { "WARD_BOSS", "The_Unravelled" },
                { "SILKSONG", "Grand_Mother_Silk" },
                { "LOST_LACE", "Lost_Lace" }
            };

            return bossMapping.ContainsKey(bossKey) ? bossMapping[bossKey] : null;
        }

        private static int GetCurrentSaveSlot()
        {
            try
            {
                var gameManagerType = AccessTools.TypeByName("GameManager");
                if (gameManagerType == null)
                {
                    return 1;
                }

                var instanceProperty = AccessTools.Property(gameManagerType, "instance");
                if (instanceProperty == null)
                {
                    return 1;
                }

                var gameManagerInstance = instanceProperty.GetValue(null);
                if (gameManagerInstance == null)
                {
                    return 1;
                }

                var profileIdField = AccessTools.Field(gameManagerType, "profileID") ??
                                    AccessTools.Field(gameManagerType, "saveSlot") ??
                                    AccessTools.Field(gameManagerType, "currentProfileId");

                if (profileIdField != null)
                {
                    var profileId = profileIdField.GetValue(gameManagerInstance);
                    if (profileId is int slotId)
                    {
                        int actualSaveSlot = slotId >= 1 ? slotId : slotId + 1;
                        return actualSaveSlot;
                    }
                }

                return 1;
            }
            catch (System.Exception)
            {
                return 1;
            }
        }

        private static int GetTotalDeathsForCurrentSave()
        {
            try
            {
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath)) return 0;

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{currentSaveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                var saveData = LoadDeathCounterData(saveFilePath);
                return saveData?.TotalDeaths ?? 0;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading total deaths for save {currentSaveSlot}: {e}");
                return 0;
            }
        }

        private static void SaveCurrentDeathData()
        {
            try
            {
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath)) return;

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{currentSaveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                var saveData = LoadDeathCounterData(saveFilePath) ?? new DeathCounterSaveData
                {
                    SaveSlot = currentSaveSlot,
                    TotalDeaths = 0,
                    LastUpdated = System.DateTime.Now,
                    BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                };

                saveData.TotalDeaths = cachedTotalDeaths;
                saveData.LastUpdated = System.DateTime.Now;

                SaveDeathCounterData(saveFilePath, saveData);
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error saving death data for save {currentSaveSlot}: {e}");
            }
        }

        private static string GetGameDataPath()
        {
            try
            {
                var unityApplicationType = AccessTools.TypeByName("UnityEngine.Application");
                if (unityApplicationType != null)
                {
                    var persistentDataPathProperty = AccessTools.Property(unityApplicationType, "persistentDataPath");
                    if (persistentDataPathProperty != null)
                    {
                        string persistentDataPath = (string)persistentDataPathProperty.GetValue(null);
                        if (!string.IsNullOrEmpty(persistentDataPath))
                        {
                            return persistentDataPath;
                        }
                    }
                }

                string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                string teamCherryPath = System.IO.Path.Combine(documentsPath, "My Games", "Hollow Knight Silksong");
                if (System.IO.Directory.Exists(teamCherryPath))
                {
                    return teamCherryPath;
                }

                string currentDir = System.Environment.CurrentDirectory;
                string relativeSavePath = System.IO.Path.Combine(currentDir, "SaveData");
                return relativeSavePath;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error getting game data path: {e}");
                return null;
            }
        }

        private static void SaveDeathCounterData(string filePath, DeathCounterSaveData data)
        {
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"SaveSlot={data.SaveSlot}");
                lines.Add($"TotalDeaths={data.TotalDeaths}");
                lines.Add($"LastUpdated={data.LastUpdated:yyyy-MM-dd HH:mm:ss}");
                lines.Add("BossDeaths_START");

                foreach (var bossEntry in data.BossDeaths)
                {
                    var bossInfo = bossEntry.Value;

                    // Format zgodny z Patcher.cs - z wcięciami
                    lines.Add($"Boss_Key={bossInfo.BossKey}");
                    lines.Add($"  Mod_Key={bossInfo.ModKey ?? ""}");
                    lines.Add($"  DeathsCount={bossInfo.DeathsCount}");

                    // Puste wartości dla dat, jeśli nie ma śmierci
                    if (bossInfo.FirstDeath != System.DateTime.MinValue)
                        lines.Add($"  FirstDeath={bossInfo.FirstDeath:yyyy-MM-dd HH:mm:ss}");
                    else
                        lines.Add($"  FirstDeath=");

                    if (bossInfo.LastDeath != System.DateTime.MinValue)
                        lines.Add($"  LastDeath={bossInfo.LastDeath:yyyy-MM-dd HH:mm:ss}");
                    else
                        lines.Add($"  LastDeath=");
                }

                lines.Add("BossDeaths_END");

                System.IO.File.WriteAllLines(filePath, lines);
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error saving death counter data to {filePath}: {e}");
            }
        }

        private static DeathCounterSaveData LoadDeathCounterData(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    string[] lines = System.IO.File.ReadAllLines(filePath);
                    var data = new DeathCounterSaveData
                    {
                        BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                    };

                    bool inBossDeaths = false;
                    BossDeathInfo currentBoss = null;

                    foreach (string line in lines)
                    {
                        if (line == "BossDeaths_START")
                        {
                            inBossDeaths = true;
                            continue;
                        }
                        else if (line == "BossDeaths_END")
                        {
                            inBossDeaths = false;
                            continue;
                        }

                        if (inBossDeaths)
                        {
                            // Usuń początkowe wcięcia dla parsowania
                            string cleanLine = line.TrimStart();

                            if (cleanLine.StartsWith("Boss_Key="))
                            {
                                string bossKey = cleanLine.Replace("Boss_Key=", "").Trim();
                                currentBoss = new BossDeathInfo { BossKey = bossKey };
                                data.BossDeaths[bossKey] = currentBoss;
                            }
                            else if (currentBoss != null && cleanLine.StartsWith("Mod_Key="))
                            {
                                currentBoss.ModKey = cleanLine.Replace("Mod_Key=", "").Trim();
                            }
                            else if (currentBoss != null && cleanLine.StartsWith("DeathsCount="))
                            {
                                if (int.TryParse(cleanLine.Replace("DeathsCount=", "").Trim(), out int count))
                                    currentBoss.DeathsCount = count;
                            }
                            else if (currentBoss != null && cleanLine.StartsWith("FirstDeath="))
                            {
                                string dateStr = cleanLine.Replace("FirstDeath=", "").Trim();
                                if (!string.IsNullOrEmpty(dateStr) && System.DateTime.TryParse(dateStr, out var firstDeath))
                                    currentBoss.FirstDeath = firstDeath;
                            }
                            else if (currentBoss != null && cleanLine.StartsWith("LastDeath="))
                            {
                                string dateStr = cleanLine.Replace("LastDeath=", "").Trim();
                                if (!string.IsNullOrEmpty(dateStr) && System.DateTime.TryParse(dateStr, out var lastDeath))
                                    currentBoss.LastDeath = lastDeath;
                            }
                        }
                        else
                        {
                            if (line.StartsWith("SaveSlot=") && int.TryParse(line.Replace("SaveSlot=", ""), out int saveSlot))
                            {
                                data.SaveSlot = saveSlot;
                            }
                            else if (line.StartsWith("TotalDeaths=") && int.TryParse(line.Replace("TotalDeaths=", ""), out int totalDeaths))
                            {
                                data.TotalDeaths = totalDeaths;
                            }
                            else if (line.StartsWith("LastUpdated="))
                            {
                                string dateStr = line.Replace("LastUpdated=", "");
                                if (System.DateTime.TryParse(dateStr, out System.DateTime lastUpdated))
                                {
                                    data.LastUpdated = lastUpdated;
                                }
                            }
                        }
                    }

                    return data;
                }
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading death counter data from {filePath}: {e}");
            }
            return null;
        }
    }
}