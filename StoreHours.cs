using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TinyResort {
    
    public class NotificationDetails {
        public NPCDetails details;

        public string NPCName;
        public string ShopName;
        
        public int morningHours;
        public int closingHours;
        public int dayOff;
        
        public bool tomorrowOff;
        public bool isVillager;
        public bool sentOpening;
        public bool sentClosing;
        public bool sentDayOff;
        public StoreHours.Status status = StoreHours.Status.None;
    }

    public class StoreHours {

        public static List<NotificationDetails> ShopInfo = new List<NotificationDetails>();
        public static List<NotificationDetails> toNotify = new List<NotificationDetails>();
        public static bool checkIfStoreOpenRunning;
        public static bool dataInitialized;

        public static int countOpening;
        public static int countClosing;
        public static int countDaysOff;
        
        // Checks the stores hours compared to the NPCs schedules
        public static int getOpeningHours(NPCDetails currentNPC) {
            for (int i = 7; i < 24; i++) {
                bool isNotWonder = currentNPC.mySchedual.dailySchedual[i] != NPCSchedual.Locations.Wonder;
                bool isNotExit = currentNPC.mySchedual.dailySchedual[i] != NPCSchedual.Locations.Exit;
                // Return the first time this is true, which will be the opening hour for the NPC. 
                if (i != 0 && i != 24 && (isNotWonder && isNotExit)) { return i; }
            } return 0;
        }

        public static int getClosingHours(NPCDetails currentNPC) {
            // If 12pm or later and the current schedule is exit and the previous isn't wonder it should be closing time
            for (int i = 12; i < 24; i++) {
                if (currentNPC.mySchedual.dailySchedual[i] == NPCSchedual.Locations.Exit && currentNPC.mySchedual.dailySchedual[i] != NPCSchedual.Locations.Wonder) {
                    return i;
                }
            } return 0;
        }

        public static bool checkIfOffTomorrow(NPCDetails details) {
            int currentDay =  WorldManager.Instance.day;
            int nextDay = currentDay >= 7 ? 0 : currentDay;
            if (details.mySchedual.dayOff[nextDay]) { return true; }
            return false;
        }
        
        public static void InitializeListData() {
            dataInitialized = true;
            ShopInfo.Clear();
            
            for (var i = 0; i < NPCManager.manage.NPCDetails.Length; i++) {
                // Initialize Unique Info
                NotificationDetails tempInfo = new NotificationDetails();
                tempInfo.NPCName = NPCManager.manage.NPCDetails[i].NPCName;
                tempInfo.ShopName = getShopName(tempInfo.NPCName);
                tempInfo.morningHours = getOpeningHours(NPCManager.manage.NPCDetails[i]);
                tempInfo.closingHours = getClosingHours(NPCManager.manage.NPCDetails[i]);
                tempInfo.tomorrowOff = checkIfOffTomorrow(NPCManager.manage.NPCDetails[i]);
                tempInfo.isVillager = NPCManager.manage.npcStatus[i].checkIfHasMovedIn();
                tempInfo.details = NPCManager.manage.NPCDetails[i];
                for (int j = 0; j < NPCManager.manage.NPCDetails[i].mySchedual.dayOff.Length; j++) {
                    var tmpDayOff = NPCManager.manage.NPCDetails[i].mySchedual.dayOff[j];
                    if (tmpDayOff) tempInfo.dayOff = j + 1; // +1 because the array starts at 0, but the day list starts at 1.
                }
                // Initialize Default Info
                tempInfo.sentOpening = false;
                tempInfo.sentClosing = false;
                tempInfo.sentDayOff = false;
                
                ShopInfo.Add(tempInfo);
            }
        }
        
        public static string getShopName(string owner) {
            switch (owner.ToLower()) {
                case "john": return "John's Goods";
                case "franklyn": return "Franklyn's Lab";
                case "rayne": return "Rayne's Greenhouse";
                case "clover": return "Threadspace";
                case "melvin": return "Melvin Furniture";
                case "irwin": return "Irwin's Barn";
                case "theodore": return "The Museum";
                case "fletch": return "The Town Hall";
                case "milburn": return "The Bank";
            }
            return "default";
        }

        public static void checkStoreStatus(RealWorldTimeLight time, List<NotificationDetails> NPC) {
            countOpening = 0;
            countClosing = 0;
            countDaysOff = 0;
            
            // Don't let it start a new one until the current notifications are posted (this could be an issue if time was going too fast, but won't account for that)
            checkIfStoreOpenRunning = true;
            toNotify.Clear();
            var tmpCurrentDay = WorldManager.Instance.day;
            
            for (int i = 0; i < NPC.Count; i++) {

                // Check if on ignore list and villager
                if (!JournalPause.ignoreFullList.Contains(NPC[i].details.NPCName.ToLower()) && NPC[i].isVillager && NPC[i].ShopName != "default") {
                    // check if day off
                    var isDayOff = NPC[i].details.mySchedual.dayOff[tmpCurrentDay - 1];
                    if (!NPC[i].sentDayOff && isDayOff) {
                        JournalPause.Plugin.Log($"Day Off: {NPC[i].NPCName}");
                        NPC[i].sentDayOff = true;
                        NPC[i].status = Status.DayOff;
                        countDaysOff += 1;
                        toNotify.Add(NPC[i]);
                    }
                    // Check opening hours
                    if (!NPC[i].sentOpening && NPC[i].morningHours >= 8 && NPC[i].morningHours == time.currentHour && !isDayOff) {
                        JournalPause.Plugin.Log($"Opening: {NPC[i].NPCName}");
                        NPC[i].sentOpening = true;
                        NPC[i].status = Status.Opening;
                        countOpening += 1;
                        toNotify.Add(NPC[i]);
                    }
                    // check closing hours
                    if (!NPC[i].sentClosing && NPC[i].closingHours == time.currentHour + JournalPause.checkHoursBefore.Value && !isDayOff) {
                        JournalPause.Plugin.Log($"Closing: {NPC[i].NPCName}");
                        NPC[i].sentClosing = true;
                        NPC[i].status = Status.Closing;
                        countClosing += 1;
                        toNotify.Add(NPC[i]);
                    }
                }
            }
            SendNotification();
        }

        public static void SendNotification() {
            string dayOff = "";
            string opening = "";
            string closing = "";
            string offTomorrow = "";
            int countOffTomorrow = 0;

            int tmpOpening = 0;
            int tmpClosing = 0;
            int tmpDayOff = 0;
            int tmpOffTomorrow = 0;

            // Looks to see if any of the NPCs in the list have the next day off
            foreach (NotificationDetails shop in toNotify) {
                // Only notify if status is set to closing and tomorrow off
                if (shop.tomorrowOff && shop.status == Status.Closing) {
                    countOffTomorrow += 1;
                    // I am removing one from countClosing to correctly adjust the grammar
                    countClosing -= 1;
                }
            }
            
            // Prepare the string for the notification messages
            var useShopNames = JournalPause.useShopNames.Value;

            if (toNotify.Count > 0) {
                for (int i = 0; i < toNotify.Count; i++) {
                    switch (toNotify[i].status) {
                        case Status.Opening:
                            tmpOpening += 1;
                            if (countOpening == 1) opening = useShopNames ? $"{toNotify[i].ShopName}" : $"{toNotify[i].NPCName}'s";
                            if (countOpening == 2 && tmpOpening == 1) opening = useShopNames ? $"{toNotify[i].ShopName} and " : $"{toNotify[i].NPCName}'s and ";
                            if (countOpening == 2 && tmpOpening == 2) opening += useShopNames ? $"{toNotify[i].ShopName}, " : $"{toNotify[i].NPCName}'s, ";
                            if (countOpening >= 3 && countOpening != tmpOpening) opening += useShopNames ? $"{toNotify[i].ShopName}, " : $"{toNotify[i].NPCName}'s, ";
                            if (countOpening >= 3 && countOpening == tmpOpening) opening += useShopNames ? $"and {toNotify[i].ShopName}" : $"and {toNotify[i].NPCName}'s";
                            break;
                        case Status.Closing:
                            if (toNotify[i].tomorrowOff) {
                                tmpOffTomorrow += 1;
                                if (countOffTomorrow == 1) offTomorrow = $"{toNotify[i].NPCName}";
                                if (countOffTomorrow == 2 && tmpOffTomorrow == 1) offTomorrow = $"{toNotify[i].NPCName} and ";
                                if (countOffTomorrow == 2 && tmpOffTomorrow == 2) offTomorrow += $"{toNotify[i].NPCName}";
                                if (countOffTomorrow >= 3 && countOffTomorrow != tmpOffTomorrow) offTomorrow += $"{toNotify[i].NPCName}, ";
                                if (countOffTomorrow >= 3 && countOffTomorrow == tmpOffTomorrow) offTomorrow += $" and {toNotify[i].NPCName}";
                            }
                            else {
                                tmpClosing += 1;
                                if (countClosing == 1) closing = useShopNames ? $"{toNotify[i].ShopName}" : $"{toNotify[i].NPCName}'s";
                                if (countClosing == 2 && tmpClosing == 1) closing = useShopNames ? $"{toNotify[i].ShopName} and " : $"{toNotify[i].NPCName}'s and ";
                                if (countClosing == 2 && tmpClosing == 2) closing += useShopNames ? $"{toNotify[i].ShopName}" : $"{toNotify[i].NPCName}'s";
                                if (countClosing >= 3 && tmpClosing == 1) closing = useShopNames ? $"{toNotify[i].ShopName}, " : $"{toNotify[i].NPCName}'s, ";
                                if (countClosing >= 3 && tmpClosing != 1 && countClosing != tmpClosing) closing += useShopNames ? $"{toNotify[i].ShopName}, " : $"{toNotify[i].NPCName}'s, ";
                                if (countClosing >= 3 && countClosing == tmpClosing) closing += useShopNames ? $"and {toNotify[i].ShopName}" : $"and {toNotify[i].NPCName}'s";
                            }
                            break;
                        case Status.DayOff:
                            tmpDayOff += 1;
                            if (countDaysOff == 1) dayOff = $"{toNotify[i].NPCName}";
                            if (countDaysOff == 2 && tmpDayOff == 1) dayOff = $"{toNotify[i].NPCName} and ";
                            if (countDaysOff == 2 && tmpDayOff == 2) dayOff += $"{toNotify[i].NPCName}";
                            if (countDaysOff >= 3 && countDaysOff != tmpDayOff) dayOff += $"{toNotify[i].NPCName}, ";
                            if (countDaysOff >= 3 && countDaysOff == tmpDayOff) dayOff += $" and {toNotify[i].NPCName}";
                            break;
                    }
                }
                string openingNotification = useShopNames ? ( countOpening > 1 ? $"{opening} are open now!" : $"{opening} is open now!") : (countOpening > 1 ? $"{opening} stores are open now!" : $"{opening} store is open now!");
                string closingNotification = useShopNames ? (countOpening > 1 ? $"{closing} are closing {JournalPause.checkHoursBefore.Value} hours." : $"{closing} is closing in {JournalPause.checkHoursBefore.Value} hours.") : (countOpening > 1 ? $"{closing} stores are closing {JournalPause.checkHoursBefore.Value} hours." : $"{closing} store is closing in {JournalPause.checkHoursBefore.Value} hours.");
                string offTomorrowNotification = countOffTomorrow > 1 ? $"{offTomorrow} are closing in {JournalPause.checkHoursBefore.Value} hours and will be closed tomorrow!" : $"{offTomorrow} is closing in {JournalPause.checkHoursBefore.Value} hours and will be closed tomorrow!";
                string dayOffNotification = countDaysOff > 1 ? $"{dayOff} are having the day off!" : $"{dayOff} has the day off!";
                
                if (countOpening > 0) NotificationManager.manage.createChatNotification(openingNotification);
                if (countClosing > 0) NotificationManager.manage.createChatNotification(closingNotification);
                if (countOffTomorrow > 0) NotificationManager.manage.createChatNotification(offTomorrowNotification);
                if (countDaysOff > 0) NotificationManager.manage.createChatNotification(dayOffNotification);
                
                
                toNotify.Clear();
            }
            checkIfStoreOpenRunning = false;
        }

        #region Refresh Variables

        // Updates the variables on Start of RealWorldTimeLight
        // This fixes issues when exiting to main menu and re-entering games)
        [HarmonyPrefix]
        public static void startPrefix() {
            InitializeListData();
            JournalPause.runOnce = false;
        }

        // Updates the variables on start of new day
        [HarmonyPostfix]
        public static void startNewDayPostfix() {
            InitializeListData();
        }

        #endregion

        // Run the function on every tick of the clock
        [HarmonyPostfix]
        public static void clockTickPostfix() {
            if (RealWorldTimeLight.time.currentMinute == 00 && RealWorldTimeLight.time.currentHour > 7 && !checkIfStoreOpenRunning) {
                checkStoreStatus(JournalPause.realWorld, ShopInfo);
            }
        }

        public enum Status {
            Opening, Closing, DayOff, OffTomorrow, None
        }
    }

}
