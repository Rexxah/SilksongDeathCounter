using BepInEx.Configuration;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace SilksongDeathCounter
{
    internal static class ConfigManager
    {
        // TotalDeaths removed - now using file-based system
        public static ConfigEntry<KeyboardShortcut> ResetKey;
        public static ConfigEntry<KeyboardShortcut> BossStatsKey;
        public static ConfigEntry<KeyboardShortcut> ToggleCounterKey;
        public static ConfigEntry<int> FontSize;
        public static ConfigEntry<int> XPostion;
        public static ConfigEntry<int> YPostion;
        public static ConfigEntry<bool> CounterVisible;

        public static void Init(ConfigFile config)
        {
            // TotalDeaths config removed - migrated to DeathCounterData files
            ResetKey = config.Bind("Hotkeys", "ResetRunDeaths", new KeyboardShortcut(KeyCode.F10), "Reset run counter.");
            BossStatsKey = config.Bind("Hotkeys", "BossStats", new KeyboardShortcut(KeyCode.None), "Show/Hide boss statistics (Optional - also available in pause menu).");
            ToggleCounterKey = config.Bind("Hotkeys", "ToggleCounter", new KeyboardShortcut(KeyCode.F8), "Toggle death counter visibility.");
            XPostion = config.Bind("UI", "XPosition", 12, "Horizontal offset from screen edge.");
            YPostion = config.Bind("UI", "YPosition", 12, "Vertical offset from screen edge.");
            FontSize = config.Bind("UI", "FontSize", 16, "Font size of death counter text.");
            CounterVisible = config.Bind("UI", "CounterVisible", true, "Whether death counter is visible.");
        }
    }
}
