using HarmonyLib;
using System;
using System.Reflection;

namespace SilksongDeathCounter
{
    internal static class SaveManager
    {
        private static int currentProfileID = -1;

        public static void OnProfileLoaded(object gameManagerInstance)
        {
            try
            {
                var gmType = gameManagerInstance.GetType();
                var profileField = gmType.GetField("profileID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (profileField == null)
                {
                    DeathCounterPlugin.LogManager.Info("SaveManager: profileField is null.");
                    return;
                }

                int profileID = (int)(profileField.GetValue(gameManagerInstance) ?? -1);
                currentProfileID = profileID;

                // Convert profileID to proper save slot (1-4)
                int saveSlot = profileID >= 1 ? profileID : profileID + 1;

                // Clear any boss fight state when loading a save
                DeathTracker.ClearCurrentBoss();

                // Refresh DeathTracker data for the new save slot
                DeathTracker.RefreshCurrentSaveData();

                // Show UI with current death count from file
                int totalDeaths = GetTotalDeathsFromFile(saveSlot);
                UIManager.UpdateDeathText(totalDeaths, 0);
                UIManager.SetVisible(true);
                DeathCounterPlugin.LogManager.Info($"DeathCounter: switched to Save_{saveSlot} (ProfileID: {profileID}) - Deaths: {totalDeaths}");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("SaveManager: error -> " + e);
            }
        }

        public static void SaveOnExit()
        {
            try
            {
                // No longer need to save BepInEx config - data is saved automatically to files
                DeathCounterPlugin.LogManager.Info("SaveManager: File-based system - no config save needed");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Info("Error in save manager exit -> " + e);
            }
        }

        private static int GetTotalDeathsFromFile(int saveSlot)
        {
            try
            {
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath)) return 0;

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{saveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                var saveData = LoadDeathCounterData(saveFilePath);
                return saveData?.TotalDeaths ?? 0;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading total deaths for save {saveSlot}: {e}");
                return 0;
            }
        }

        // Helper methods - copied from Patcher for consistency
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
                            // Nowy format: Boss_Key=, Mod_Key=, DeathsCount=, FirstDeath=, LastDeath=
                            if (line.StartsWith("Boss_Key="))
                            {
                                string bossKey = line.Replace("Boss_Key=", "").Trim();
                                currentBoss = new BossDeathInfo { BossKey = bossKey };
                                data.BossDeaths[bossKey] = currentBoss;
                            }
                            else if (currentBoss != null && line.StartsWith("Mod_Key="))
                            {
                                currentBoss.ModKey = line.Replace("Mod_Key=", "").Trim();
                            }
                            else if (currentBoss != null && line.StartsWith("DeathsCount="))
                            {
                                if (int.TryParse(line.Replace("DeathsCount=", "").Trim(), out int count))
                                    currentBoss.DeathsCount = count;
                            }
                            else if (currentBoss != null && line.StartsWith("FirstDeath="))
                            {
                                if (System.DateTime.TryParse(line.Replace("FirstDeath=", "").Trim(), out var firstDeath))
                                    currentBoss.FirstDeath = firstDeath;
                            }
                            else if (currentBoss != null && line.StartsWith("LastDeath="))
                            {
                                if (System.DateTime.TryParse(line.Replace("LastDeath=", "").Trim(), out var lastDeath))
                                    currentBoss.LastDeath = lastDeath;
                            }
                            // Legacy format support: Boss=X|Deaths=Y
                            else if (line.Contains("Boss=") && line.Contains("|Deaths="))
                            {
                                string[] parts = line.Split('|');
                                if (parts.Length == 2)
                                {
                                    string bossKey = parts[0].Replace("Boss=", "").Trim();
                                    if (int.TryParse(parts[1].Replace("Deaths=", "").Trim(), out int deaths))
                                    {
                                        data.BossDeaths[bossKey] = new BossDeathInfo
                                        {
                                            BossKey = bossKey,
                                            ModKey = bossKey, // Fallback - use boss key as mod key
                                            DeathsCount = deaths,
                                            FirstDeath = System.DateTime.Now,
                                            LastDeath = System.DateTime.Now
                                        };
                                    }
                                }
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
