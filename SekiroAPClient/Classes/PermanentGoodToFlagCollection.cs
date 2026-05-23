using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Classes
{
    public class PermanentGoodToFlagCollection
    {
        private static Dictionary<int,int> permanentGoodToFlag = new Dictionary<int, int>
        {
            { 2910, 6740 }, // Mechanical Barrel
            { 9790, 6509 }, // Slender Finger
            //{ 2110, 6746 }, // Puppeteer Ninjutsu
            //{ 9009, 6750 }, // Sakura Droplet
            //{ 9060, 6751 }, // Dragon's Tally Board
            //{ 2100, 6745 }, // Bloodsmoke Ninjutsu
            //{ 2462, 6701 }, // One Mind
            //{ 2460, 6700 }, // Dragon Flash
            //{ 2120, 6747 }, // Bestowal Ninjutsu
            //{ 6100, 6702 }, // Black Gunpowder
            //{ 2490, 6716 }, // Sakura Dance
            { 2920, 6705 }, // Shinobi Esoteric Text
            { 2921, 6706 }, // Prosthetic Esoteric Text
            { 9720, 6502 }, // Flame Barrel
            { 9730, 6503 }, // Shinobi Axe of the Monkey
            { 9740, 6504 }, // Mist Raven's Feathers
            { 9700, 6500 }, // Shuriken Wheel
            { 9750, 6505 }, // Sabimaru
            { 9780, 6508 }, // Gyoubu's Broken Horn
            { 9721, 6742 }, // Pine Resin Ember
            { 9770, 6507 }, // Large Fan
            //{ 4400, 6724 }, // Gourd Seed            
            //{ 2923, 6708 }, // Senpou Esoteric Text
            { 9791, 6743 }, // Malcontent's Ring
            //{ 2481, 6715 }, // Breath of Nature: Shadow
            //{ 2471, 6711 }, // Shinobi Medicine Rank 2
            //{ 2475, 6713 }, // A Beast's Karma
            //{ 2472, 6712 }, // Shinobi Medicine Rank 3
            //{ 2470, 6710 }, // Shinobi Medicine Rank 1
            //{ 2480, 6714 }, // Breath of Life: Shadow
            //{ 5510, 6022 }, // Dancing Dragon Mask
            { 2450, 6719 }, // Anti-air Deathblow Text
        };

        public static int GetPermanentFlagForItem(int itemId)
        {
            var eventId = 0;
            permanentGoodToFlag.TryGetValue(itemId, out eventId);
            return eventId;
        }

        public static int GetExpectedItemForPermanentFlag(int eventFlag)
        {
            foreach (var pair in permanentGoodToFlag)
            {
                if (pair.Value == eventFlag)
                    return pair.Key;
            }

            return 0;
        }
    }
}
