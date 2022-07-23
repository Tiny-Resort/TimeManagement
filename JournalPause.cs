using System;
using System.Collections;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;


namespace JournalPause {
    
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class JournalPause : BaseUnityPlugin {
        
        public const string pluginGuid = "tinyresort.dinkum.journalpause";
        public const string pluginName = "Journal Pause";
        public const string pluginVersion = "1.0.5";
        public static RealWorldTimeLight realWorld;

        private void Awake() {

            #region Logging
            ManualLogSource logger = Logger;

            bool flag;
            BepInExInfoLogInterpolatedStringHandler handler = new BepInExInfoLogInterpolatedStringHandler(18, 1, out flag);
            if (flag) { handler.AppendLiteral("Plugin " + pluginGuid + " (v" + pluginVersion + ") loaded!"); }
            logger.LogInfo(handler);
            #endregion

            #region Patching
            Harmony harmony = new Harmony(pluginGuid);

            MethodInfo update = AccessTools.Method(typeof(RealWorldTimeLight), "Update");
            MethodInfo updatePatch = AccessTools.Method(typeof(JournalPause), "updatePatch");

            MethodInfo closeSubMenu = AccessTools.Method(typeof(MenuButtonsTop), "closeSubMenu");
            MethodInfo closeSubMenuPatch = AccessTools.Method(typeof(JournalPause), "closeSubMenuPatch");
            MethodInfo openSubMenu = AccessTools.Method(typeof(MenuButtonsTop), "openSubMenu");
            MethodInfo openSubMenuPatch = AccessTools.Method(typeof(JournalPause), "openSubMenuPatch");

            harmony.Patch(update, new HarmonyMethod(updatePatch));
            harmony.Patch(closeSubMenu, new HarmonyMethod(closeSubMenuPatch));
            harmony.Patch(openSubMenu, new HarmonyMethod(openSubMenuPatch));
            #endregion

        }

        // Gets a reference to the time manager class so that we can reference and set the clock routine easily
        private static bool updatePatch(RealWorldTimeLight __instance) {
            realWorld = __instance;
            return true;
        }

        // Same as normal time routine, but allows us to start and stop on demand
        public static IEnumerator newRunClock(RealWorldTimeLight __instance) {
            
            float currentSpeed = (float)AccessTools.Field(typeof(RealWorldTimeLight), "currentSpeed").GetValue(__instance);

            while (true)
            {
                __instance.clockTick();
                __instance.clockTickEvent.Invoke();
                yield return new WaitForSeconds(currentSpeed);
                if (__instance.currentHour != 0)
                {
                    __instance.currentMinute++;
                }
                if (__instance.currentMinute >= 60)
                {
                    __instance.currentMinute = 0;
                    if (__instance.currentHour != 0)
                    {
                        __instance.NetworkcurrentHour = __instance.currentHour + 1;
                    }
                }
                if (__instance.currentMinute == 0 || __instance.currentMinute == 15 || __instance.currentMinute == 30 || __instance.currentMinute == 45)
                {
                    __instance.taskChecker.Invoke();
                }
            }
        }
        
        // Stops the time routine from running when the journal is opened
        public static bool openSubMenuPatch() {

            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            var clockRoutine = (Coroutine)AccessTools.Field(typeof(RealWorldTimeLight), "clockRoutine").GetValue(realWorld);
            realWorld.StopCoroutine(clockRoutine);
            return true;

        }
        
        // Restarts the time routine when the journal is closed
        // Runs a custom time routine just because its harder to get the original routine to run again
        public static bool closeSubMenuPatch(MenuButtonsTop __instance) {

            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            // Fixes issue with opening menu quickly after reviving from death
            if (!NetworkMapSharer.share.nextDayIsReady) return true;

            // Makes sure the entire journal is being closed, not just a sub-submenu
            if (MilestoneManager.manage.milestoneClaimWindowOpen || 
                PediaManager.manage.entryFullScreenShown || 
                (PhotoManager.manage.photoTabOpen && PhotoManager.manage.blownUpWindow.activeInHierarchy)) {
                return true;
            }
            
            // Restarts time (Failsafe: Makes sure its not already restarted somehow)
            var clockRoutineInfo = (FieldInfo) AccessTools.Field(typeof(RealWorldTimeLight), "clockRoutine");
            var clockRoutine = (Coroutine) clockRoutineInfo.GetValue(realWorld);
            if (clockRoutine != null) realWorld.StopCoroutine(clockRoutine);
            clockRoutineInfo.SetValue(realWorld, realWorld.StartCoroutine(newRunClock(realWorld)));
            return true;

        }

    }

}
