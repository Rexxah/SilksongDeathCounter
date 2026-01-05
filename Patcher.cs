using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SilksongDeathCounter
{
    internal static class Patcher
    {
        // Śledzenie aktualnego bossa
        private static string _currentBossKey = null;
        private static string _currentBossName = null;
        private static string _currentBossSprite = null;
        private static object _currentBossHealthManager = null;

        // Śledzenie aktualnej sceny dla specjalnych bossów
        private static string _currentSceneName = null;

        public static void ApplyPatches(Harmony harmony)
        {
            TryPatchDeathMethod(harmony);
            TryPatchAllLoadGameFromUIMethods(harmony);
            TryPatchQuitToMenu(harmony);
            TryPatchGameManagerStartup(harmony);
            TryPatchDisplayBossTitle(harmony);
            TryPatchHealthManagerDeath(harmony);
            TryPatchSceneLoad(harmony);
            TryPatchStartNewGame(harmony);

            // Patch dla menu pauzy
            try
            {
                DeathCounterPlugin.LogManager.Info("🔧 Applying PauseMenuPatcher...");
                harmony.PatchAll(typeof(PauseMenuPatcher));
                DeathCounterPlugin.LogManager.Info("✅ PauseMenuPatcher applied successfully!");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"❌ Error applying PauseMenuPatcher: {e}");
            }
        }

        private static void TryPatchDeathMethod(Harmony harmony)
        {
            try
            {
                MethodInfo deathMethod = AccessTools.Method("HeroVibrationController:PlayHeroDeath");
                if (deathMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Postfix_PlayHeroDeath), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(deathMethod, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching PlayHeroDeath -> " + e);
            }
        }

        private static void TryPatchAllLoadGameFromUIMethods(Harmony harmony)
        {
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                if (gmType == null) return;

                foreach (var m in gmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "LoadGameFromUI") continue;
                    var postfix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(GenericPostfix_AfterLoadGameFromUI), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(m, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching LoadGameFromUI -> " + e);
            }
        }

        private static void Postfix_PlayHeroDeath()
        {
            DeathTracker.AddDeath();
        }

        private static void GenericPostfix_AfterLoadGameFromUI(object __instance)
        {
            SaveManager.OnProfileLoaded(__instance);
        }

        private static void TryPatchQuitToMenu(Harmony harmony)
        {
            try
            {
                MethodInfo quitMethod = AccessTools.Method("QuitToMenu:Start");
                if (quitMethod != null)
                {
                    var postfix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Postfix_QuitToMenu), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(quitMethod, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching QuitToMenu -> " + e);
            }
        }

        private static void Postfix_QuitToMenu()
        {
            UIManager.SetVisible(false);
            // Clear any boss fight state when quitting to menu
            DeathTracker.ClearCurrentBoss();
            DeathCounterPlugin.LogManager.Info("DeathCounter: UI hidden on quit to menu.");
        }

        private static void TryPatchGameManagerStartup(Harmony harmony)
        {
            try
            {
                MethodInfo methodInfo = AccessTools.Method("GameManager:Start");
                if (methodInfo != null)
                {
                    var postfix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Postfix_GameManagerStart), BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(methodInfo, postfix: postfix);
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching GameManager Start -> " + e);
            }
        }

        private static void Postfix_GameManagerStart(object __instance)
        {
            // Inicjalizacja systemu migracji danych - tworzymy folder i migrujemy z BepInEx
            InitializeDeathCounterDataSystem();

            // Pobieramy liczby śmierci dla wszystkich 4 slotów z nowych plików
            int[] deathsPerSlot = new int[4];
            for (int i = 1; i <= 4; i++)
            {
                int deaths = GetTotalDeathsFromFile(i);
                deathsPerSlot[i - 1] = deaths; // Array is 0-indexed, but saves are 1-indexed
                DeathCounterPlugin.LogManager.Info($"📊 FILE Save_{i}: {deaths} deaths -> Array[{i - 1}] = {deaths}");
            }

            // Debug: wyświetlmy co przekazujemy do UI
            DeathCounterPlugin.LogManager.Info("🖥️ UI ARRAY DATA:");
            for (int j = 0; j < deathsPerSlot.Length; j++)
            {
                DeathCounterPlugin.LogManager.Info($"   Array[{j}] = {deathsPerSlot[j]} deaths (will display in UI Save {j + 1})");
            }

            // aktualizujemy UI
            UIManager.UpdateDeathCountersOnSaveSlots(deathsPerSlot);
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

        private static void TryPatchDisplayBossTitle(Harmony harmony)
        {
            try
            {
                var displayBossTitleType = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.DisplayBossTitle");
                if (displayBossTitleType != null)
                {
                    MethodInfo onEnterMethod = AccessTools.Method(displayBossTitleType, "OnEnter");
                    if (onEnterMethod != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Prefix_DisplayBossTitle), BindingFlags.Static | BindingFlags.NonPublic));
                        harmony.Patch(onEnterMethod, prefix: prefix);
                        DeathCounterPlugin.LogManager.Info("Successfully patched DisplayBossTitle.OnEnter");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Warning("DisplayBossTitle.OnEnter method not found");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Warning("DisplayBossTitle type not found");
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching DisplayBossTitle -> " + e);
            }
        }

        private static void Prefix_DisplayBossTitle(object __instance)
        {
            try
            {
                // Pobieramy pole bossTitle z instancji DisplayBossTitle
                var bossTitleField = AccessTools.Field(__instance.GetType(), "bossTitle");
                if (bossTitleField != null)
                {
                    var bossTitle = bossTitleField.GetValue(__instance);
                    if (bossTitle != null)
                    {
                        // FsmString ma właściwość Value
                        var valueProperty = AccessTools.Property(bossTitle.GetType(), "Value");
                        if (valueProperty != null)
                        {
                            string bossTitleKey = (string)valueProperty.GetValue(bossTitle);
                            if (!string.IsNullOrEmpty(bossTitleKey))
                            {
                                DeathCounterPlugin.LogManager.Info($"🔥 BOSS TITLE KEY: {bossTitleKey}");

                                // Zapisujemy aktualnego bossa
                                _currentBossKey = bossTitleKey;
                                _currentBossName = null;
                                _currentBossSprite = null;
                                _currentBossHealthManager = null;

                                // Próbujemy znaleźć HealthManager bossa w scenie
                                TryFindBossHealthManager(bossTitleKey);

                                // Próbujemy przetłumaczyć klucz lokalizacyjny
                                try
                                {
                                    var languageType = AccessTools.TypeByName("TeamCherry.Localization.Language");
                                    if (languageType != null)
                                    {
                                        var getMethod = AccessTools.Method(languageType, "Get", new[] { typeof(string), typeof(string) });
                                        if (getMethod != null)
                                        {
                                            // Próbujemy różne arkusze lokalizacyjne - tylko te które istnieją
                                            string[] sheets = { "Journal", "UI", "General" };
                                            foreach (string sheet in sheets)
                                            {
                                                try
                                                {
                                                    string translatedName = (string)getMethod.Invoke(null, new object[] { bossTitleKey, sheet });
                                                    if (!string.IsNullOrEmpty(translatedName) && translatedName != bossTitleKey && !translatedName.Contains("#!#"))
                                                    {
                                                        DeathCounterPlugin.LogManager.Info($"🌟 BOSS NAME TRANSLATED ({sheet}): {translatedName}");
                                                        break;
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    DeathCounterPlugin.LogManager.Info($"🔍 Sheet '{sheet}' failed: {ex.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    DeathCounterPlugin.LogManager.Warning($"Could not translate boss title: {e.Message}");
                                }

                                // Próbujemy znaleźć dane z Journal
                                TryGetJournalData(bossTitleKey);

                                // Po zebraniu wszystkich danych, informujemy DeathTracker o bossie
                                UpdateDeathTrackerWithBossData(bossTitleKey);
                            }
                            else
                            {
                                DeathCounterPlugin.LogManager.Info("Boss title displayed but text is empty");
                            }
                        }
                        else
                        {
                            DeathCounterPlugin.LogManager.Warning("Could not find Value property on bossTitle");
                        }
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Info("Boss title displayed but bossTitle is null");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Warning("Could not find bossTitle field in DisplayBossTitle");
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in DisplayBossTitle prefix: {e}");
            }
        }

        private static void UpdateDeathTrackerWithBossData(string bossTitleKey)
        {
            try
            {
                // Zapisujemy surowe dane tak jak są
                string bossKey = bossTitleKey;
                string displayName = _currentBossName ?? bossTitleKey;
                string spriteName = _currentBossSprite ?? "";

                // Dynamiczne określanie Mod_Key na podstawie HP dla specjalnych bossów
                string modKey = null;

                // Specjalny przypadek: DOCK_GUARD_SOLO i DOCK_GUARD_THROWER to jeden boss fight
                // Używamy wspólnego klucza "FOREBROTHERS_FIGHT" dla śledzenia
                if (bossTitleKey == "DOCK_GUARD_SOLO" || bossTitleKey == "DOCK_GUARD_THROWER")
                {
                    bossKey = "FOREBROTHERS_FIGHT";  // Wspólny klucz dla obu
                    modKey = "Forebrothers_Signis_Gron";
                    DeathCounterPlugin.LogManager.Info($"🎯 FOREBROTHERS DETECTED: {bossTitleKey} -> Combined key: FOREBROTHERS_FIGHT");
                }
                else if (bossTitleKey == "LACE" && _currentBossHealthManager != null)
                {
                    // Sprawdź HP dla Lace
                    int hp = GetBossHP(_currentBossHealthManager);
                    if (hp > 0)
                    {
                        if (hp < 300)
                        {
                            bossKey = "LACE";
                            modKey = "Lace_1";
                            DeathCounterPlugin.LogManager.Info($"🎯 LACE HP: {hp} -> Lace_1");
                        }
                        else if (hp >= 500)
                        {
                            bossKey = "LACE_2";
                            modKey = "Lace_2";
                            DeathCounterPlugin.LogManager.Info($"🎯 LACE HP: {hp} -> Lace_2");
                        }
                        else
                        {
                            // HP między 300-500 - domyślnie Lace_1
                            bossKey = "LACE";
                            modKey = "Lace_1";
                            DeathCounterPlugin.LogManager.Info($"🎯 LACE HP: {hp} (range 300-500) -> Lace_1 (default)");
                        }
                    }
                }
                else if (bossTitleKey == "GIANT_BONE_FLYER" && _currentBossHealthManager != null)
                {
                    // Sprawdź HP dla Giant Bone Flyer
                    int hp = GetBossHP(_currentBossHealthManager);
                    if (hp > 0)
                    {
                        if (hp < 600)
                        {
                            bossKey = "GIANT_BONE_FLYER";
                            modKey = "Savage_Beastfly_1";
                            DeathCounterPlugin.LogManager.Info($"🎯 GIANT_BONE_FLYER HP: {hp} -> Savage_Beastfly_1");
                        }
                        else
                        {
                            bossKey = "GIANT_BONE_FLYER_2";
                            modKey = "Savage_Beastfly_2";
                            DeathCounterPlugin.LogManager.Info($"🎯 GIANT_BONE_FLYER HP: {hp} -> Savage_Beastfly_2");
                        }
                    }
                }

                DeathCounterPlugin.LogManager.Info($"🎯 RAW BOSS DATA:");
                DeathCounterPlugin.LogManager.Info($"   📋 Boss Key: '{bossKey}'");
                DeathCounterPlugin.LogManager.Info($"   🎮 Mod Key: '{modKey ?? "will be determined later"}'");
                DeathCounterPlugin.LogManager.Info($"   👑 Display Name: '{displayName}'");
                DeathCounterPlugin.LogManager.Info($"   🎨 Sprite Name: '{spriteName}'");

                if (string.IsNullOrEmpty(spriteName))
                {
                    DeathCounterPlugin.LogManager.Warning($"⚠️ WARNING: No sprite found for boss '{bossTitleKey}' - will use fallback");
                }

                // Informujemy DeathTracker o rozpoczęciu walki z bossem - przekazujemy bossKey i modKey
                DeathTracker.SetCurrentBoss(bossKey, displayName, spriteName, modKey);
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error updating DeathTracker with boss data: {e}");
            }
        }

        // Pomocnicza funkcja do pobierania HP bossa
        private static int GetBossHP(object healthManager)
        {
            try
            {
                if (healthManager == null) return 0;

                var hpField = AccessTools.Field(healthManager.GetType(), "hp");
                if (hpField != null)
                {
                    int hp = (int)hpField.GetValue(healthManager);
                    return hp;
                }
                return 0;
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Warning($"Error getting boss HP: {e.Message}");
                return 0;
            }
        }

        private static void TryGetJournalData(string bossTitleKey)
        {
            try
            {
                var enemyJournalManagerType = AccessTools.TypeByName("EnemyJournalManager");
                if (enemyJournalManagerType != null)
                {
                    var getRecordMethod = AccessTools.Method(enemyJournalManagerType, "GetRecord", new[] { typeof(string) });
                    if (getRecordMethod != null)
                    {
                        // Próbujemy różne warianty nazwy - bez mapowania, prosta logika
                        DeathCounterPlugin.LogManager.Info($"🔍 TRYING JOURNAL VARIANTS FOR: {bossTitleKey}");
                        string[] nameVariants = new[] {
                            bossTitleKey, // VAMPIRE_GNAT
                            ConvertToTitleCase(bossTitleKey.Replace("_", " ")), // Vampire Gnat
                            bossTitleKey.Replace("_", " "), // VAMPIRE GNAT
                            bossTitleKey.ToLower().Replace("_", " "), // vampire gnat
                            ConvertToTitleCase(bossTitleKey.Replace("_", "")), // VampireGnat -> Vampiregnat
                            bossTitleKey.Replace("_", ""), // VAMPIREGNAT
                            "Journal_" + bossTitleKey,
                            "Enemy_" + bossTitleKey
                        };

                        foreach (string nameVariant in nameVariants)
                        {
                            try
                            {
                                DeathCounterPlugin.LogManager.Info($"🔍 TRYING JOURNAL KEY: {nameVariant}");
                                var journalRecord = getRecordMethod.Invoke(null, new object[] { nameVariant });
                                if (journalRecord != null)
                                {
                                    DeathCounterPlugin.LogManager.Info($"📖 FOUND JOURNAL RECORD: {nameVariant}");

                                    // Pobieramy DisplayName
                                    var displayNameProperty = AccessTools.Property(journalRecord.GetType(), "DisplayName");
                                    if (displayNameProperty != null)
                                    {
                                        var displayName = displayNameProperty.GetValue(journalRecord);
                                        string displayNameStr = GetLocalisedStringValue(displayName);
                                        DeathCounterPlugin.LogManager.Info($"👑 JOURNAL DISPLAY NAME: {displayNameStr}");

                                        // Zapisujemy nazwę do śledzenia bossa
                                        if (displayNameStr != null && !displayNameStr.Contains("key:"))
                                        {
                                            _currentBossName = displayNameStr;
                                        }
                                    }

                                    // ZAWSZE sprawdź zarówno IconSprite jak i EnemySprite dla debugowania
                                    var iconSpriteProperty = AccessTools.Property(journalRecord.GetType(), "IconSprite");
                                    var enemySpriteProperty = AccessTools.Property(journalRecord.GetType(), "EnemySprite");

                                    string iconSpriteName = "NULL";
                                    string enemySpriteName = "NULL";

                                    if (iconSpriteProperty != null)
                                    {
                                        var iconSprite = iconSpriteProperty.GetValue(journalRecord);
                                        if (iconSprite != null)
                                        {
                                            var iconSpriteNameProperty = AccessTools.Property(iconSprite.GetType(), "name");
                                            iconSpriteName = (string)iconSpriteNameProperty.GetValue(iconSprite);
                                        }
                                    }

                                    if (enemySpriteProperty != null)
                                    {
                                        var enemySprite = enemySpriteProperty.GetValue(journalRecord);
                                        if (enemySprite != null)
                                        {
                                            var enemySpriteNameProperty = AccessTools.Property(enemySprite.GetType(), "name");
                                            enemySpriteName = (string)enemySpriteNameProperty.GetValue(enemySprite);
                                        }
                                    }

                                    DeathCounterPlugin.LogManager.Info($"🔍 SPRITE DEBUG: IconSprite='{iconSpriteName}', EnemySprite='{enemySpriteName}'");

                                    // Bez mapowania - ZAWSZE preferuj IconSprite
                                    if (iconSpriteName != "NULL")
                                    {
                                        DeathCounterPlugin.LogManager.Info($"🎨 USING ICON SPRITE: {iconSpriteName}");
                                        _currentBossSprite = iconSpriteName;
                                    }
                                    else if (enemySpriteName != "NULL")
                                    {
                                        DeathCounterPlugin.LogManager.Warning($"⚠️ IconSprite not available, using EnemySprite as fallback: {enemySpriteName}");
                                        _currentBossSprite = enemySpriteName;
                                    }
                                    else
                                    {
                                        DeathCounterPlugin.LogManager.Error($"❌ NO SPRITES AVAILABLE for {bossTitleKey}!");
                                    }

                                    // Pobieramy Description
                                    var descriptionProperty = AccessTools.Property(journalRecord.GetType(), "Description");
                                    if (descriptionProperty != null)
                                    {
                                        var description = descriptionProperty.GetValue(journalRecord);
                                        DeathCounterPlugin.LogManager.Info($"📝 JOURNAL DESCRIPTION: {GetLocalisedStringValue(description)}");
                                    }

                                    return; // Znaleźliśmy rekord, kończymy
                                }
                            }
                            catch { }
                        }
                    }
                }
                DeathCounterPlugin.LogManager.Info($"📖 No journal record found for: {bossTitleKey}");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Warning($"Error getting journal data: {e.Message}");
            }
        }

        private static string ConvertToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            string[] words = input.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", words);
        }

        private static string GetLocalisedStringValue(object localisedString)
        {
            if (localisedString == null) return "null";

            try
            {
                // Sprawdzamy czy to LocalisedString i próbujemy różne podejścia
                var type = localisedString.GetType();

                // Próba 1: Może ma metodę ToString() która zwraca przetłumaczony tekst
                string toStringResult = localisedString.ToString();
                if (!string.IsNullOrEmpty(toStringResult) && toStringResult != type.Name)
                {
                    return toStringResult;
                }

                // Próba 2: Sprawdzamy czy ma pole "key" lub "m_key"
                var keyField = AccessTools.Field(type, "key") ?? AccessTools.Field(type, "m_key") ?? AccessTools.Field(type, "_key");
                var sheetField = AccessTools.Field(type, "sheet") ?? AccessTools.Field(type, "m_sheet") ?? AccessTools.Field(type, "_sheet");

                if (keyField != null && sheetField != null)
                {
                    string key = (string)keyField.GetValue(localisedString);
                    string sheet = (string)sheetField.GetValue(localisedString);

                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(sheet))
                    {
                        // Próbujemy przetłumaczyć
                        var languageType = AccessTools.TypeByName("TeamCherry.Localization.Language");
                        if (languageType != null)
                        {
                            var getMethod = AccessTools.Method(languageType, "Get", new[] { typeof(string), typeof(string) });
                            if (getMethod != null)
                            {
                                try
                                {
                                    string translated = (string)getMethod.Invoke(null, new object[] { key, sheet });
                                    if (!string.IsNullOrEmpty(translated) && !translated.Contains("#!#"))
                                    {
                                        return translated;
                                    }
                                }
                                catch { }
                            }
                        }
                        return $"key: {key}, sheet: {sheet}";
                    }
                }

                // Próba 3: Sprawdzamy czy ma właściwości Key i Sheet (wielkimi literami)
                var keyProperty = AccessTools.Property(type, "key") ?? AccessTools.Property(type, "Key");
                var sheetProperty = AccessTools.Property(type, "sheet") ?? AccessTools.Property(type, "Sheet");

                if (keyProperty != null && sheetProperty != null)
                {
                    string key = (string)keyProperty.GetValue(localisedString);
                    string sheet = (string)sheetProperty.GetValue(localisedString);

                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(sheet))
                    {
                        return $"key: {key}, sheet: {sheet}";
                    }
                }

                // Jeśli nic nie zadziałało, zwracamy ToString()
                return toStringResult;
            }
            catch (Exception ex)
            {
                DeathCounterPlugin.LogManager.Info($"🔍 GetLocalisedStringValue error: {ex.Message}");
                return localisedString.ToString();
            }
        }

        private static void TryPatchHealthManagerDeath(Harmony harmony)
        {
            try
            {
                // Patch BossSceneController CheckBossesDead - to jest wywoływane gdy boss umiera
                var bossSceneControllerType = AccessTools.TypeByName("BossSceneController");
                if (bossSceneControllerType != null)
                {
                    MethodInfo checkBossesDeadMethod = AccessTools.Method(bossSceneControllerType, "CheckBossesDead");
                    if (checkBossesDeadMethod != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Prefix_CheckBossesDead), BindingFlags.Static | BindingFlags.NonPublic));
                        harmony.Patch(checkBossesDeadMethod, prefix: prefix);
                        DeathCounterPlugin.LogManager.Info("Successfully patched BossSceneController.CheckBossesDead");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Warning("BossSceneController.CheckBossesDead method not found");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Warning("BossSceneController type not found");
                }

                // Dodatkowo patch HealthManager.Die jako backup
                var healthManagerType = AccessTools.TypeByName("HealthManager");
                if (healthManagerType != null)
                {
                    MethodInfo dieMethod = AccessTools.Method(healthManagerType, "Die", new[] {
                        typeof(float?), typeof(object), typeof(object), typeof(GameObject),
                        typeof(bool), typeof(float), typeof(bool), typeof(bool)
                    });
                    if (dieMethod != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Prefix_HealthManagerDie), BindingFlags.Static | BindingFlags.NonPublic));
                        harmony.Patch(dieMethod, prefix: prefix);
                        DeathCounterPlugin.LogManager.Info("Successfully patched HealthManager.Die");
                    }
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching boss death methods -> " + e);
            }
        }

        private static void Prefix_CheckBossesDead(object __instance)
        {
            try
            {
                // Sprawdzamy czy mamy aktualnego bossa
                if (!string.IsNullOrEmpty(_currentBossKey))
                {
                    // Sprawdzamy pole bossesLeft w BossSceneController
                    var bossesLeftField = AccessTools.Field(__instance.GetType(), "bossesLeft");
                    if (bossesLeftField != null)
                    {
                        int bossesLeft = (int)bossesLeftField.GetValue(__instance);
                        DeathCounterPlugin.LogManager.Info($"🔍 BOSSES LEFT: {bossesLeft}");

                        // Jeśli to był ostatni boss (bossesLeft będzie 0), to nasz boss umarł
                        if (bossesLeft <= 0)
                        {
                            DeathCounterPlugin.LogManager.Info($"💀 BOSS DEFEATED: {_currentBossName ?? _currentBossKey ?? "Unknown Boss"}");

                            // Informujemy DeathTracker że walka z bossem się skończyła
                            DeathTracker.ClearCurrentBoss();

                            // Resetujemy śledzenie bossa
                            _currentBossKey = null;
                            _currentBossName = null;
                            _currentBossSprite = null;
                            _currentBossHealthManager = null;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in CheckBossesDead prefix: {e}");
            }
        }

        private static void Prefix_HealthManagerDie(object __instance)
        {
            try
            {
                // Sprawdzamy czy to śmierć aktualnego bossa
                if (_currentBossHealthManager != null && __instance == _currentBossHealthManager)
                {
                    DeathCounterPlugin.LogManager.Info($"💀 BOSS DEFEATED (HealthManager.Die): {_currentBossName ?? _currentBossKey ?? "Unknown Boss"}");

                    // Informujemy DeathTracker że walka z bossem się skończyła
                    DeathTracker.ClearCurrentBoss();

                    // Resetujemy śledzenie bossa
                    _currentBossKey = null;
                    _currentBossName = null;
                    _currentBossSprite = null;
                    _currentBossHealthManager = null;
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in HealthManager.Die prefix: {e}");
            }
        }

        private static void TryFindBossHealthManager(string bossTitleKey)
        {
            try
            {
                // Szukamy wszystkich HealthManager'ów w scenie
                var healthManagerType = AccessTools.TypeByName("HealthManager");
                if (healthManagerType != null)
                {
                    var healthManagers = UnityEngine.Object.FindObjectsOfType(healthManagerType);

                    DeathCounterPlugin.LogManager.Info($"🔍 FOUND {healthManagers.Length} HealthManagers in scene");

                    foreach (var hm in healthManagers)
                    {
                        try
                        {
                            // Sprawdzamy czy to może być boss - szukamy dużego HP lub specjalnych właściwości
                            var gameObject = AccessTools.Property(hm.GetType(), "gameObject")?.GetValue(hm);
                            if (gameObject != null)
                            {
                                var goName = AccessTools.Property(gameObject.GetType(), "name")?.GetValue(gameObject) as string;

                                // Sprawdzamy HP - to jest pole, nie właściwość
                                var hpField = AccessTools.Field(hm.GetType(), "hp");
                                if (hpField != null)
                                {
                                    int hp = (int)hpField.GetValue(hm);

                                    // Boss prawdopodobnie ma więcej niż 50 HP
                                    if (hp > 50)
                                    {
                                        DeathCounterPlugin.LogManager.Info($"🎯 POTENTIAL BOSS: {goName} (HP: {hp})");

                                        // Dla pierwszego potencjalnego bossa z wysokim HP, zapisujemy go i subskrybujemy na OnDeath
                                        if (_currentBossHealthManager == null)
                                        {
                                            _currentBossHealthManager = hm;
                                            DeathCounterPlugin.LogManager.Info($"👑 TRACKING BOSS: {goName} (HP: {hp})");

                                            // Subskrybujemy bezpośrednio na event OnDeath
                                            SubscribeToBossDeathEvent(hm);
                                        }
                                    }
                                    else
                                    {
                                        DeathCounterPlugin.LogManager.Info($"🐛 Regular enemy: {goName} (HP: {hp})");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DeathCounterPlugin.LogManager.Info($"Error checking HealthManager: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Warning($"Error finding boss HealthManager: {e.Message}");
            }
        }

        private static void SubscribeToBossDeathEvent(object healthManager)
        {
            try
            {
                // Znajdujemy event OnDeath przez reflection
                var eventInfo = healthManager.GetType().GetEvent("OnDeath");
                if (eventInfo != null)
                {
                    // Tworzymy delegate do naszej metody callback
                    var deathEventType = AccessTools.TypeByName("HealthManager+DeathEvent");
                    if (deathEventType != null)
                    {
                        var callbackMethod = typeof(Patcher).GetMethod(nameof(OnBossDeathCallback), BindingFlags.Static | BindingFlags.NonPublic);
                        var callback = System.Delegate.CreateDelegate(deathEventType, callbackMethod);
                        eventInfo.AddEventHandler(healthManager, callback);
                        DeathCounterPlugin.LogManager.Info("🎯 SUBSCRIBED TO BOSS DEATH EVENT");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Warning("HealthManager+DeathEvent type not found");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Warning("OnDeath event not found on HealthManager");
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error subscribing to boss death event: {e}");
            }
        }

        private static void OnBossDeathCallback()
        {
            try
            {
                string bossName = _currentBossName ?? _currentBossKey ?? "Unknown Boss";
                DeathCounterPlugin.LogManager.Info($"💀 BOSS DEFEATED (OnDeath Event): {bossName}");

                // Zapisujemy śmierć bossa do pliku
                SaveBossDeathToFile(bossName);

                // Informujemy DeathTracker że walka z bossem się skończyła
                DeathTracker.ClearCurrentBoss();

                // Resetujemy śledzenie bossa
                _currentBossKey = null;
                _currentBossName = null;
                _currentBossSprite = null;
                _currentBossHealthManager = null;
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in boss death callback: {e}");
            }
        }

        private static void SaveBossDeathToFile(string bossName)
        {
            // Boss death is now handled by DeathTracker when player dies
            // This method is kept for boss defeats (when boss dies, not player)
            try
            {
                DeathCounterPlugin.LogManager.Info($"📊 BOSS DEFEATED: {bossName} - Death tracking handled by DeathTracker");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in boss defeat logging: {e}");
            }
        }

        private static int GetCurrentSaveSlot()
        {
            try
            {
                // Próbujemy pobrać aktualny slot zapisu z GameManager
                var gameManagerType = AccessTools.TypeByName("GameManager");
                if (gameManagerType != null)
                {
                    var instanceProperty = AccessTools.Property(gameManagerType, "instance");
                    if (instanceProperty != null)
                    {
                        var gameManagerInstance = instanceProperty.GetValue(null);
                        if (gameManagerInstance != null)
                        {
                            // Szukamy pola z numerem slotu zapisu
                            var profileIdField = AccessTools.Field(gameManagerType, "profileID") ??
                                                AccessTools.Field(gameManagerType, "saveSlot") ??
                                                AccessTools.Field(gameManagerType, "currentProfileId");

                            if (profileIdField != null)
                            {
                                var profileId = profileIdField.GetValue(gameManagerInstance);
                                if (profileId is int slotId)
                                {
                                    // Jeśli game używa 0-3, konwertujemy na 1-4
                                    // Jeśli game używa 1-4, zostawiamy jak jest
                                    int actualSaveSlot = slotId >= 1 ? slotId : slotId + 1;
                                    return actualSaveSlot;
                                }
                            }
                        }
                    }
                }

                // Fallback - domyślnie slot 1
                return 1;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Warning($"Could not determine current save slot: {e.Message}");
                return 1; // Domyślnie slot 1
            }
        }

        private static void InitializeDeathCounterDataSystem()
        {
            try
            {
                // Pobieramy ścieżkę do folderu z zapisami gry
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath))
                {
                    DeathCounterPlugin.LogManager.Warning("Could not determine game data path for death counter migration");
                    return;
                }

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");

                // Tworzymy folder DeathCounterData jeśli nie istnieje
                if (!System.IO.Directory.Exists(deathCounterDataPath))
                {
                    System.IO.Directory.CreateDirectory(deathCounterDataPath);
                    DeathCounterPlugin.LogManager.Info($"📁 CREATED DeathCounterData folder: {deathCounterDataPath}");
                }

                // Sprawdzamy i migrujemy dane dla każdego slotu zapisu (Save_1 do Save_4)
                for (int saveSlot = 1; saveSlot <= 4; saveSlot++)
                {
                    string saveFileName = $"DeathCounterData_Save_{saveSlot}.txt";
                    string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                    // Sprawdzamy czy plik już istnieje
                    if (!System.IO.File.Exists(saveFilePath))
                    {
                        // MIGRATION ONLY: Pobieramy dane z BepInEx config jeden raz
                        int deathsFromConfig = 0;
                        try
                        {
                            // Próbujemy odczytać z config tylko dla migracji - używamy bezpośredniego dostępu
                            var pluginConfig = DeathCounterPlugin.Instance?.Config;
                            if (pluginConfig != null)
                            {
                                string section = "Save_" + saveSlot;
                                var configEntry = pluginConfig.Bind(section, "TotalDeaths", 0, "Migrated deaths count.");
                                deathsFromConfig = configEntry.Value;
                            }
                        }
                        catch
                        {
                            // Jeśli nie można odczytać z config, rozpoczynamy od 0
                            deathsFromConfig = 0;
                        }

                        // Tworzymy nowy plik z danymi z template'a wszystkich bossów
                        var saveData = LoadBossDataTemplate();
                        if (saveData == null)
                        {
                            // Fallback: tworzymy pusty save data
                            saveData = new DeathCounterSaveData
                            {
                                SaveSlot = saveSlot,
                                TotalDeaths = deathsFromConfig,
                                LastUpdated = System.DateTime.Now,
                                BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                            };
                        }

                        // Aktualizujemy save slot i total deaths
                        saveData.SaveSlot = saveSlot;
                        saveData.TotalDeaths = deathsFromConfig;
                        saveData.LastUpdated = System.DateTime.Now;

                        SaveDeathCounterData(saveFilePath, saveData);
                        DeathCounterPlugin.LogManager.Info($"📄 CREATED Save_{saveSlot} with {saveData.BossDeaths.Count} bosses -> {saveFilePath}");
                    }
                    else
                    {
                        // Plik już istnieje, sprawdzamy czy dane są aktualne
                        var existingData = LoadDeathCounterData(saveFilePath);
                        if (existingData != null)
                        {
                            DeathCounterPlugin.LogManager.Info($"📄 FOUND existing Save_{saveSlot}: {existingData.TotalDeaths} deaths");
                        }
                    }
                }

                // Weryfikacja migracji - sprawdzamy czy pliki zostały utworzone
                DeathCounterPlugin.LogManager.Info("📋 MIGRATION VERIFICATION:");
                for (int saveSlot = 1; saveSlot <= 4; saveSlot++)
                {
                    string verifyFileName = $"DeathCounterData_Save_{saveSlot}.txt";
                    string verifyFilePath = System.IO.Path.Combine(deathCounterDataPath, verifyFileName);
                    var fileData = LoadDeathCounterData(verifyFilePath);

                    if (fileData != null)
                    {
                        DeathCounterPlugin.LogManager.Info($"✅ File Save_{saveSlot}: {fileData.TotalDeaths} deaths (Ready)");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Warning($"❌ File Save_{saveSlot}: Not found or corrupted");
                    }
                }

                DeathCounterPlugin.LogManager.Info($"✅ DeathCounterData system initialized successfully");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error initializing DeathCounterData system: {e}");
            }
        }

        private static string GetGameDataPath()
        {
            try
            {
                // Próbujemy różne sposoby znalezienia ścieżki do zapisów gry

                // Sposób 1: Unity Application.persistentDataPath
                var unityApplicationType = AccessTools.TypeByName("UnityEngine.Application");
                if (unityApplicationType != null)
                {
                    var persistentDataPathProperty = AccessTools.Property(unityApplicationType, "persistentDataPath");
                    if (persistentDataPathProperty != null)
                    {
                        string persistentDataPath = (string)persistentDataPathProperty.GetValue(null);
                        if (!string.IsNullOrEmpty(persistentDataPath))
                        {
                            DeathCounterPlugin.LogManager.Info($"🔍 FOUND persistentDataPath: {persistentDataPath}");
                            return persistentDataPath;
                        }
                    }
                }

                // Sposób 2: Standardowa ścieżka Windows dla Team Cherry
                string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                string teamCherryPath = System.IO.Path.Combine(documentsPath, "My Games", "Hollow Knight Silksong");
                if (System.IO.Directory.Exists(teamCherryPath))
                {
                    DeathCounterPlugin.LogManager.Info($"🔍 FOUND Team Cherry path: {teamCherryPath}");
                    return teamCherryPath;
                }

                // Sposób 3: Ścieżka względna od executable
                string currentDir = System.Environment.CurrentDirectory;
                string relativeSavePath = System.IO.Path.Combine(currentDir, "SaveData");
                DeathCounterPlugin.LogManager.Info($"🔍 USING relative path: {relativeSavePath}");
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
                // Używamy prostego formatowania tekstu zamiast JSON
                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"SaveSlot={data.SaveSlot}");
                lines.Add($"TotalDeaths={data.TotalDeaths}");
                lines.Add($"LastUpdated={data.LastUpdated:yyyy-MM-dd HH:mm:ss}");
                lines.Add("BossDeaths_START");

                foreach (var bossEntry in data.BossDeaths)
                {
                    var bossInfo = bossEntry.Value;

                    // Nowy prosty format: Boss_Key, Mod_Key, DeathsCount, FirstDeath, LastDeath
                    lines.Add($"Boss_Key={bossEntry.Key}");
                    lines.Add($"  Mod_Key={bossInfo.ModKey}");
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
                    string currentBossKey = null;

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

                        if (!inBossDeaths)
                        {
                            // Parse header data
                            if (line.StartsWith("SaveSlot=") && int.TryParse(line.Replace("SaveSlot=", ""), out int slot))
                                data.SaveSlot = slot;
                            else if (line.StartsWith("TotalDeaths=") && int.TryParse(line.Replace("TotalDeaths=", ""), out int deaths))
                                data.TotalDeaths = deaths;
                            else if (line.StartsWith("LastUpdated=") && System.DateTime.TryParse(line.Replace("LastUpdated=", ""), out var updated))
                                data.LastUpdated = updated;
                        }
                        else
                        {
                            // Parse boss data - nowy format
                            if (line.StartsWith("Boss_Key="))
                            {
                                currentBossKey = line.Replace("Boss_Key=", "");
                                data.BossDeaths[currentBossKey] = new BossDeathInfo
                                {
                                    BossKey = currentBossKey
                                };
                            }
                            else if (line.StartsWith("  ") && !string.IsNullOrEmpty(currentBossKey) && data.BossDeaths.ContainsKey(currentBossKey))
                            {
                                string cleanLine = line.Trim();
                                var currentBoss = data.BossDeaths[currentBossKey];

                                if (cleanLine.StartsWith("Mod_Key="))
                                    currentBoss.ModKey = cleanLine.Replace("Mod_Key=", "");
                                else if (cleanLine.StartsWith("DeathsCount=") && int.TryParse(cleanLine.Replace("DeathsCount=", ""), out int count))
                                    currentBoss.DeathsCount = count;
                                else if (cleanLine.StartsWith("FirstDeath="))
                                {
                                    string dateStr = cleanLine.Replace("FirstDeath=", "");
                                    if (!string.IsNullOrEmpty(dateStr) && System.DateTime.TryParse(dateStr, out var firstDeath))
                                        currentBoss.FirstDeath = firstDeath;
                                }
                                else if (cleanLine.StartsWith("LastDeath="))
                                {
                                    string dateStr = cleanLine.Replace("LastDeath=", "");
                                    if (!string.IsNullOrEmpty(dateStr) && System.DateTime.TryParse(dateStr, out var lastDeath))
                                        currentBoss.LastDeath = lastDeath;
                                }
                            }
                        }
                    }

                    return data;
                }
                return null;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading death counter data from {filePath}: {e}");
                return null;
            }
        }

        // Ładuje template z wszystkimi bossami
        private static DeathCounterSaveData LoadBossDataTemplate()
        {
            try
            {
                DeathCounterPlugin.LogManager.Info($"📋 Creating boss data template from internal data");

                var templateData = new DeathCounterSaveData
                {
                    SaveSlot = 0,
                    TotalDeaths = 0,
                    LastUpdated = System.DateTime.Now,
                    BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                };

                // Definicja wszystkich bossów - mapowanie Boss_Key -> Mod_Key (nazwa karty w UI)
                // UWAGA: LACE i GIANT_BONE_FLYER używają domyślnych wartości - rzeczywiste Mod_Key są określane dynamicznie na podstawie HP
                var bosses = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "MOSSBONE_MOTHER", "Moss_Mother" },
                    { "BELLBEAST", "Bell_Beast" },
                    { "LACE", "Lace_1" },  // Dynamiczne: HP < 300 = Lace_1, HP >= 500 = Lace_2
                    { "LACE_2", "Lace_2" },  // Wpis dla Lace_2
                    { "SONG_GOLEM", "Fourth_Chorus" },
                    { "GIANT_BONE_FLYER", "Savage_Beastfly_1" },  // Dynamiczne: HP < 600 = Savage_Beastfly_1, HP >= 600 = Savage_Beastfly_2
                    { "GIANT_BONE_FLYER_2", "Savage_Beastfly_2" },  // Wpis dla Savage_Beastfly_2
                    { "SPLINTER_QUEEN", "Sister_Splinter" },
                    { "SKULL_KING", "Skull_Tyrant" },
                    { "VAMPIRE_GNAT", "Moorwing" },
                    { "DRILLERS", "Conchfly" },
                    { "PHANTOM", "Phantom" },
                    { "LAST_JUDGE", "Last_Judge" },
                    { "COGWORK_DANCERS", "Cogwork_Dancers" },
                    { "TROBBIO", "Trobbio" },
                    { "DOCK_GUARD_SOLO", "Forebrothers_Signis_Gron" },  // Oba DOCK_GUARD wskazują na to samo
                    { "DOCK_GUARD_THROWER", "Forebrothers_Signis_Gron" },  // bo to jeden boss fight z dwoma przeciwnikami
                    { "FOREBROTHERS_FIGHT", "Forebrothers_Signis_Gron" },  // Wspólny klucz dla walki z Forebrothers
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
                    { "SILKSONG", "Grand_Mother_Silk" },  // Dodane z Boss_Data_Unified
                    { "LOST_LACE", "Lost_Lace" }  // Dodane z Boss_Data_Unified
                };

                // Tworzymy wpisy dla każdego bossa
                foreach (var boss in bosses)
                {
                    templateData.BossDeaths[boss.Key] = new BossDeathInfo
                    {
                        BossKey = boss.Key,
                        ModKey = boss.Value,
                        DeathsCount = 0,
                        FirstDeath = System.DateTime.MinValue,
                        LastDeath = System.DateTime.MinValue
                    };
                }

                DeathCounterPlugin.LogManager.Info($"✅ Created template with {templateData.BossDeaths.Count} bosses");
                return templateData;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error creating boss data template: {e}");

                // Fallback: zwracamy pusty template
                return new DeathCounterSaveData
                {
                    SaveSlot = 0,
                    TotalDeaths = 0,
                    LastUpdated = System.DateTime.Now,
                    BossDeaths = new System.Collections.Generic.Dictionary<string, BossDeathInfo>()
                };
            }
        }

        private static void TryPatchSceneLoad(Harmony harmony)
        {
            try
            {
                // Subskrybuj event SceneManager.sceneLoaded aby śledzić zmiany scen
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                DeathCounterPlugin.LogManager.Info("Successfully subscribed to SceneManager.sceneLoaded event");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error subscribing to scene load event -> " + e);
            }
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                // Zapisz aktualną scenę
                _currentSceneName = scene.name;
                DeathCounterPlugin.LogManager.Info($"🗺️ Scene loaded: {_currentSceneName}");

                // Sprawdź czy to specjalna scena z bossem
                CheckForSpecialBossScene(_currentSceneName);
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in OnSceneLoaded: {e}");
            }
        }

        private static void TryPatchStartNewGame(Harmony harmony)
        {
            try
            {
                var uiManagerType = AccessTools.TypeByName("UIManager");
                if (uiManagerType != null)
                {
                    MethodInfo startNewGameMethod = AccessTools.Method(uiManagerType, "StartNewGame", new[] { typeof(bool), typeof(bool) });
                    if (startNewGameMethod != null)
                    {
                        var prefix = new HarmonyMethod(typeof(Patcher).GetMethod(nameof(Prefix_StartNewGame), BindingFlags.Static | BindingFlags.NonPublic));
                        harmony.Patch(startNewGameMethod, prefix: prefix);
                        DeathCounterPlugin.LogManager.Info("Successfully patched UIManager.StartNewGame");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Warning("UIManager.StartNewGame method not found");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Warning("UIManager type not found");
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error("Error patching UIManager.StartNewGame -> " + e);
            }
        }

        private static void Prefix_StartNewGame(bool permaDeath, bool bossRush)
        {
            try
            {
                DeathCounterPlugin.LogManager.Info("🎮 NEW GAME STARTED - Resetting death counter data...");

                // Pobierz aktualny save slot
                int saveSlot = GetCurrentSaveSlot();
                DeathCounterPlugin.LogManager.Info($"📂 Resetting data for Save Slot {saveSlot}");

                // Zresetuj dane dla tego save slotu
                ResetDeathCounterDataForSaveSlot(saveSlot);

                DeathCounterPlugin.LogManager.Info($"✅ Death counter data reset complete for Save Slot {saveSlot}");
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error in StartNewGame prefix: {e}");
            }
        }

        private static void ResetDeathCounterDataForSaveSlot(int saveSlot)
        {
            try
            {
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath))
                {
                    DeathCounterPlugin.LogManager.Warning("Could not determine game data path for reset");
                    return;
                }

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{saveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                // Tworzymy świeży szablon z zerami
                var freshTemplate = LoadBossDataTemplate();
                if (freshTemplate != null)
                {
                    freshTemplate.SaveSlot = saveSlot;
                    freshTemplate.TotalDeaths = 0;
                    freshTemplate.LastUpdated = System.DateTime.Now;

                    // Zapisz świeży szablon
                    SaveDeathCounterData(saveFilePath, freshTemplate);
                    DeathCounterPlugin.LogManager.Info($"✨ Created fresh death counter template for Save {saveSlot} with {freshTemplate.BossDeaths.Count} bosses (all at 0 deaths)");
                }
                else
                {
                    DeathCounterPlugin.LogManager.Error("Failed to create fresh template for reset");
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error resetting death counter data for save {saveSlot}: {e}");
            }
        }

        private static void CheckForSpecialBossScene(string sceneName)
        {
            try
            {
                // Sprawdzamy czy to scena Silk Mother
                if (sceneName == "Cradle_03")
                {
                    DeathCounterPlugin.LogManager.Info("🕷️ SILK MOTHER SCENE DETECTED! Initializing boss tracking...");

                    // Inicjalizujemy śledzenie Silk Mother - używamy SILKSONG jako Boss_Key
                    _currentBossKey = "SILKSONG";
                    _currentBossName = "Grand Mother Silk";
                    _currentBossSprite = ""; // Będzie pobrane z Journal jeśli dostępne

                    // Informujemy DeathTracker o rozpoczęciu walki z Silk Mother
                    DeathTracker.SetCurrentBoss("SILKSONG", "Grand Mother Silk", "", "Grand_Mother_Silk");
                    DeathCounterPlugin.LogManager.Info("👑 Silk Mother boss tracking initialized!");
                }
                // Sprawdzamy czy to scena Lost Lace
                else if (sceneName == "Abyss_Cocoon")
                {
                    DeathCounterPlugin.LogManager.Info("🦋 LOST LACE SCENE DETECTED! Initializing boss tracking...");

                    // Inicjalizujemy śledzenie Lost Lace - używamy LOST_LACE jako Boss_Key
                    _currentBossKey = "LOST_LACE";
                    _currentBossName = "Lost Lace";
                    _currentBossSprite = ""; // Będzie pobrane z Journal jeśli dostępne

                    // Informujemy DeathTracker o rozpoczęciu walki z Lost Lace
                    DeathTracker.SetCurrentBoss("LOST_LACE", "Lost Lace", "", "Lost_Lace");
                    DeathCounterPlugin.LogManager.Info("👑 Lost Lace boss tracking initialized!");
                }
                else
                {
                    // Jeśli opuściliśmy specjalną scenę, wyczyść tracking
                    if (_currentBossKey == "SILKSONG" || _currentBossKey == "LOST_LACE")
                    {
                        DeathCounterPlugin.LogManager.Info($"🚪 Left special boss scene ({_currentBossKey}), clearing tracking");
                        DeathTracker.ClearCurrentBoss();
                        _currentBossKey = null;
                        _currentBossName = null;
                        _currentBossSprite = null;
                        _currentBossHealthManager = null;
                    }
                }
            }
            catch (Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error checking boss scene: {e}");
            }
        }
    }

    // Klasa do przechowywania informacji o śmierciach na bossie
    public class BossDeathInfo
    {
        public string BossKey { get; set; }        // Klucz bossa z gry (np. "BLUE_ASSISTANT")
        public string ModKey { get; set; }         // Nazwa karty w UI (np. "Plasmified_Zango")
        public int DeathsCount { get; set; }       // Liczba śmierci
        public System.DateTime FirstDeath { get; set; }  // Pierwsza śmierć
        public System.DateTime LastDeath { get; set; }   // Ostatnia śmierć
    }

    // Klasa do przechowywania danych o śmierciach
    public class DeathCounterSaveData
    {
        public int SaveSlot { get; set; }
        public int TotalDeaths { get; set; }
        public System.DateTime LastUpdated { get; set; }
        public System.Collections.Generic.Dictionary<string, BossDeathInfo> BossDeaths { get; set; } = new System.Collections.Generic.Dictionary<string, BossDeathInfo>();
    }
}
