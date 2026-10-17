using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace OreHitboxVisualizer
{
    /// <summary>Qué se considera "colisión" para pintar un trozo en rojo.</summary>
    public enum HitboxMode
    {
        /// <summary>Rojo si la caja del trozo toca algo que no sea su propio depósito (terreno, otra roca, construcciones).</summary>
        Collision,
        /// <summary>Rojo si el juego considera el trozo sostenido (misma lógica que MineRock5.UpdateSupport). Verde = se va a caer.</summary>
        Support
    }

    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class OreHitboxVisualizerPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "adrirf7.OreHitboxVisualizer";
        public const string ModName = "OreHitboxVisualizer";
        public const string ModVersion = "1.0.0";

        // Nombre del archivo en BepInEx/config.
        internal const string FilePrefix = "OreHitboxVisualizer.adrirf7";

        internal static ManualLogSource Log;
        internal static ConfigFile MainConfig;

        // General
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> PickaxeOnly;
        internal static ConfigEntry<HitboxMode> Mode;
        internal static ConfigEntry<float> HideDistance;
        internal static ConfigEntry<float> HideAfterSeconds;

        // Aspecto
        internal static ConfigEntry<Color> CollidingColor;
        internal static ConfigEntry<Color> FreeColor;
        internal static ConfigEntry<float> LineThickness;
        internal static ConfigEntry<bool> ShowFill;
        internal static ConfigEntry<float> FillOpacity;
        internal static ConfigEntry<bool> ShowThroughWalls;

        private void Awake()
        {
            Log = Logger;
            MainConfig = new ConfigFile(Path.Combine(Paths.ConfigPath, FilePrefix + ".cfg"), true, Info.Metadata);

            Enabled = MainConfig.Bind("1 - General", "Enabled", true,
                "true = hitboxes are shown after hitting an ore. Toggled in game with ToggleKey (the state is saved).");
            ToggleKey = MainConfig.Bind("1 - General", "ToggleKey", new KeyboardShortcut(KeyCode.L),
                "Key to show / hide the hitboxes.");
            PickaxeOnly = MainConfig.Bind("1 - General", "PickaxeOnly", true,
                "true = hitboxes only appear when hitting the rock with a pickaxe; false = any weapon or tool.");
            Mode = MainConfig.Bind("1 - General", "Mode", HitboxMode.Collision,
                "Collision = a piece is red when its hitbox touches anything outside its own deposit (ground, other rocks, buildings) and green otherwise. Other pieces of the same deposit are ignored.\n" +
                "Support = a piece is red when the game considers it supported and green when it will break off on the next mined piece (exact vanilla support logic).");
            HideDistance = MainConfig.Bind("1 - General", "HideDistance", 40f,
                "The hitboxes of a rock are removed when you move farther than this (metres). Hit it again to show them again.");
            HideAfterSeconds = MainConfig.Bind("1 - General", "HideAfterSeconds", 0f,
                "Seconds since the last hit after which the hitboxes are removed. 0 = never (only by distance).");

            CollidingColor = MainConfig.Bind("2 - Appearance", "CollidingColor", new Color(1f, 0.15f, 0.15f, 1f),
                "Colour of a piece whose hitbox is colliding with something.");
            FreeColor = MainConfig.Bind("2 - Appearance", "FreeColor", new Color(0.15f, 1f, 0.2f, 1f),
                "Colour of a piece whose hitbox is not colliding with anything.");
            LineThickness = MainConfig.Bind("2 - Appearance", "LineThickness", 0.03f,
                "Thickness of the hitbox edges (metres).");
            ShowFill = MainConfig.Bind("2 - Appearance", "ShowFill", true,
                "true = also draw the hitbox faces semi-transparent.");
            FillOpacity = MainConfig.Bind("2 - Appearance", "FillOpacity", 0.12f,
                "Opacity (0-1) of the hitbox faces.");
            ShowThroughWalls = MainConfig.Bind("2 - Appearance", "ShowThroughWalls", true,
                "true = hitboxes are visible through the rock and terrain (most of them are inside the rock).");

            CollidingColor.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();
            FreeColor.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();
            LineThickness.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();
            ShowFill.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();
            FillOpacity.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();
            ShowThroughWalls.SettingChanged += (s, e) => HitboxManager.OnAppearanceChanged();

            new Harmony(ModGuid).PatchAll();
            Log.LogInfo(ModName + " " + ModVersion + " loaded.");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown() && CanUseHotkeys())
            {
                Enabled.Value = !Enabled.Value;
                string text = IsSpanish()
                    ? (Enabled.Value ? "Hitbox de minerales: visible" : "Hitbox de minerales: oculta")
                    : (Enabled.Value ? "Ore hitboxes: shown" : "Ore hitboxes: hidden");
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, text);
            }

            HitboxManager.Tick();
        }

        private void OnDestroy()
        {
            HitboxManager.ClearAll();
        }

        /// <summary>Evita que la tecla actúe mientras se escribe en el chat, la consola o un cuadro de texto.</summary>
        private static bool CanUseHotkeys()
        {
            if (Player.m_localPlayer == null)
            {
                return false;
            }
            if (Chat.instance != null && Chat.instance.HasFocus())
            {
                return false;
            }
            return !Console.IsVisible() && !TextInput.IsVisible() && !Minimap.InTextInput() && !Menu.IsVisible();
        }

        private static bool IsSpanish()
        {
            string lang = Localization.instance?.GetSelectedLanguage();
            return lang == "Spanish" || lang == "Spanish_LatAm";
        }
    }
}
