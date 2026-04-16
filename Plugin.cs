using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CarnivalMod
{
    [BepInPlugin("com.spark3mods.carnival", "Carnival Mod", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Carnival Mod loaded! Patching PlayerBhysics.Start...");
            new Harmony("com.spark3mods.carnival").PatchAll();
            Log.LogInfo("Harmony patch applied.");
        }
    }

    [HarmonyPatch(typeof(PlayerBhysics), "Start")]
    public class PlayerBhysics_Start_Patch
    {
        static void Postfix(PlayerBhysics __instance)
        {
            Plugin.Log.LogInfo("PlayerBhysics.Start fired — attempting companion spawn.");

            if (Object.FindObjectOfType<CompanionController>() != null)
            {
                Plugin.Log.LogInfo("Companion already exists, skipping spawn.");
                return;
            }

            GameObject companion = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            companion.name = "Companion";
            companion.transform.position = __instance.transform.position + Vector3.up * 3f;

            companion.AddComponent<Rigidbody>();
            companion.AddComponent<CompanionController>();

            // Carnival Score tracker — separate GameObject, lives for the duration of the stage
            GameObject scoreTracker = new GameObject("CarnivalScoreTracker");
            scoreTracker.AddComponent<CarnivalScoreTracker>();

            Plugin.Log.LogInfo("Companion spawned successfully!");
        }
    }
}
