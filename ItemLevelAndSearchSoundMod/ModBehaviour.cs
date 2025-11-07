using Duckov.UI;
using Duckov.UI.Animations;
using Duckov.Utilities;
using FMOD;
using HarmonyLib;
using ItemStatsSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ItemLevelAndSearchSoundMod
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string Id = "Spuddy.ItemLevelAndSearchSoundMod";
        public const string Low = "UI/hover";
        public const string Medium = "UI/sceneloader_click";
        public const string High = "UI/game_start";

        public static float DefaultSearchAnimationValue;
        public static bool DisableModSearchTime;

        public static readonly int[] ForceWhiteLevelTypeID = new int[] { 308, 309, 368, 394, 890 };

        public static Dictionary<ItemValueLevel, Sound> ItemValueLevelSound = new Dictionary<ItemValueLevel, Sound>();
        public static string ErrorMessage = "";

        public static ChannelGroup SfxGroup
        {
            get
            {
                if (!sfxGroup.hasHandle())
                {
                    RESULT result = FMODUnity.RuntimeManager.GetBus("bus:/Master/SFX").getChannelGroup(out sfxGroup);
                    if (result != RESULT.OK)
                        UnityEngine.Debug.LogError("ItemLevelAndSearchSoundMod FMOD failed to get sfx group: " + result);
                }
                return sfxGroup;
            }
        }

        public static Color White, Green, Blue, Purple, Orange, LightRed, Red;
        private static Sound searchingSound;
        private static ChannelGroup sfxGroup;
        private Harmony harmony;
        private Channel searchingChannel;

        private void OnEnable()
        {
            UnityEngine.Debug.Log("ItemLevelAndSearchSoundMod OnEnable");

            DisableModSearchTime = File.Exists("ItemLevelAndSearchSoundMod/DisableModSearchTime.txt");

            var magnifier = GameplayDataSettings.UIPrefabs.ItemDisplay.transform.Find("InspectioningIndicator/Magnifier");
            if (magnifier != null)
            {
                var revolver = magnifier.GetComponent<Revolver>();
                if (revolver != null)
                    DefaultSearchAnimationValue = revolver.rPM;
            }

            LoadSounds();
            LoadColors();

            ItemUtilities.OnItemSentToPlayerInventory += OnItemSentToPlayerInventory;
            InteractableLootbox.OnStartLoot += OnStartLoot;
            InteractableLootbox.OnStopLoot += OnStopLoot;

            harmony = new Harmony(Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void LoadSounds()
        {
            try
            {
                string searchingSoundPath = "ItemLevelAndSearchSoundMod/Searching.mp3";
                if (File.Exists(searchingSoundPath))
                {
                    var soundResult = FMODUnity.RuntimeManager.CoreSystem.createSound(searchingSoundPath, MODE.LOOP_NORMAL, out searchingSound);
                    if (soundResult != RESULT.OK)
                        ErrorMessage += "FMOD failed to create searching sound: " + soundResult + "\n";
                }

                foreach (ItemValueLevel item in Enum.GetValues(typeof(ItemValueLevel)))
                {
                    string path = $"ItemLevelAndSearchSoundMod/{(int)item}.mp3";
                    if (File.Exists(path))
                    {
                        var soundResult = FMODUnity.RuntimeManager.CoreSystem.createSound(path, MODE.LOOP_OFF, out Sound sound);
                        if (soundResult != RESULT.OK)
                        {
                            ErrorMessage += "FMOD failed to create sound: " + soundResult + "\n";
                            continue;
                        }
                        ItemValueLevelSound.Add(item, sound);
                    }
                }
            }
            catch (Exception e)
            {
                ErrorMessage += e.ToString() + "\n";
            }
        }

        private void LoadColors()
        {
            string configFile = "ItemLevelAndSearchSoundMod/ColorConfig.txt";

            var defaults = new Dictionary<string, string>
            {
                {"White", "00000000"},
                {"Green", "1A5A3EAA"},
                {"Blue", "39537CCC"},
                {"Purple", "3F3B75AA"},
                {"Orange", "67523BCC"},
                {"LightRed", "7C434ACC"},
                {"Red", "8F2531CC"}
            };

            if (!File.Exists(configFile))
            {
                Directory.CreateDirectory("ItemLevelAndSearchSoundMod");
                using (var writer = new StreamWriter(configFile))
                {
                    foreach (var kv in defaults)
                        writer.WriteLine($"{kv.Key}={kv.Value}");
                }
            }

            var lines = File.ReadAllLines(configFile);
            foreach (var kv in defaults)
            {
                string value = kv.Value;
                foreach (var line in lines)
                {
                    if (line.StartsWith(kv.Key + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        value = line.Split('=')[1];
                        break;
                    }
                }
                if (ColorUtility.TryParseHtmlString("#" + value, out Color c))
                {
                    switch (kv.Key)
                    {
                        case "White": White = c; break;
                        case "Green": Green = c; break;
                        case "Blue": Blue = c; break;
                        case "Purple": Purple = c; break;
                        case "Orange": Orange = c; break;
                        case "LightRed": LightRed = c; break;
                        case "Red": Red = c; break;
                    }
                }
            }
        }

        private void OnStartLoot(InteractableLootbox lootbox)
        {
            if (!lootbox.Inventory.NeedInspection || lootbox.Inventory.Content.All(item => item == null || item.Inspected))
                return;

            if (!searchingSound.hasHandle()) return;

            RESULT result = FMODUnity.RuntimeManager.CoreSystem.playSound(searchingSound, SfxGroup, false, out searchingChannel);
            if (result != RESULT.OK)
                ErrorMessage += "FMOD failed to play searching sound: " + result + "\n";
        }

        private void OnStopLoot(InteractableLootbox lootbox)
        {
            if (searchingChannel.hasHandle())
            {
                searchingChannel.stop();
                searchingChannel = default;
            }
        }

        private void OnItemSentToPlayerInventory(Item item)
        {
            item.onInspectionStateChanged -= PatchItemDisplaySetup.OnInspectionStateChanged;
        }

        private void OnGUI()
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                var style = new GUIStyle(GUI.skin.label);
                style.normal.textColor = Color.red;
                GUI.Label(new Rect(10, 10, Screen.width - 10, Screen.height - 10), "ItemLevelAndSearchSoundMod Error: \n" + ErrorMessage, style);
            }
        }

        private void OnDisable()
        {
            ItemUtilities.OnItemSentToPlayerInventory -= OnItemSentToPlayerInventory;
            InteractableLootbox.OnStartLoot -= OnStartLoot;
            InteractableLootbox.OnStopLoot -= OnStopLoot;

            harmony.UnpatchAll(Id);

            if (searchingSound.hasHandle())
                searchingSound.release();

            foreach (var sound in ItemValueLevelSound)
                sound.Value.release();

            ItemValueLevelSound.Clear();
        }
    }
}
