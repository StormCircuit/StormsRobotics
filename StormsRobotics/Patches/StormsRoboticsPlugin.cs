using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using Assets.Scripts.Objects;
//aimee tweaker by Storm and Copilot, written with help from the modding community and AI

namespace StormsRobotics
{
    [BepInPlugin("StormsRoboticsPlugin", "StormsRoboticsPlugin", "0.0.0.1")]
    public class StormsRoboticsPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<float> sampleConfig;
        public static float sampleConfig =>
        sampleConfig?.Value ?? 1.0f;

        void Awake()
        {
            Log = Logger;


            //apply patching
            var harmony = new Harmony("StormsRoboticsPlugin");
            harmony.PatchAll();
        }
    }
}