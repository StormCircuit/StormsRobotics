using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using Assets.Scripts.Objects;
using UnityEngine;
//aimee tweaker by Storm and Copilot, written with help from the modding community


namespace StormsAimeeBuff
{
    [HarmonyPatch(typeof(RobotMining))]
    [HarmonyPatch("Awake")]
    public class RobotMiningPatch
    {
        [HarmonyPostfix]
        public static void aimeeBuffPatch(RobotMining __instance)
        {
            aimeeBuffModPlugin.Log.LogInfo("Patching aimee values.");

            //patch non static publics
            __instance.MaxSpeed = aimeeBuffModPlugin.aimeeSpeed.Value;
            __instance.TargetMotorPower = aimeeBuffModPlugin.aimeeTorque.Value;
            __instance.WeatherDamageScale = aimeeBuffModPlugin.aimeeStormDamage.Value;

            //patch static publics
            RobotMining.RepairSpeedScale = aimeeBuffModPlugin.aimeeRepairSpeed.Value;
            
            //patch private static fields with reflection
            var type = typeof(RobotMining);
            type.GetField("maxMiningDepth", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, aimeeBuffModPlugin.aimeeMiningDepth.Value);
            type.GetField("_isStuckCheckAmount", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, aimeeBuffModPlugin.aimeeStuckTimer.Value);  
            type.GetField("_isStuckMovementAmount", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, aimeeBuffModPlugin.aimeeStuckSpeed.Value);
            type.GetField("MinableSearchArea", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, aimeeBuffModPlugin.aimeeSearchArea.Value);
        }
    }

    [HarmonyPatch(typeof(DynamicThing))]
    public static class DynamicThingPatcher
    {
        [HarmonyPatch("GetStormWindVector")]
        [HarmonyPostfix]
        public static Vector3 GetStormWindVector_Postfix(Vector3 __result, DynamicThing __instance)
        {
            if(__instance is RobotMining){
                return __result * aimeeBuffModPlugin.aimeeWindScale.Value;
            }
            return __result;
        }
    }
}