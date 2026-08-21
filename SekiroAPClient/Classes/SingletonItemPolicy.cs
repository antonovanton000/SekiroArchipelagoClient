namespace SekiroAPClient.Classes;

public static class SingletonItemPolicy
{
    private static readonly HashSet<int> ForceQuantityOneGoodIds = new()
    {
        // Major key items
        2310, // Shinobi Prosthetic
        2400, // Mortal Blade
        2420, // Mibu Breathing Technique
        2500, // Lotus of the Palace
        2501, // Shelter Stone
        2502, // Aromatic Branch
        
        // Ending / quest items
        9000, // Divine Dragon's Tears
        9010, // Young Lord's Bell Charm
        9011, // Father's Bell Charm
        9060, // Dragon's Tally Board
        //9180, // Truly Precious Bait
        //9181, // Truly Precious Bait
        9200, // Fresh Serpent Viscera
        9201, // Dried Serpent Viscera
        9210, // Holy Chapter: Infested
        9211, // Holy Chapter: Dragon's Return
        9212, // Frozen Tears
        9213, // Rice for Kuro
        9214, // Fine Snow
        9215, // Red Carp Eyes
        9216, // Tomoe's Note
        9220, // Red and White Pinwheel
        9221, // White Pinwheel
        9230, // Great White Whisker
        9240, // Water of the Palace
        9300, // Gatehouse Key
        9403, // Hidden Temple Key
        9404, // Secret Passage Key
        9405, // Gun Fort Shrine Key
        2920, // Shinobi Esoteric Text
        2921, // Prosthetic Esoteric Text
        2922, // Ashina Esoteric Text
        2923, //Senpou Esoteric Text
        2924, //Mushin Esoteric Text
    
        // Prosthetic tools and prosthetic upgrade unlocks
        9700, // Shuriken Wheel
        9710, // Robert's Firecrackers
        9720, // Flame Barrel
        9721, // Pine Resin Ember
        9730, // Shinobi Axe of the Monkey
        9740, // Mist Raven's Feathers
        9750, // Sabimaru
        9760, // Iron Fortress
        9770, // Large Fan
        9780, // Gyoubu's Broken Horn
        9790, // Slender Finger
        9791, // Malcontent's Ring
    };

    public static bool ShouldClampToOne(int goodId)
    {
        return ForceQuantityOneGoodIds.Contains(goodId)
            || PermanentGoodToFlagCollection.GetPermanentFlagForItem(goodId) > 0;
          //|| goodId is >= 5200 and <= 5213; Exclude memories from this policy, as they are not really "singleton" items, and can be obtained multiple times in a single playthrough.
    }
}
