using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SilksongDeathCounter
{
    internal static class BossStatsUI
    {
        private static GameObject bossStatsUIObject;
        private static bool isUIVisible = false;
        private static Transform contentParent;
        private static GameObject cardTemplate;
        private static AssetBundle uiBundle;
        private static float previousTimeScale = 1f; // Zapamiętaj poprzedni timeScale

        public static void Init()
        {
            try
            {
                LoadUIBundle();
                CreateBossStatsUI();
                DeathCounterPlugin.LogManager.Info("BossStatsUI initialized successfully.");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error initializing BossStatsUI: {e}");
            }
        }

        private static void LoadUIBundle()
        {
            try
            {
                // Ścieżka do bundle'a w folderze moda
                string bundlePath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(DeathCounterPlugin).Assembly.Location),
                    "ui_bundle"
                );

                if (System.IO.File.Exists(bundlePath))
                {
                    uiBundle = AssetBundle.LoadFromFile(bundlePath);
                    if (uiBundle == null)
                    {
                        DeathCounterPlugin.LogManager.Error("Failed to load ui_bundle.");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Info("UI Bundle loaded successfully.");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Error($"UI Bundle not found at: {bundlePath}");
                }
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading UI bundle: {e}");
            }
        }

        private static void CreateBossStatsUI()
        {
            try
            {
                if (uiBundle == null)
                {
                    DeathCounterPlugin.LogManager.Error("Cannot create UI - bundle is null.");
                    return;
                }

                // Listuj wszystkie assety w bundle
                string[] assetNames = uiBundle.GetAllAssetNames();
                DeathCounterPlugin.LogManager.Info($"Assets in bundle: {string.Join(", ", assetNames)}");

                // Ładujemy prefab z bundle'a
                GameObject uiPrefab = uiBundle.LoadAsset<GameObject>("CanvasDeathCounterStatsMod");
                if (uiPrefab == null)
                {
                    DeathCounterPlugin.LogManager.Error("Cannot find CanvasDeathCounterStatsMod in bundle.");
                    DeathCounterPlugin.LogManager.Info("Available GameObjects in bundle:");
                    GameObject[] allPrefabs = uiBundle.LoadAllAssets<GameObject>();
                    foreach (var prefab in allPrefabs)
                    {
                        DeathCounterPlugin.LogManager.Info($"  - {prefab.name}");
                    }
                    return;
                }
                else
                {
                    DeathCounterPlugin.LogManager.Info($"Found UI prefab: {uiPrefab.name}");
                }

                // Tworzymy instancję UI
                bossStatsUIObject = Object.Instantiate(uiPrefab);
                Object.DontDestroyOnLoad(bossStatsUIObject);

                // Znajdź Canvas i ustaw go jako overlay
                Canvas canvas = bossStatsUIObject.GetComponent<Canvas>();
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 1000; // Wysoki priorytet
                }

                // Znajdź CardPanelDCB (gdzie będą karty bossów)
                contentParent = FindChildByName(bossStatsUIObject.transform, "CardPanelDCB");
                if (contentParent == null)
                {
                    DeathCounterPlugin.LogManager.Error("Cannot find CardPanelDCB object in UI.");
                    DeathCounterPlugin.LogManager.Info("Listing all UI hierarchy:");
                    ListChildren(bossStatsUIObject.transform, "Root");
                    return;
                }
                else
                {
                    DeathCounterPlugin.LogManager.Info($"Found CardPanelDCB object at: {contentParent.name}");
                }

                // Znajdź template karty (CardDCB)
                Transform cardTransform = FindChildByName(contentParent, "CardDCB");
                if (cardTransform != null)
                {
                    cardTemplate = cardTransform.gameObject;
                    DeathCounterPlugin.LogManager.Info($"Found CardDCB template: {cardTemplate.name} (Active: {cardTemplate.activeSelf})");
                    // Ukryj template - nie powinien być widoczny w UI
                    cardTemplate.SetActive(false);
                }
                else
                {
                    DeathCounterPlugin.LogManager.Error("Cannot find CardDCB template.");
                    DeathCounterPlugin.LogManager.Info("Listing CardPanelDCB children:");
                    ListChildren(contentParent, "CardPanelDCB");

                    // Spróbuj też szukać w całej hierarchii
                    DeathCounterPlugin.LogManager.Info("Searching for CardDCB in entire UI hierarchy:");
                    Transform globalCardSearch = FindChildByName(bossStatsUIObject.transform, "CardDCB");
                    if (globalCardSearch != null)
                    {
                        DeathCounterPlugin.LogManager.Info($"Found CardDCB globally at: {globalCardSearch.name} under parent: {globalCardSearch.parent?.name}");
                        cardTemplate = globalCardSearch.gameObject;
                    }
                }

                // Ukryj UI na start
                bossStatsUIObject.SetActive(false);
                DeathCounterPlugin.LogManager.Info("Boss Stats UI created successfully.");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error creating boss stats UI: {e}");
            }
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name == name)
                return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                Transform result = FindChildByName(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private static void ListChildren(Transform parent, string parentName)
        {
            DeathCounterPlugin.LogManager.Info($"Children of {parentName}:");
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                DeathCounterPlugin.LogManager.Info($"  [{i}] {child.name} (Active: {child.gameObject.activeSelf})");

                // Lista dzieci pierwszego poziomu
                if (child.childCount > 0)
                {
                    for (int j = 0; j < child.childCount; j++)
                    {
                        Transform grandChild = child.GetChild(j);
                        DeathCounterPlugin.LogManager.Info($"    [{j}] {grandChild.name}");
                    }
                }
            }
        }

        public static void Update()
        {
            // Opcjonalny klawisz skrótu (jeśli użytkownik go ustawi w config)
            if (ConfigManager.BossStatsKey.Value.IsDown())
            {
                ToggleUI();
            }

            // ESC zamyka UI jeśli jest otwarte
            if (isUIVisible && Input.GetKeyDown(KeyCode.Escape))
            {
                HideUI();
            }
        }

        public static void ToggleUI()
        {
            if (bossStatsUIObject == null)
            {
                DeathCounterPlugin.LogManager.Error("Boss Stats UI not initialized.");
                return;
            }

            isUIVisible = !isUIVisible;
            bossStatsUIObject.SetActive(isUIVisible);

            if (isUIVisible)
            {
                ShowUI();
            }
            else
            {
                HideUI();
            }
        }

        public static void ShowUI()
        {
            if (bossStatsUIObject == null)
            {
                DeathCounterPlugin.LogManager.Error("Boss Stats UI not initialized.");
                return;
            }

            isUIVisible = true;
            bossStatsUIObject.SetActive(true);

            // Zapauzuj grę
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            RefreshBossStats();
            DeathCounterPlugin.LogManager.Info("Boss Stats UI shown - Game paused.");
        }

        public static void HideUI()
        {
            if (bossStatsUIObject == null)
            {
                return;
            }

            isUIVisible = false;
            bossStatsUIObject.SetActive(false);

            // Pokaż z powrotem pause menu Container
            PauseMenuPatcher.ShowPauseMenuContainer();

            // Wznów grę
            Time.timeScale = previousTimeScale;

            DeathCounterPlugin.LogManager.Info("Boss Stats UI hidden - Game resumed.");
        }

        public static void RefreshBossStats()
        {
            try
            {
                if (contentParent == null)
                {
                    DeathCounterPlugin.LogManager.Error("UI components not ready for refresh - CardPanelDCB is null.");
                    return;
                }

                // Pobierz dane o bossach z pliku save
                var bossData = GetBossDeathData();

                DeathCounterPlugin.LogManager.Info($"🔄 REFRESHING UI - Found {(bossData?.Count ?? 0)} boss records in save file");

                // Iteruj po WSZYSTKICH kartach w UI, nie tylko tych z save file
                int updatedCount = 0;
                for (int i = 0; i < contentParent.childCount; i++)
                {
                    Transform cardTransform = contentParent.GetChild(i);
                    string cardName = cardTransform.name;

                    // Pomiń template CardDCB
                    if (cardName == "CardDCB")
                    {
                        continue;
                    }

                    // Sprawdź czy mamy dane dla tej karty
                    BossDeathInfo bossInfo = null;
                    if (bossData != null)
                    {
                        // Szukaj po ModKey (nazwa karty to ModKey)
                        bossInfo = bossData.Values.FirstOrDefault(b => b.ModKey == cardName);
                    }

                    // Jeśli nie ma danych, utwórz pusty wpis (0 śmierci)
                    if (bossInfo == null)
                    {
                        bossInfo = new BossDeathInfo
                        {
                            BossKey = "UNKNOWN",
                            ModKey = cardName,
                            DeathsCount = 0,
                            FirstDeath = System.DateTime.MinValue,
                            LastDeath = System.DateTime.MinValue
                        };
                    }

                    DeathCounterPlugin.LogManager.Info($"🐉 Updating card [{i}]: {cardName} -> Deaths: {bossInfo.DeathsCount}");

                    // Aktualizuj kartę
                    UpdateBossCard(cardName, bossInfo);
                    updatedCount++;
                }

                DeathCounterPlugin.LogManager.Info($"✅ Refreshed {updatedCount} boss cards in UI");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error refreshing boss stats: {e}");
            }
        }

        private static void ClearExistingCards()
        {
            // Ta funkcja nie jest już potrzebna - nie usuwamy kart, tylko aktualizujemy
            DeathCounterPlugin.LogManager.Info("ClearExistingCards: skipped (cards are updated in place)");
        }

        // Nowa funkcja: aktualizuj istniejącą kartę w UI zamiast tworzyć nową
        private static void UpdateBossCard(string modKey, BossDeathInfo bossInfo)
        {
            try
            {
                if (contentParent == null)
                {
                    DeathCounterPlugin.LogManager.Error($"Cannot update card for {modKey} - contentParent is null!");
                    return;
                }

                // Znajdź kartę po nazwie (Mod_Key)
                Transform cardTransform = FindChildByName(contentParent, modKey);

                if (cardTransform == null)
                {
                    DeathCounterPlugin.LogManager.Warning($"⚠️ Card '{modKey}' not found in CardPanelDCB!");
                    DeathCounterPlugin.LogManager.Info("Available cards:");
                    for (int i = 0; i < contentParent.childCount; i++)
                    {
                        Transform child = contentParent.GetChild(i);
                        DeathCounterPlugin.LogManager.Info($"  [{i}] {child.name}");
                    }
                    return;
                }

                DeathCounterPlugin.LogManager.Info($"✅ Found card: {modKey}");

                // Znajdź komponent DeathCount w karcie
                Transform deathCountTransform = FindChildByName(cardTransform, "DeathCount");

                if (deathCountTransform != null)
                {
                    Text deathCountText = deathCountTransform.GetComponent<Text>();
                    if (deathCountText != null)
                    {
                        // Aktualizuj tekst w formacie "Deaths: X"
                        deathCountText.text = $"Deaths: {bossInfo.DeathsCount}";
                        DeathCounterPlugin.LogManager.Info($"✅ Updated deaths for {modKey}: Deaths: {bossInfo.DeathsCount}");
                    }
                    else
                    {
                        DeathCounterPlugin.LogManager.Error($"DeathCount component found but has no Text component!");
                    }
                }
                else
                {
                    DeathCounterPlugin.LogManager.Error($"DeathCount component not found in card {modKey}!");
                    // Lista dzieci karty dla debugowania
                    DeathCounterPlugin.LogManager.Info($"Card '{modKey}' children:");
                    for (int i = 0; i < cardTransform.childCount; i++)
                    {
                        Transform child = cardTransform.GetChild(i);
                        DeathCounterPlugin.LogManager.Info($"  [{i}] {child.name}");
                    }
                }
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error updating boss card for {modKey}: {e}");
            }
        }

        private static void CopyParentMaskSettings(Transform currentIcon, Transform templateIcon)
        {
            try
            {
                // Skopiuj ustawienia mask parent
                Transform currentMask = currentIcon.parent;
                Transform templateMask = templateIcon.parent;

                if (currentMask != null && templateMask != null)
                {
                    DeathCounterPlugin.LogManager.Info($"Copying mask settings from {templateMask.name} to {currentMask.name}");

                    // Skopiuj RectTransform mask
                    RectTransform currentMaskRect = currentMask.GetComponent<RectTransform>();
                    RectTransform templateMaskRect = templateMask.GetComponent<RectTransform>();

                    if (currentMaskRect != null && templateMaskRect != null)
                    {
                        currentMaskRect.anchorMin = templateMaskRect.anchorMin;
                        currentMaskRect.anchorMax = templateMaskRect.anchorMax;
                        currentMaskRect.anchoredPosition = templateMaskRect.anchoredPosition;
                        currentMaskRect.sizeDelta = templateMaskRect.sizeDelta;
                        currentMaskRect.pivot = templateMaskRect.pivot;
                        currentMaskRect.offsetMin = templateMaskRect.offsetMin;
                        currentMaskRect.offsetMax = templateMaskRect.offsetMax;

                        DeathCounterPlugin.LogManager.Info($"Copied mask rect - Size: {templateMaskRect.sizeDelta}, Offsets: {templateMaskRect.offsetMin}-{templateMaskRect.offsetMax}");
                    }

                    // Skopiuj Image component mask
                    Image currentMaskImage = currentMask.GetComponent<Image>();
                    Image templateMaskImage = templateMask.GetComponent<Image>();

                    if (currentMaskImage != null && templateMaskImage != null)
                    {
                        currentMaskImage.sprite = templateMaskImage.sprite;
                        currentMaskImage.color = templateMaskImage.color;
                        currentMaskImage.type = templateMaskImage.type;
                        currentMaskImage.preserveAspect = templateMaskImage.preserveAspect;
                        currentMaskImage.raycastTarget = templateMaskImage.raycastTarget;

                        DeathCounterPlugin.LogManager.Info("Copied mask image settings");
                    }

                    // Skopiuj Mask component
                    Mask currentMaskComp = currentMask.GetComponent<Mask>();
                    Mask templateMaskComp = templateMask.GetComponent<Mask>();

                    if (templateMaskComp != null)
                    {
                        if (currentMaskComp == null)
                            currentMaskComp = currentMask.gameObject.AddComponent<Mask>();

                        currentMaskComp.showMaskGraphic = templateMaskComp.showMaskGraphic;
                        DeathCounterPlugin.LogManager.Info($"Copied mask component - showMaskGraphic: {templateMaskComp.showMaskGraphic}");
                    }

                    // Skopiuj też border parent jeśli istnieje
                    Transform currentBorder = currentMask.parent;
                    Transform templateBorder = templateMask.parent;

                    if (currentBorder != null && templateBorder != null)
                    {
                        RectTransform currentBorderRect = currentBorder.GetComponent<RectTransform>();
                        RectTransform templateBorderRect = templateBorder.GetComponent<RectTransform>();

                        if (currentBorderRect != null && templateBorderRect != null)
                        {
                            currentBorderRect.sizeDelta = templateBorderRect.sizeDelta;
                            currentBorderRect.anchoredPosition = templateBorderRect.anchoredPosition;
                            DeathCounterPlugin.LogManager.Info($"Copied border size: {templateBorderRect.sizeDelta}");
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error copying parent mask settings: {e}");
            }
        }



        private static Sprite LoadBossSprite(string spriteName)
        {
            try
            {
                if (string.IsNullOrEmpty(spriteName))
                {
                    DeathCounterPlugin.LogManager.Warning("SpriteName is empty or null");
                    return null;
                }

                DeathCounterPlugin.LogManager.Info($"🔍 Loading sprite: '{spriteName}'");

                // Pobierz wszystkie sprite
                Sprite[] allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
                DeathCounterPlugin.LogManager.Info($"Searching through {allSprites.Length} loaded sprites for: '{spriteName}'");

                // TYLKO dokładne dopasowanie nazwy - tak jak w Journal
                foreach (var s in allSprites)
                {
                    if (s.name.Equals(spriteName, System.StringComparison.Ordinal)) // Dokładne dopasowanie, case-sensitive
                    {
                        bool isSmallSprite = s.rect.width <= 150 && s.rect.height <= 150;
                        string sizeType = isSmallSprite ? "ICON (small)" : "ENEMY (large)";
                        DeathCounterPlugin.LogManager.Info($"✅ Found EXACT sprite: {s.name} (Size: {s.rect.width}x{s.rect.height}) - Type: {sizeType}");
                        return s;
                    }
                }

                // Jeśli dokładne nie ma, spróbuj case-insensitive jako fallback
                foreach (var s in allSprites)
                {
                    if (s.name.Equals(spriteName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        DeathCounterPlugin.LogManager.Info($"⚠️ Found Journal sprite (case-insensitive): {s.name} (Size: {s.rect.width}x{s.rect.height})");
                        return s;
                    }
                }

                // Lepszy fallback - szukaj sprite'ów zawierających kluczowe słowa
                DeathCounterPlugin.LogManager.Warning($"❌ EXACT Journal sprite '{spriteName}' not found! Trying smart search...");

                string[] searchParts = spriteName.Split(new char[] { '_', ' ', '-' }, System.StringSplitOptions.RemoveEmptyEntries);
                Sprite bestMatch = null;
                int bestScore = 0;

                // Najpierw preferuj sprite'y z małym rozmiarem (prawdopodobnie Icon sprite'y)
                foreach (var s in allSprites)
                {
                    if (s.rect.width <= 0 || s.rect.height <= 0) continue; // Pomiń nieprawidłowe sprite'y

                    int matchScore = 0;
                    foreach (string part in searchParts)
                    {
                        if (s.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matchScore++;
                        }
                    }

                    // Preferuj sprite'y z małym rozmiarem (Icon) i dobrym dopasowaniem nazwy
                    bool isSmallSprite = s.rect.width <= 150 && s.rect.height <= 150; // Icon sprite'y są zwykle małe
                    int totalScore = matchScore * 10 + (isSmallSprite ? 5 : 0);

                    if (matchScore > 0 && totalScore > bestScore)
                    {
                        bestMatch = s;
                        bestScore = totalScore;
                        DeathCounterPlugin.LogManager.Info($"🎯 Better match found: {s.name} (Size: {s.rect.width}x{s.rect.height}, Score: {totalScore})");
                    }
                }

                if (bestMatch != null)
                {
                    DeathCounterPlugin.LogManager.Info($"✅ Using BEST MATCH sprite: {bestMatch.name} (Size: {bestMatch.rect.width}x{bestMatch.rect.height})");
                    return bestMatch;
                }

                // Debug - pokaż dostępne sprite'y dla tego bossa
                DeathCounterPlugin.LogManager.Info($"Available sprites containing parts of '{spriteName}':");
                int count = 0;
                foreach (var s in allSprites)
                {
                    bool hasAnyPart = false;
                    foreach (string part in searchParts)
                    {
                        if (s.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasAnyPart = true;
                            break;
                        }
                    }

                    if (hasAnyPart)
                    {
                        DeathCounterPlugin.LogManager.Info($"  [{count}] {s.name} (Size: {s.rect.width}x{s.rect.height})");
                        count++;
                        if (count >= 15) break;
                    }
                }

                return null;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading boss sprite {spriteName}: {e}");
                return null;
            }
        }

        private static Dictionary<string, BossDeathInfo> GetBossDeathData()
        {
            try
            {
                // Pobierz aktualny save slot
                int currentSaveSlot = GetCurrentSaveSlot();

                // Pobierz ścieżkę do danych
                string gameDataPath = GetGameDataPath();
                if (string.IsNullOrEmpty(gameDataPath))
                    return new Dictionary<string, BossDeathInfo>();

                string deathCounterDataPath = System.IO.Path.Combine(gameDataPath, "DeathCounterData");
                string saveFileName = $"DeathCounterData_Save_{currentSaveSlot}.txt";
                string saveFilePath = System.IO.Path.Combine(deathCounterDataPath, saveFileName);

                // Załaduj dane
                var saveData = LoadDeathCounterData(saveFilePath);
                var bossDeaths = saveData?.BossDeaths ?? new Dictionary<string, BossDeathInfo>();

                // Proste filtrowanie - zwróć tylko bossów z DeathsCount > 0
                var filteredBosses = bossDeaths.Where(kvp => kvp.Value.DeathsCount > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                DeathCounterPlugin.LogManager.Info($"� Loaded {bossDeaths.Count} total bosses, {filteredBosses.Count} with deaths");

                return filteredBosses;
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error getting boss death data: {e}");
                return new Dictionary<string, BossDeathInfo>();
            }
        }

        private static string TryGetDisplayNameFromJournal(string journalName)
        {
            try
            {
                if (string.IsNullOrEmpty(journalName))
                    return null;

                var enemyJournalManagerType = AccessTools.TypeByName("EnemyJournalManager");
                if (enemyJournalManagerType != null)
                {
                    var getRecordMethod = AccessTools.Method(enemyJournalManagerType, "GetRecord", new[] { typeof(string) });
                    if (getRecordMethod != null)
                    {
                        var journalRecord = getRecordMethod.Invoke(null, new object[] { journalName });
                        if (journalRecord != null)
                        {
                            var displayNameProperty = AccessTools.Property(journalRecord.GetType(), "DisplayName");
                            if (displayNameProperty != null)
                            {
                                var displayName = displayNameProperty.GetValue(journalRecord);
                                string displayNameStr = GetLocalisedStringValue(displayName);
                                if (!string.IsNullOrEmpty(displayNameStr) && !displayNameStr.Contains("key:"))
                                {
                                    return displayNameStr;
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Warning($"Error getting display name from journal for '{journalName}': {e.Message}");
            }

            return null;
        }

        private static string GetLocalisedStringValue(object localisedString)
        {
            if (localisedString == null) return null;

            try
            {
                // Najpierw spróbuj ToString()
                string toStringResult = localisedString.ToString();
                var type = localisedString.GetType();

                if (!string.IsNullOrEmpty(toStringResult) && toStringResult != type.Name)
                {
                    return toStringResult;
                }

                // Spróbuj przez pola key i sheet
                var keyField = AccessTools.Field(type, "key") ?? AccessTools.Field(type, "m_key") ?? AccessTools.Field(type, "_key");
                var sheetField = AccessTools.Field(type, "sheet") ?? AccessTools.Field(type, "m_sheet") ?? AccessTools.Field(type, "_sheet");

                if (keyField != null && sheetField != null)
                {
                    string key = (string)keyField.GetValue(localisedString);
                    string sheet = (string)sheetField.GetValue(localisedString);

                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(sheet))
                    {
                        // Próbuj przetłumaczyć
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
                    }
                }

                return toStringResult;
            }
            catch (System.Exception ex)
            {
                DeathCounterPlugin.LogManager.Info($"GetLocalisedStringValue error: {ex.Message}");
                return localisedString?.ToString();
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

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
                System.IO.File.WriteAllLines(filePath, lines);
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error saving death counter data to {filePath}: {e}");
            }
        }

        // Helper methods - copied from DeathTracker for consistency
        private static int GetCurrentSaveSlot()
        {
            try
            {
                var gameManagerType = AccessTools.TypeByName("GameManager");
                if (gameManagerType == null) return 1;

                var instanceProperty = AccessTools.Property(gameManagerType, "instance");
                if (instanceProperty == null) return 1;

                var gameManagerInstance = instanceProperty.GetValue(null);
                if (gameManagerInstance == null) return 1;

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
                        BossDeaths = new Dictionary<string, BossDeathInfo>()
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
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error loading death counter data from {filePath}: {e}");
            }
            return null;
        }

        public static void Cleanup()
        {
            try
            {
                // Przywróć normalny czas gry jeśli UI było otwarte
                if (isUIVisible)
                {
                    Time.timeScale = previousTimeScale;
                    DeathCounterPlugin.LogManager.Info("Game time restored during cleanup.");
                }

                if (bossStatsUIObject != null)
                {
                    Object.Destroy(bossStatsUIObject);
                    bossStatsUIObject = null;
                }

                if (uiBundle != null)
                {
                    uiBundle.Unload(true);
                    uiBundle = null;
                }

                isUIVisible = false;
                previousTimeScale = 1f;

                DeathCounterPlugin.LogManager.Info("BossStatsUI cleanup completed.");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error during BossStatsUI cleanup: {e}");
            }
        }
    }
}