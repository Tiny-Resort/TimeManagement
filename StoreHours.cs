using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;

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
        public static void startNewDayPostfix() {
            for (int i = 0; i < openingHours.Count; i++) {
                openingHours[i].checkedClosing = false;
                openingHours[i].checkedOpening = false;
                openingHours[i].checkedIfDayOff = false;
            }
        }

        #endregion

        #region Run Every Clock Tick

        [HarmonyPostfix]
        public static void clockTickPostfix() { runCheckIfOpenOrCloseSoon(JournalPause.realWorld, openingHours); }

        #endregion

        #region Check Store Hours and Days Off

        public static bool checkDaysOff(ShopInfo details, bool checkTomorrow) {

            int currentDay = checkTomorrow == false ? WorldManager.manageWorld.day - 1 : WorldManager.manageWorld.day;
            int nextDay = currentDay >= 7 ? 0 : currentDay;

            if (checkTomorrow) {
                if (details.details.mySchedual.dayOff[nextDay]) { return true; }
            }
            if (details.details.mySchedual.dayOff[currentDay]) { return true; }
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

        public static bool checkStoreHours(ShopInfo details, bool checkClosing) {

            int currentHour = checkClosing == false ? RealWorldTimeLight.time.currentHour : RealWorldTimeLight.time.currentHour + JournalPause.checkHoursBefore.Value;
            bool isDayOff = details.details.mySchedual.dayOff[WorldManager.manageWorld.day - 1];
            bool isNotWonder = details.details.mySchedual.dailySchedual[currentHour] != NPCSchedual.Locations.Wonder;
            bool isNotExit = details.details.mySchedual.dailySchedual[RealWorldTimeLight.time.currentHour] != NPCSchedual.Locations.Exit;
            if (currentHour != 0 && currentHour != 24 && !isDayOff && isNotWonder && isNotExit) { return true; }
            return false;
        }

        #endregion

        #region Run Check and Send Notification

        public static void runCheckIfOpenOrCloseSoon(RealWorldTimeLight time, List<ShopInfo> list) {
            for (int i = 0; i < list.Count; i++) {
                if (!JournalPause.ignoreFullList.Contains(list[i].details.NPCName.ToLower())) {
                    if (checkStoreHours(list[i], false) && time.currentMinute > 00 && list[i].isVillager && !list[i].checkedOpening && time.currentHour == 7) { list[i].checkedOpening = true; }
                    if (checkStoreHours(list[i], false) && time.currentMinute == 00 && list[i].isVillager && !list[i].checkedOpening && time.currentHour >= 8 && time.currentHour < 13) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner}'s store just opened (at {list[i].morningHours}AM).");
                        list[i].checkedOpening = true;
                    }
                    if (time.currentHour < 23) {
                        if (!checkDaysOff(list[i], false) && !checkStoreHours(list[i], true) && time.currentHour > 12 && time.currentMinute == 0 && list[i].isVillager && !list[i].checkedClosing) {
                            if (checkDaysOff(list[i], true)) { NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in {JournalPause.checkHoursBefore.Value} hour(s) (at {list[i].closingHours - 12}PM) and will be closed tomorrow."); }
                            else
                                NotificationManager.manage.createChatNotification($"{list[i].owner}'s store will close in an hour(at {list[i].closingHours - 12}PM).");
                            list[i].checkedClosing = true;
                        }
                    }
                    if (checkDaysOff(list[i], false) && !list[i].checkedIfDayOff && list[i].isVillager && time.currentHour >= 8) {
                        NotificationManager.manage.createChatNotification($"{list[i].owner} is off today!");
                        list[i].checkedIfDayOff = true;
                    }
                }
            }
        }

        #endregion

    }

}
