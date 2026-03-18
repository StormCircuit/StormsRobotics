using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using Assets.Scripts.Objects;
//aimee tweaker by Storm and Copilot, written with help from the modding community and AI

namespace StormsAimeeBuff
{
    [BepInPlugin("StormsAimeeTweaker", "Storms Aimee Tweaker", "2.2.0.0")]
    public class aimeeBuffModPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<float> aimeeSpeed;
        public static float AimeeSpeed =>
        aimeeSpeed?.Value ?? 1.0f;
        public static ConfigEntry<float> aimeeTorque;
        public static float AimeeTorque =>
        aimeeTorque?.Value ?? 1.0f;
        public static ConfigEntry<float> aimeeStormDamage;
        public static float AimeeStormDamage =>
        aimeeStormDamage?.Value ?? 1.0f;
        public static ConfigEntry<float> aimeeWindScale;
        public static float AimeeWindScale =>
        aimeeWindScale?.Value ?? 1.0f;        
        public static ConfigEntry<int> aimeeMiningDepth;
        public static int AimeeMiningDepth =>
        aimeeMiningDepth?.Value ?? 32; 
        public static ConfigEntry<float> aimeeStuckTimer;
        public static float AimeeStuckTimer =>
        aimeeStuckTimer?.Value ?? 1.0f;  
        public static ConfigEntry<float> aimeeStuckSpeed;
        public static float AimeeStuckSpeed =>
        aimeeStuckSpeed?.Value ?? 1.0f; 
        public static ConfigEntry<int> aimeeSearchArea;
        public static float AimeeSearchArea =>
        aimeeSearchArea?.Value ?? 1.0f; 
        public static ConfigEntry<float> aimeeRepairSpeed;
        public static float AimeeRepairSpeed =>
        aimeeRepairSpeed?.Value ?? 1.0f; 

        void Awake()
        {
            Log = Logger;

            aimeeSpeed = Config.Bind("General", "aimeeSpeed", 1.3f, new ConfigDescription("In m/s." + "\nVanilla is 1.3"));
            aimeeTorque = Config.Bind("General", "aimeeTorque", 0.01f, new ConfigDescription("The torque of Aimee's motor." + "\nUnknown units." + "\nVanilla is 0.01"));
            aimeeStormDamage = Config.Bind("General", "aimeeStormDamage", 1.0f, new ConfigDescription("Amount of storm damage Aimee takes." + "\nIn % where 1 = 100%. " + "\nVanilla is 1"));
            aimeeWindScale = Config.Bind("General", "aimeeWindScale", 1.0f, new ConfigDescription("Amount of wind force Aimee takes." + "\nIn % where 1 = 100%. " + "\nVanilla is 1"));
            aimeeRepairSpeed = Config.Bind("General", "aimeeRepairSpeed", 0.4f, new ConfigDescription("In % where 1 = 100%. " + "\nVanilla is 0.4"));
            aimeeMiningDepth = Config.Bind("General", "aimeeMiningDepth", 3, new ConfigDescription("I think in meters." + "\nVanilla is 3"));
            aimeeStuckTimer = Config.Bind("General", "aimeeStuckTimer", 60f, new ConfigDescription("In seconds." + "\nVanilla is 60"));
            aimeeStuckSpeed = Config.Bind("General", "aimeeStuckSpeed", 0.1f, new ConfigDescription("I think in m/s." + "\nVanilla is 0.1"));
            aimeeSearchArea = Config.Bind("General", "aimeeSearchArea", 16, new ConfigDescription("The range aimee will look for ores in when in mining mode. I think in meters." + "\nVanilla is 16"));

            //apply patching
            var harmony = new Harmony("StormsAimeeBuff");
            harmony.PatchAll();
        }
    }
}