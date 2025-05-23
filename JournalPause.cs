﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TinyResort {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class JournalPause : BaseUnityPlugin {

        public static TRPlugin Plugin;
        public const string pluginGuid = "tinyresort.dinkum.journalpause";
        public const string pluginName = "Time Management";
        public const string pluginVersion = "1.3.2";
        
        public static RealWorldTimeLight realWorld;
        public static NPCManager manager;
        
        public static float timeSpeed = 0.5f;
        public static bool pausedByHotkey;
        public static bool paused;
        public static Coroutine customRoutine;
        public static bool journalOpen;
        
        public static bool firstDayBeforeJournal;
        public static float timeSpeedDefault;
        public static bool inBetweenDays;
        public static bool runOnce;
        
        public static ConfigEntry<KeyCode> pauseHotkey;
        public static ConfigEntry<KeyCode> increaseTimeSpeedHotkey;
        public static ConfigEntry<KeyCode> decreaseTimeSpeedHotkey;
        public static ConfigEntry<bool> disableKeybinds;
        public static ConfigEntry<string> ignoreList;
        public static ConfigEntry<int> checkHoursBefore;
        public static ConfigEntry<bool> useShopNames;

        
        public static List<string> ignoreFullList;
        public static bool FullVersion = true; 

        private void Awake() {

            Plugin = TRTools.Initialize(this, 10);

            #region Configuration

            if (FullVersion) {
                pauseHotkey = Config.Bind<KeyCode>("Keybinds", "Pause", KeyCode.F9, "Unity KeyCode used for pausing the game.");
                increaseTimeSpeedHotkey = Config.Bind<KeyCode>("Keybinds", "IncreaseTimeSpeed", KeyCode.KeypadPlus, "Unity KeyCode used for increasing the current time speed.");
                decreaseTimeSpeedHotkey = Config.Bind<KeyCode>("Keybinds", "DecreaseTimeSpeed", KeyCode.KeypadMinus, "Unity KeyCode used for decreasing the current time speed.");
                timeSpeed = Config.Bind<float>("Speed", "TimeSpeed", 0.5f, "How many minutes of in-game time should pass per second. This default is the game's default. Higher values will result is faster, shorter days. Lower values will result in longer, slower days. A value of 1 will be twice as fast as the default game speed. A value of 0.25 will be half as fast as the default game speed.").Value;
                disableKeybinds = Config.Bind<bool>("Speed", "DisableKeybinds", false, "Disables the use of the keybinds for increasing and decreasing time.");
            }
            ignoreList = Config.Bind<string>("Shop Control", "IgnoreNPCShopNotification", " ", $"Add NPC names that you would like to ignore and separate it by comma (no space).\nHere is a list: John, Clover, Rayne, Irwin, Theodore, Melvin, Franklyn, Fletch, Milburn");
            checkHoursBefore = Config.Bind<int>("Shop Control", "X-HoursBefore", 1, "Set a value of 1 or 2 for how many hours before closing you'd like to notified.");
            useShopNames = Config.Bind<bool>("Shop Control", "UseShopNames", true, $"Set to true to use the shop's name and false to use the NPC name in the notification.");

            timeSpeedDefault = timeSpeed;

            if (checkHoursBefore.Value > 2) checkHoursBefore.Value = 2;
            Debug.Log("CHECK HOURS BEFORE: " + checkHoursBefore.Value);
            #endregion

            #region Patching
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "Update", typeof(JournalPause), "updatePatch");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "clockTick", typeof(StoreHours), "clockTickPostfix");
            Plugin.QuickPatch(typeof(MenuButtonsTop), "closeSubMenu", typeof(JournalPause), "closeSubMenuPatch");
            Plugin.QuickPatch(typeof(MenuButtonsTop), "openSubMenu", typeof(JournalPause), "openSubMenuPatch");
            Plugin.QuickPatch(typeof(MenuButtonsTop), "ConfirmQuitButton", typeof(JournalPause), "confirmQuitButtonPrefix");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "startNewDay", typeof(StoreHours), "startNewDayPostfix");
            Plugin.QuickPatch(typeof(RealWorldTimeLight), "Start", typeof(StoreHours), "startPrefix");
            #endregion

            ignoreFullList = ignoreList.Value.ToLower().Split(',').ToList();
        }

  
        // Gets a reference to the time manager class so that we can reference and set the clock routine easily
        private static bool updatePatch(RealWorldTimeLight __instance) {

            realWorld = __instance;

            inBetweenDays = CharLevelManager.manage.levelUpWindowOpen;
            firstDayBeforeJournal = !TownManager.manage.journalUnlocked;

            // Clients in a multiplayer world should not be able to stop time at all
            if (!realWorld.isServer) return true;

            timeSpeedInputs();

            // Pauses the game using a hotkey instead of the journal
            if (FullVersion && !firstDayBeforeJournal && Input.GetKeyDown(pauseHotkey.Value)) {

                pausedByHotkey = !pausedByHotkey;

                // If pausing by hotkey now, make sure the game is paused
                if (pausedByHotkey) {
                    if (!paused) { pauseTime(); }
                    TRTools.TopNotification("Time Management", "Now PAUSED");
                }

                // If unpausing by hotkey, unpause the game unless the journal is open
                else {
                    if (paused && !journalOpen) {
                        //StaticLogger.LogInfo("unpauseTime() --- Paused and !JournalOpen");
                        unpauseTime();
                    }
                    if (journalOpen) { TRTools.TopNotification("Time Management", "Now UNPAUSED (Still paused while in the journal)"); }
                    else { TRTools.TopNotification("Time Management", "Now UNPAUSED"); }
                }
            }

            // Ensures time is stopped if it's supposed to be paused and started if its not
            var clockRoutine = (Coroutine)AccessTools.Field(typeof(RealWorldTimeLight), "clockRoutine").GetValue(realWorld);
            if (paused && clockRoutine != null && !firstDayBeforeJournal) {
                //StaticLogger.LogInfo("Pause Time Checks 1");
                pauseTime();
            }
            else if (!paused && clockRoutine == null && !firstDayBeforeJournal) { unpauseTime(); }

            // If the game is not paused but our custom coroutine isn't running, then time speed isn't correct possibly
            // So, ensure our routine is the one that's playing
            if (!paused && customRoutine != clockRoutine && !firstDayBeforeJournal) {
                //StaticLogger.LogInfo("Pause Time Checks 2");
                pauseTime();
                unpauseTime();
            }
            
            if (!StoreHours.dataInitialized) {
                StoreHours.InitializeListData();
            }
            return true;
        }
        // Same as normal time routine, but allows us to start and stop on demand
        public static IEnumerator newRunClock(RealWorldTimeLight __instance) {
            //StaticLogger.LogInfo("New Run Clock");
            while (true) {

                __instance.clockTick();
                __instance.clockTickEvent.Invoke();

                // Combines our setting and the game's speed in a way that ensures in-game time manipulation still works but relative to our speed
                float currentSpeed = (float)AccessTools.Field(typeof(RealWorldTimeLight), "currentSpeed").GetValue(__instance);
                float minuteDelay = (1f / timeSpeed) * (currentSpeed / 2f);
                yield return new WaitForSeconds(minuteDelay);

                // Counts up the minutes
                if (__instance.currentHour != 0) { __instance.currentMinute++; }

                // If it's a new hour, alert clients
                if (__instance.currentMinute >= 60) {
                    __instance.currentMinute = 0;
                    if (__instance.currentHour != 0) { __instance.NetworkcurrentHour = __instance.currentHour + 1; }
                }

                // Run any necessary tasks
                if (__instance.currentMinute == 0 || __instance.currentMinute == 15 || __instance.currentMinute == 30 || __instance.currentMinute == 45) { __instance.taskChecker.Invoke(); }
            }
        } 

        // Increasing or decreasing the speed of time with hotkeys
        public static void timeSpeedInputs() {

            if (!FullVersion || firstDayBeforeJournal || disableKeybinds.Value) return;

            var increaseSpeed = Input.GetKeyDown(increaseTimeSpeedHotkey.Value);
            var decreaseSpeed = Input.GetKeyDown(decreaseTimeSpeedHotkey.Value);

            // Increasing the speed of time, keeping it below 20 minutes per second
            if (increaseSpeed || decreaseSpeed) { 
                var text = "Time speed ";

                // Decreasing Speed
                if (increaseSpeed) {

                    if (timeSpeed >= 60f) {
                        timeSpeed = 60f;
                        text += "at maximum!";
                    } 

                    else {

                        float increment;
                        if (timeSpeed >= 10) { increment = 5; }
                        else if (timeSpeed >= 4) { increment = 1; }
                        else if (timeSpeed >= 1) { increment = 0.5f; }
                        else if (timeSpeed >= 0.5f) { increment = 0.1f; }
                        else if (timeSpeed >= 0.2f) { increment = 0.05f; }
                        else { increment = 0.01f; }

                        timeSpeed = (Mathf.Round(timeSpeed / increment) * increment) + increment;
                        text += "increased to " + timeSpeed + " min/sec";

                    }

                }

                // Increasing Speed
                else {

                    if (timeSpeed <= 0.05f) {
                        timeSpeed = 0.05f;
                        text += "at minimum!";
                    }

                    else {

                        float increment;
                        if (timeSpeed >= 11) { increment = 2; }
                        else if (timeSpeed >= 4.5f) { increment = 1; }
                        else if (timeSpeed >= 1.25f) { increment = 0.5f; }
                        else if (timeSpeed >= 0.55f) { increment = 0.1f; }
                        else if (timeSpeed >= 0.21f) { increment = 0.05f; }
                        else { increment = 0.01f; }

                        timeSpeed = (Mathf.Round(timeSpeed / increment) * increment) - increment;
                        text += "decreased to " + timeSpeed + " min/sec";

                    }

                }

                // Clamps time speed to keep it from going wild
                timeSpeed = Mathf.Clamp(timeSpeed, 0.05f, 60f);
                TRTools.TopNotification("Time Management", text);

            }

        }

        // Stops the time routine from running when the journal is opened
        public static bool openSubMenuPatch() {
            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            journalOpen = true;
            pauseTime();

            return true;
        }

        // Stops the flow of time
        public static void pauseTime() {
            if (firstDayBeforeJournal || inBetweenDays) return;
            stopRoutines();
            paused = true;
        }

        public static void stopRoutines() {
            //StaticLogger.LogInfo("Stop Routines");
            if (realWorld == null) return;
            var clockRoutineInfo = AccessTools.Field(typeof(RealWorldTimeLight), "clockRoutine");
            var clockRoutine = (Coroutine)clockRoutineInfo.GetValue(realWorld);
            if (clockRoutine != null) {
                realWorld.StopCoroutine(clockRoutine);
                clockRoutineInfo.SetValue(realWorld, null);
            }

            if (customRoutine != null) {
                realWorld.StopCoroutine(customRoutine);
                customRoutine = null;
            }
            realWorld.StopCoroutine("runClock");

        }

        // Restarts time (Failsafe: Makes sure its not already restarted somehow)
        public static void unpauseTime() {
            if (firstDayBeforeJournal || inBetweenDays) return;
            var clockRoutineInfo = AccessTools.Field(typeof(RealWorldTimeLight), "clockRoutine");
            stopRoutines();
            customRoutine = realWorld.StartCoroutine(newRunClock(realWorld));
            clockRoutineInfo.SetValue(realWorld, customRoutine);
            paused = false;
        }

        // Restarts the time routine when the journal is closed
        // Runs a custom time routine just because its harder to get the original routine to run again
        public static bool closeSubMenuPatch(MenuButtonsTop __instance) {
            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            // Fixes issue with opening menu quickly after reviving from death
            if (!NetworkMapSharer.Instance.nextDayIsReady) return true;

            // Makes sure the entire journal is being closed, not just a sub-submenu
            if (MilestoneManager.manage.milestoneClaimWindowOpen ||
                PediaManager.manage.entryFullScreenShown ||
                (PhotoManager.manage.photoTabOpen && PhotoManager.manage.blownUpWindow.activeInHierarchy)) { return true; }

            journalOpen = false;

            // Only unpause time if the pause hotkey isn't toggled on
            if (!pausedByHotkey) unpauseTime();

            return true;

        }

        [HarmonyPostfix]
        public static void confirmQuitButtonPrefix() {
            stopRoutines();
            paused = false;
            pausedByHotkey = false;
            journalOpen = false;
            timeSpeed = timeSpeedDefault;

            //StaticLogger.LogInfo("Inside ConfirmQuitButtonPostfix");
        }

    }


}
