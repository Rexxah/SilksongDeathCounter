using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace SilksongDeathCounter
{
    [HarmonyPatch]
    internal static class PauseMenuPatcher
    {
        private static GameObject deathCounterButton = null;
        private static PauseMenuButton deathCounterButtonInstance = null;
        private static GameObject pauseMenuContainer = null;

        // Patch dla UIManager.ShowMenu - wywoływane gdy pokazuje się pauseMenuScreen
        [HarmonyPatch(typeof(global::UIManager), "ShowMenu")]
        [HarmonyPostfix]
        private static IEnumerator OnMenuShown(IEnumerator __result, global::UIManager __instance, MenuScreen menu)
        {
            // Najpierw wykonaj oryginalną coroutine
            while (__result.MoveNext())
            {
                yield return __result.Current;
            }

            // Sprawdź czy to jest pause menu i dodaj przycisk
            if (menu != null && menu.gameObject.name == "NewPauseMenuScreen")
            {
                DeathCounterPlugin.LogManager.Info("NewPauseMenuScreen detected - adding Death Counter button");
                AddDeathCounterButton(__instance);
            }
        }

        private static void AddDeathCounterButton(global::UIManager uiManager)
        {
            try
            {
                DeathCounterPlugin.LogManager.Info("AddDeathCounterButton START!");

                // Użyj __instance (instancja UIManager przekazana przez Harmony)
                Transform uiCanvas = uiManager.UICanvas?.transform;
                if (uiCanvas == null)
                {
                    DeathCounterPlugin.LogManager.Error("UICanvas not found!");
                    return;
                }
                DeathCounterPlugin.LogManager.Info($"UICanvas found: {uiCanvas.name}");

                // Znajdź NewPauseMenuScreen
                Transform pauseMenuScreen = uiCanvas.Find("NewPauseMenuScreen");
                if (pauseMenuScreen == null)
                {
                    DeathCounterPlugin.LogManager.Error("NewPauseMenuScreen not found!");
                    return;
                }
                DeathCounterPlugin.LogManager.Info($"NewPauseMenuScreen found: {pauseMenuScreen.name}");

                // Znajdź Container/Controls
                Transform container = pauseMenuScreen.Find("Container");
                if (container == null)
                {
                    DeathCounterPlugin.LogManager.Error("Container not found!");
                    return;
                }

                // Zapamiętaj Container do ukrywania
                pauseMenuContainer = container.gameObject;
                DeathCounterPlugin.LogManager.Info($"Container found and saved: {container.name}");

                Transform controls = container.Find("Controls");
                if (controls == null)
                {
                    DeathCounterPlugin.LogManager.Error("Controls not found!");
                    return;
                }
                DeathCounterPlugin.LogManager.Info($"Controls found with {controls.childCount} children");

                // Sprawdź czy przycisk już istnieje
                Transform existingButton = controls.Find("DeathCounterButton");
                if (existingButton != null)
                {
                    DeathCounterPlugin.LogManager.Info("Death Counter button already exists!");
                    return;
                }

                // Znajdź OptionsButton do zduplikowania
                Transform optionsButton = controls.Find("OptionsButton");
                if (optionsButton == null)
                {
                    DeathCounterPlugin.LogManager.Error("OptionsButton not found!");
                    return;
                }
                DeathCounterPlugin.LogManager.Info($"OptionsButton found: {optionsButton.name}");

                // Duplikuj OptionsButton 1:1
                DeathCounterPlugin.LogManager.Info("Duplicating OptionsButton...");
                deathCounterButton = Object.Instantiate(optionsButton.gameObject, controls);
                deathCounterButton.name = "DeathCounterButton";
                DeathCounterPlugin.LogManager.Info($"DeathCounterButton created: {deathCounterButton.name}");

                // Znajdź "Menu Button Text" i zmień tekst
                Transform menuButtonText = deathCounterButton.transform.Find("Menu Button Text");
                if (menuButtonText != null)
                {
                    // Usuń AutoLocalizeTextUI
                    var autoLocalize = menuButtonText.GetComponent<AutoLocalizeTextUI>();
                    if (autoLocalize != null)
                    {
                        Object.Destroy(autoLocalize);
                        DeathCounterPlugin.LogManager.Info("AutoLocalizeTextUI removed");
                    }

                    // Zmień tekst
                    Text buttonText = menuButtonText.GetComponent<Text>();
                    if (buttonText != null)
                    {
                        buttonText.text = "Boss Death Counter";
                        DeathCounterPlugin.LogManager.Info("Button text changed to 'Boss Death Counter'");
                    }
                }

                // Przenieś przycisk pod OptionsButton
                deathCounterButton.transform.SetSiblingIndex(optionsButton.GetSiblingIndex() + 1);
                DeathCounterPlugin.LogManager.Info($"Sibling index set to: {deathCounterButton.transform.GetSiblingIndex()}");

                // Zapamiętaj PauseMenuButton component - będziemy patchować jego OnPointerClick
                var pauseMenuButtonComp = deathCounterButton.GetComponent<PauseMenuButton>();
                if (pauseMenuButtonComp != null)
                {
                    DeathCounterPlugin.LogManager.Info("PauseMenuButton component found - saving reference");
                    deathCounterButtonInstance = pauseMenuButtonComp;
                }
                else
                {
                    DeathCounterPlugin.LogManager.Error("PauseMenuButton component not found!");
                }

                DeathCounterPlugin.LogManager.Info("Death Counter button added successfully!");
            }
            catch (System.Exception e)
            {
                DeathCounterPlugin.LogManager.Error($"Error adding Death Counter button: {e}");
                DeathCounterPlugin.LogManager.Error($"Stack trace: {e.StackTrace}");
            }
        }

        // Public metoda do pokazywania Container z powrotem
        public static void ShowPauseMenuContainer()
        {
            if (pauseMenuContainer != null)
            {
                pauseMenuContainer.SetActive(true);
                DeathCounterPlugin.LogManager.Info("Pause menu Container shown");
            }
        }

        // Harmony Prefix dla PauseMenuButton.OnPointerClick - zmienia funkcję naszego przycisku
        [HarmonyPatch(typeof(PauseMenuButton), "OnPointerClick")]
        [HarmonyPrefix]
        private static bool OnPauseMenuButtonClick(PauseMenuButton __instance)
        {
            // Sprawdź czy to nasz przycisk Death Counter
            if (__instance == deathCounterButtonInstance)
            {
                DeathCounterPlugin.LogManager.Info("Death Counter button clicked!");

                // Ukryj Container pause menu
                if (pauseMenuContainer != null)
                {
                    pauseMenuContainer.SetActive(false);
                    DeathCounterPlugin.LogManager.Info("Pause menu Container hidden");
                }

                // Pokaż UI z bossami
                BossStatsUI.ShowUI();

                // Zwróć false aby zablokować oryginalną funkcję (nie otwieraj Options menu)
                return false;
            }

            // Dla innych przycisków pozwól działać normalnie
            return true;
        }
    }
}
