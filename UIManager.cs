using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace SilksongDeathCounter
{
    internal static class UIManager
    {
        private static Canvas createdCanvas;
        private static Text deathText;
        private static bool fontApplied = false;
        private static string lastText = null;

        public static void CreateOverlayCanvasAndText()
        {
            GameObject canvasObj = new GameObject("DeathCounterCanvas");
            createdCanvas = canvasObj.AddComponent<Canvas>();
            createdCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            createdCanvas.overrideSorting = true;
            createdCanvas.sortingOrder = 32767;

            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            Object.DontDestroyOnLoad(canvasObj);

            GameObject textObj = new GameObject("DeathCounterText");
            textObj.transform.SetParent(canvasObj.transform, false);

            deathText = textObj.AddComponent<Text>();
            deathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            deathText.fontSize = ConfigManager.FontSize.Value;
            deathText.color = Color.white;
            deathText.raycastTarget = false;

            RectTransform rect = deathText.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.sizeDelta = new Vector2(400, 64);

            // Użyj offsetów z configu
            ApplyPositionToRect(rect, ConfigManager.XPostion.Value, ConfigManager.YPostion.Value);
            SetVisible(false); // Zaczynamy ukryty - SaveManager.cs ustawi visibility gdy save się załaduje
            UpdateDeathText(0, 0);
        }

        public static void ApplyPositionToRect(RectTransform rect, int offsetX, int offsetY)
        {
            // Domyślne ustawienie jak topleft
            deathText.alignment = TextAnchor.UpperLeft;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(offsetX, -offsetY); // Y ujemny, by zachować zachowanie jak w topleft
        }

        public static void UpdateDeathText(int total, int run)
        {
            if (deathText == null) return;
            string newText = $"Deaths: {total} (Run: {run})";
            if (!string.Equals(newText, lastText))
            {
                deathText.text = newText;
                lastText = newText;
            }
        }

        public static void UpdateFontIfNeeded()
        {
            if (fontApplied || deathText == null) return;
            Font gameFont = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "TrajanPro-Bold");
            if (gameFont != null)
            {
                deathText.font = gameFont;
                fontApplied = true;
                DeathCounterPlugin.LogManager.Info("DeathCounter: Trajan Pro Bold font applied!");
            }
        }

        public static void SetVisible(bool visible)
        {
            if (deathText != null)
            {
                // Tylko pokazuj gdy zarówno parametr visible = true I konfiguracja CounterVisible = true
                bool shouldShow = visible && ConfigManager.CounterVisible.Value;
                deathText.gameObject.SetActive(shouldShow);
            }
        }

        public static void UpdateCounterVisibility()
        {
            if (deathText != null)
            {
                // Użyj obecnego stanu aktywności jako podstawy, ale zastosuj nowe ustawienie visibility
                bool currentlyIntendedToBeVisible = deathText.gameObject.activeSelf;
                if (currentlyIntendedToBeVisible || ConfigManager.CounterVisible.Value)
                {
                    // Jeśli licznik jest aktualnie pokazywany LUB user właśnie go włączył
                    SetVisible(true);
                }
                else
                {
                    // Jeśli licznik jest ukryty i user go wyłączył
                    deathText.gameObject.SetActive(false);
                }
            }
        }

        public static void AddDeathCounterToSaveSlot(Transform slotTransform, int deathCount)
        {
            if (slotTransform == null) return;

            var bottom = slotTransform.Find("ActiveSaveSlot/Bottom Section");
            if (bottom == null) return;

            if (bottom.Find("DeathCounterText") != null) return;

            GameObject deathObj = new GameObject("DeathCounterText");
            deathObj.transform.SetParent(bottom, false);

            // Przesuń na 3 pozycję w hierarchii (indeks 2, bo indeksowanie od 0)
            deathObj.transform.SetSiblingIndex(3);

            var deathText = deathObj.AddComponent<Text>();
            deathText.text = $"Deaths: {deathCount}";

            // Spróbuj znaleźć czcionkę TrajanPro-Regular, jeśli nie to domyślna
            Font trajanFont = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == "TrajanPro-Bold");
            if (trajanFont != null)
                deathText.font = trajanFont;
            else
                deathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            deathText.fontSize = 35;
            deathText.color = Color.white;
            deathText.alignment = TextAnchor.MiddleRight;

            RectTransform rect = deathObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200, 30);
        }

        public static void UpdateDeathCountersOnSaveSlots(int[] deathCounts)
        {
            // Nie przesuwamy tablicy - Array[0] = Save 1, Array[1] = Save 2, etc.
            // Poprzedni kod przesuwał indeksy niepotrzebnie

            var slotsParent = GameObject.Find("_UIManager/UICanvas/SaveProfileScreen/Content/SaveSlots");
            if (slotsParent == null) return;

            string[] slotNames = { "SlotOne", "SlotTwo", "SlotThree", "SlotFour" };
            for (int i = 0; i < slotNames.Length; i++)
            {
                var slot = slotsParent.transform.Find(slotNames[i]);
                if (slot != null)
                {
                    int deaths = (i < deathCounts.Length) ? deathCounts[i] : 0;
                    AddDeathCounterToSaveSlot(slot, deaths);
                }
            }
        }
    }
}
