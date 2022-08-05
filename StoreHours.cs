using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace JournalPause {

    public class ShopInfo {
        public NPCDetails details;
        public string owner;
        public int morningHours;
        public int closingHours;
        public bool isVillager;
        public bool checkedOpening;
        public bool checkedClosing;
        public bool checkedIfDayOff;
    }

    public class StoreHours {

        public static List<ShopInfo> openingHours = new List<ShopInfo>();

        #region Refresh Variables

        // Updates the variables on Start of RealWorldTimeLight
        // This fixes issues when exiting to main menu and re-entering games)
        [HarmonyPrefix]
        public static void startPrefix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
                openingHours[i].checkedIfDayOff = false;
                JournalPause.runOnce = false;
            }
        }

        // Updates the variables on start of new day
        [HarmonyPostfix]
        public static void startNewDayPostfix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
                openingHours[i].checkedIfDayOff = false;
            }
        }

        #endregion

        #region Run Every Clock Tick

        // Run the function on every tick of the clock
        [HarmonyPostfix]
        public static void clockTickPostfix() { runCheckIfOpenOrCloseSoon(JournalPause.realWorld, openingHours); }

        #endregion

        #region Check Store Hours and Days Off
        
        // Check which days the NPC has off
        public static bool checkDaysOff(ShopInfo details, bool checkTomorrow) {

            int currentDay = checkTomorrow == false ? WorldManager.manageWorld.day - 1 : WorldManager.manageWorld.day;
            int nextDay = currentDay >= 7 ? 0 : currentDay;

            if (checkTomorrow) {
                if (details.details.mySchedual.dayOff[nextDay]) { return true; }
            }
            else {
                if (details.details.mySchedual.dayOff[currentDay]) { return true; }
            }
            return false;
        }

        // Collects the Shop's hours in string format (8AM - 6PM) and parses it for just the two digits. 
        // Also stores the owner's name, if they are currently a villager, and the NPC Details. 
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

        // Checks the stores hours compared to the NPCs schedules
        public static bool checkStoreHours(ShopInfo details, bool checkClosing) {
            int currentHour = !checkClosing ? RealWorldTimeLight.time.currentHour : RealWorldTimeLight.time.currentHour + JournalPause.checkHoursBefore.Value;
            Debug.Log("Current Hour For Closing: " + currentHour);
            if (checkClosing && currentHour > details.details.mySchedual.dailySchedual.Length - 1) { return false; }
            bool isDayOff = details.details.mySchedual.dayOff[WorldManager.manageWorld.day - 1];
            bool isNotWonder = details.details.mySchedual.dailySchedual[currentHour] != NPCSchedual.Locations.Wonder;
            bool isNotExit = details.details.mySchedual.dailySchedual[currentHour] != NPCSchedual.Locations.Exit;
            if (currentHour != 0 && currentHour != 24 && !isDayOff && isNotWonder && isNotExit) { return true; }
            return false;
        }

        #endregion

        #region Run Check and Send Notification

        // Runs the functions and prints out the notification message
        public static void runCheckIfOpenOrCloseSoon(RealWorldTimeLight time, List<ShopInfo> list) {
            for (int i = 0; i < list.Count; i++) {
                // Set to ignore a list of NPCs as requested in the Config file
                if (!JournalPause.ignoreFullList.Contains(list[i].details.NPCName.ToLower()) && list[i].isVillager && time.currentMinute == 00) {
                    
                    if (!list[i].checkedOpening) {
                        // If the store is open before 8AM, ignore the notification since they wil always be open on day start
                        if (checkStoreHours(list[i], false) && time.currentHour == 7) {
                            list[i].checkedOpening = true;
                        }
                        // Check the opening store hours for the remaining NPCs
                        if (checkStoreHours(list[i], false) && time.currentHour >= 8 && time.currentHour < 13) {
                            NotificationManager.manage.createChatNotification($"{list[i].owner}'s store just opened (at {list[i].morningHours}AM).");
                            list[i].checkedOpening = true;
                        }
                    }

                    // Check the closing stores hours; Takes in a config option for how many hours prior you want to see the notification appear.
                    // It will also give you a heads up if they are going to be closed the following day. 
                    if (time.currentHour < 23) {
                        if (time.currentHour > 12 && !list[i].checkedClosing && !checkDaysOff(list[i], false) && !checkStoreHours(list[i], true)) {
                            if (checkDaysOff(list[i], true)) { NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in {JournalPause.checkHoursBefore.Value} hour(s) (at {list[i].closingHours - 12}PM) and will be closed tomorrow."); }
                            else { NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in {JournalPause.checkHoursBefore.Value} hour(s) (at {list[i].closingHours - 12}PM)."); }
                            list[i].checkedClosing = true;
                        }
                    }
                    // Checks if the NPC has the day off and gives you a notification (as a hint to hang out with them)
                    if (!list[i].checkedIfDayOff && time.currentHour >= 8 && checkDaysOff(list[i], false)) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner} is off today!");
                        list[i].checkedIfDayOff = true;
                    }
                }
            }
        }

        #endregion

    }

}
