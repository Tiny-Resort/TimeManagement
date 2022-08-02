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

    public class ShopInfo {
        public NPCDetails details;
        public string owner;
        public int morningHours;
        public int closingHours;
        public bool isVillager;
        public bool checkedOpening = false;
        public bool checkedClosing = false;
        public bool checkedIfDayOff = false;
    }
    public class StoreHours {
        
        public static List<ShopInfo> openingHours = new List<ShopInfo>();

        [HarmonyPrefix]
        public static void startPrefix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
                openingHours[i].checkedIfDayOff = false;
                JournalPause.runOnce = false;
            }
        }
        [HarmonyPostfix]
        public static void clockTickPostfix() { runCheckIfOpenOrCloseSoon(JournalPause.realWorld, openingHours); }

        [HarmonyPostfix]
        public static void startNewDayPostfix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
                openingHours[i].checkedIfDayOff = false;
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
                    openingHours.Add(tempInfo);
                }
            }
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
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour + JournalPause.checkHoursBefore.Value] != NPCSchedual.Locations.Wonder
             && details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour + JournalPause.checkHoursBefore.Value] != NPCSchedual.Locations.Exit) { return true; }
            return false;
        }

        public static void runCheckIfOpenOrCloseSoon(RealWorldTimeLight time, List<ShopInfo> list) {
            for (int i = 0; i < list.Count; i++) {
                if (!JournalPause.ignoreFullList.Contains(list[i].details.NPCName.ToLower())) {
                    if (checkIfOpenInCurrentHour(list[i]) && time.currentMinute > 00 && list[i].isVillager && !list[i].checkedOpening && time.currentHour == 7) { list[i].checkedOpening = true; }
                    if (checkIfOpenInCurrentHour(list[i]) && time.currentMinute == 00 && list[i].isVillager && !list[i].checkedOpening && time.currentHour >= 8 && time.currentHour < 13) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner}'s store just opened (at {list[i].morningHours}AM).");
                        list[i].checkedOpening = true;
                    }
                    if (time.currentHour < 23) {
                        if (!checkIfCurrentDayOff(list[i]) && !checkIfClosedInNextHour(list[i]) && time.currentHour > 12 && time.currentMinute == 0 && list[i].isVillager && !list[i].checkedClosing) {
                            if (checkNextDayOff(list[i])) { NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in {JournalPause.checkHoursBefore.Value} hour(s) (at {list[i].closingHours - 12}PM) and will be closed tomorrow."); }
                            else
                                NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in an hour(at {list[i].closingHours - 12}PM).");
                            list[i].checkedClosing = true;
                        }
                    }
                    if (checkIfCurrentDayOff(list[i]) && !list[i].checkedIfDayOff && list[i].isVillager && time.currentHour >= 8) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner} is off today!");
                        list[i].checkedIfDayOff = true;
                    }
                }
            }
        }
    }
}
