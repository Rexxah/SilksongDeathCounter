using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SilksongDeathCounter
{
    [BepInPlugin("com.peacestudio.silksongdeathcounter", "Silksong Death Counter", "5.0.0")]
    public class DeathCounterPlugin : BaseUnityPlugin
    {
        internal static DeathCounterPlugin Instance;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;

            // IMPORTANT: Initialize LogManager FIRST - other classes depend on it
            LogManager.Init(Logger);

            ConfigManager.Init(Config);
            UIManager.CreateOverlayCanvasAndText();
            DeathTracker.Init();
            BossStatsUI.Init();

            _harmony = new Harmony("com.peacestudio.silksongdeathcounter");
            Patcher.ApplyPatches(_harmony);

            Logger.LogInfo("Silksong Death Counter loaded.");
        }

        internal static class LogManager
        {
            private static ManualLogSource logger;

            public static void Init(ManualLogSource log) => logger = log;

            public static void Info(string msg) => logger.LogInfo(msg);
            public static void Warning(string msg) => logger.LogWarning(msg);
            public static void Error(string msg) => logger.LogError(msg);
        }

        private void Update()
        {
            DeathTracker.Update();
            UIManager.UpdateFontIfNeeded();
            BossStatsUI.Update();
        }

        private void OnDestroy()
        {
            SaveManager.SaveOnExit();
            BossStatsUI.Cleanup();
            _harmony?.UnpatchSelf();
        }
    }
}
