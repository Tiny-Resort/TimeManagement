using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using Mirror;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine.InputSystem;

namespace JournalPause {

    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class JournalPause : BaseUnityPlugin {

        public const string pluginGuid = "tinyresort.dinkum.journalpause";
        public const string pluginName = "Time Management";
        public const string pluginVersion = "1.2.1";
        public static ManualLogSource StaticLogger;
        public static RealWorldTimeLight realWorld;
        public static ConfigEntry<KeyCode> pauseHotkey;
        public static ConfigEntry<KeyCode> increaseTimeSpeedHotkey;
        public static ConfigEntry<KeyCode> decreaseTimeSpeedHotkey;
        public static ConfigEntry<bool> disableKeybinds;
        public static float timeSpeed = 0.5f;
        public static bool pausedByHotkey;
        public static bool paused;
        public static Coroutine customRoutine;
        public static bool journalOpen;
        public static bool forceClearNotification;
        public static bool firstDayBeforeJournal;
        public static float timeSpeedDefault;
        public static bool inBetweenDays;
        public static bool runOnce = false;
        public static bool runOnce2 = false;
        public static NPCManager manager;
        public static List<ShopInfo> openingHours = new List<ShopInfo>();
        public static ConfigEntry<string> ignoreList;
        public static List<string> ignoreFullList;
        public static ConfigEntry<int> checkHoursBefore;

        public static bool FullVersion = true;

        private void Awake() {

            StaticLogger = Logger;

            #region Configuration

            if (FullVersion) {
                pauseHotkey = Config.Bind<KeyCode>("Keybinds", "Pause", KeyCode.F9, "Unity KeyCode used for pausing the game.");
                increaseTimeSpeedHotkey = Config.Bind<KeyCode>("Keybinds", "IncreaseTimeSpeed", KeyCode.KeypadPlus, "Unity KeyCode used for increasing the current time speed.");
                decreaseTimeSpeedHotkey = Config.Bind<KeyCode>("Keybinds", "DecreaseTimeSpeed", KeyCode.KeypadMinus, "Unity KeyCode used for decreasing the current time speed.");
                timeSpeed = Config.Bind<float>("Speed", "TimeSpeed", 0.5f, "How many minutes of in-game time should pass per second. This default is the game's default. Higher values will result is faster, shorter days. Lower values will result in longer, slower days. A value of 1 will be twice as fast as the default game speed. A value of 0.25 will be half as fast as the default game speed.").Value;
                disableKeybinds = Config.Bind<bool>("Speed", "DisableKeybinds", false, "Disables the use of the keybinds for increasing and decreasing time.");
                ignoreList = Config.Bind<string>("Shop Control", "IgnoreNPCShopNotification", " ", $"Add NPC names that you would like to ignore and separate it by comma (no space).\nHere is a list: John, Clover, Rayne, Irwin, Theodore, Melvin, Franklyn, Fletch, Milburn");
                checkHoursBefore = Config.Bind<int>("Shop Control", "X-HoursBefore", 1 , "Set the desired number of hours before closing you'd like to be warned.");
            }
            timeSpeedDefault = timeSpeed;

            #endregion

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

            MethodInfo clockTick = AccessTools.Method(typeof(RealWorldTimeLight), "clockTick");
            MethodInfo clockTickPostfix = AccessTools.Method(typeof(JournalPause), "clockTickPostfix");

            MethodInfo closeSubMenu = AccessTools.Method(typeof(MenuButtonsTop), "closeSubMenu");
            MethodInfo closeSubMenuPatch = AccessTools.Method(typeof(JournalPause), "closeSubMenuPatch");

            MethodInfo openSubMenu = AccessTools.Method(typeof(MenuButtonsTop), "openSubMenu");
            MethodInfo openSubMenuPatch = AccessTools.Method(typeof(JournalPause), "openSubMenuPatch");

            MethodInfo confirmQuitButton = AccessTools.Method(typeof(MenuButtonsTop), "ConfirmQuitButton");
            MethodInfo confirmQuitButtonPrefix = AccessTools.Method(typeof(JournalPause), "confirmQuitButtonPrefix");

            MethodInfo startNewDay = AccessTools.Method(typeof(RealWorldTimeLight), "startNewDay");
            MethodInfo startNewDayPostfix = AccessTools.Method(typeof(JournalPause), "startNewDayPostfix");

            if (FullVersion) {
                MethodInfo makeTopNotification = AccessTools.Method(typeof(NotificationManager), "makeTopNotification");
                MethodInfo makeTopNotificationPrefix = AccessTools.Method(typeof(JournalPause), "makeTopNotificationPrefix");
                harmony.Patch(makeTopNotification, new HarmonyMethod(makeTopNotificationPrefix));
            }

            harmony.Patch(update, new HarmonyMethod(updatePatch));
            harmony.Patch(closeSubMenu, new HarmonyMethod(closeSubMenuPatch));
            harmony.Patch(openSubMenu, new HarmonyMethod(openSubMenuPatch));
            harmony.Patch(confirmQuitButton, new HarmonyMethod(confirmQuitButtonPrefix));
            harmony.Patch(clockTick, new HarmonyMethod(clockTickPostfix));
            harmony.Patch(startNewDay, new HarmonyMethod(startNewDayPostfix));

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
                    forceClearNotification = true;
                    NotificationManager.manage.makeTopNotification("Time Management", "Now PAUSED");
                }

                // If unpausing by hotkey, unpause the game unless the journal is open
                else {
                    if (paused && !journalOpen) {
                        //StaticLogger.LogInfo("unpauseTime() --- Paused and !JournalOpen");
                        unpauseTime();
                    }
                    forceClearNotification = true;
                    if (journalOpen) { NotificationManager.manage.makeTopNotification("Time Management", "Now UNPAUSED (Still paused while in the journal)"); }
                    else { NotificationManager.manage.makeTopNotification("Time Management", "Now UNPAUSED"); }
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

            if (!runOnce) {
                checkShopHours();
                runOnce = true;
            }

            return true;
        }

        public static void checkShopHours() {
            for (var i = 0; i < NPCManager.manage.NPCDetails.Length; i++) {
                string[] tmpString = NPCManager.manage.NPCDetails[i].mySchedual.getOpeningHours().Split(' ');
                if (tmpString.Length >= 3) {
                    int morningHours;
                    int.TryParse(Regex.Match(tmpString[1], @"\d+").Value, out morningHours);
                    int nightHours;
                    int.TryParse(Regex.Match(tmpString[3], @"\d+").Value, out nightHours);
                    ShopInfo tempInfo = new ShopInfo();
                    tempInfo.owner = NPCManager.manage.NPCDetails[i].NPCName;
                    tempInfo.morningHours = morningHours;
                    tempInfo.closingHours = nightHours + 12;
                    tempInfo.isVillager = NPCManager.manage.npcStatus[i].checkIfHasMovedIn();
                    tempInfo.details = NPCManager.manage.NPCDetails[i];
                    Debug.Log($"{tempInfo.owner} | {tempInfo.morningHours} | {tempInfo.closingHours} | {tempInfo.isVillager}");
                    openingHours.Add(tempInfo);
                }
            }
        }

        [HarmonyPostfix]
        public static void clockTickPostfix() { runCheckIfOpenOrCloseSoon(realWorld, openingHours); }

        [HarmonyPostfix]
        public static void startNewDayPostfix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
            }
        }

        public static bool checkIfCurrentDayOff(ShopInfo details) {
            if (details.details.mySchedual.dayOff[WorldManager.manageWorld.day - 1]) return true;
            return false;
        }

        public static bool checkNextDayOff(ShopInfo details) {
            if (WorldManager.manageWorld.day >= 7) {
                if (details.details.mySchedual.dayOff[0]) return true;
            }
            else {
                if (details.details.mySchedual.dayOff[WorldManager.manageWorld.day]) return true;

            }

            return false;
        }

        public static bool checkIfOpenInCurrentHour(ShopInfo details) {
            if (RealWorldTimeLight.time.currentHour != 0
             && RealWorldTimeLight.time.currentHour != 24
             && !details.details.mySchedual.dayOff[WorldManager.manageWorld.day - 1]
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour] != NPCSchedual.Locations.Wonder
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour] != NPCSchedual.Locations.Exit) { return true; }
            return false;
        }

        public static bool checkIfClosedInNextHour(ShopInfo details) {
            if (RealWorldTimeLight.time.currentHour != 0
             && RealWorldTimeLight.time.currentHour != 24
             && !details.details.mySchedual.dayOff[WorldManager.manageWorld.day - 1]
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour + checkHoursBefore.Value] != NPCSchedual.Locations.Wonder
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour + checkHoursBefore.Value] != NPCSchedual.Locations.Exit) { return true; }
            return false;
        }

        public static void runCheckIfOpenOrCloseSoon(RealWorldTimeLight time, List<ShopInfo> list) {
            for (int i = 0; i < list.Count; i++) {
                if (!ignoreFullList.Contains(list[i].details.NPCName.ToLower())) {
                    if (checkIfOpenInCurrentHour(list[i]) && time.currentMinute == 10 && list[i].isVillager && !list[i].checkedOpening && time.currentHour == 7) { list[i].checkedOpening = true; }
                    if (checkIfOpenInCurrentHour(list[i]) && time.currentMinute == 00 && list[i].isVillager && !list[i].checkedOpening) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner}'s store just opened (at {list[i].morningHours}AM).");
                        list[i].checkedOpening = true;
                    }
                    if (time.currentHour < 23) {
                        if (!checkIfCurrentDayOff(list[i]) && !checkIfClosedInNextHour(list[i]) && time.currentHour > 12 && time.currentMinute == 0 && list[i].isVillager && !list[i].checkedClosing) {
                            if (checkNextDayOff(list[i])) { NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in {checkHoursBefore.Value} hour(s) (at {list[i].closingHours - 12}PM) and will be closed tomorrow."); }
                            else
                                NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in an hour(at {list[i].closingHours - 12}PM).");
                            list[i].checkedClosing = true;
                        }
                    }
                }
            }
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
                StaticLogger.LogInfo("Test if I got in here");
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
                forceClearNotification = true;
                NotificationManager.manage.makeTopNotification("Time Management", text);

            }

        }

        // Forcibly clears the top notification so that it can be replaced immediately
        [HarmonyPrefix]
        public static bool makeTopNotificationPrefix(NotificationManager __instance) {

            if (forceClearNotification) {
                forceClearNotification = false;

                var toNotify = (List<string>)AccessTools.Field(typeof(NotificationManager), "toNotify").GetValue(__instance);
                var subTextNot = (List<string>)AccessTools.Field(typeof(NotificationManager), "subTextNot").GetValue(__instance);
                var soundToPlay = (List<ASound>)AccessTools.Field(typeof(NotificationManager), "soundToPlay").GetValue(__instance);
                var topNotificationRunning = AccessTools.Field(typeof(NotificationManager), "topNotificationRunning");
                var topNotificationRunningRoutine = topNotificationRunning.GetValue(__instance);

                // Clears existing notifications in the queue
                toNotify.Clear();
                subTextNot.Clear();
                soundToPlay.Clear();

                // Stops the current coroutine from continuing
                if (topNotificationRunningRoutine != null) {
                    __instance.StopCoroutine((Coroutine)topNotificationRunningRoutine);
                    topNotificationRunning.SetValue(__instance, null);
                }

                // Resets all animations related to the notificatin bubble appearing/disappearing
                __instance.StopCoroutine("closeWithMask");
                __instance.topNotification.StopAllCoroutines();
                var Anim = __instance.topNotification.GetComponent<WindowAnimator>();
                Anim.StopAllCoroutines();
                Anim.maskChild.enabled = false;
                Anim.contents.gameObject.SetActive(false);
                Anim.gameObject.SetActive(false);

                return true;

            }
            else
                return true;
        }

        // Stops the time routine from running when the journal is opened
        public static bool openSubMenuPatch() {
            //StaticLogger.LogInfo("Open Sub Menu Patch");
            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            journalOpen = true;
            pauseTime();

            return true;
        }

        // Stops the flow of time
        public static void pauseTime() {
            //StaticLogger.LogInfo("Inside Pause Time");
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
            // StaticLogger.LogInfo("Unpause Time");
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
            //StaticLogger.LogInfo("closeSubMenuPatch");
            // Keeps it from running on clients in a multiplayer world due to clockRoutine not running
            // on clients and preventing the player from opening their milestone manager (ESC key)
            if (!realWorld.isServer) return true;

            // Fixes issue with opening menu quickly after reviving from death
            if (!NetworkMapSharer.share.nextDayIsReady) return true;

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

    public class ShopInfo {
        public NPCDetails details;
        public string owner;
        public int morningHours;
        public int closingHours;
        public bool isVillager;
        public bool checkedOpening = false;
        public bool checkedClosing = false;
    }

}
