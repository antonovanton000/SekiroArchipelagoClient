using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Classes
{
    public class ItemDescriptionHelper
    {
        private static List<string> _itemsDescriptions = new List<string>
        {
            "A relic drawn from a distant realm known as {0}.\rIt strayed far from its rightful owner.\rSomewhere, another soul may be searching for what you now hold.",
            "An object that crossed worlds from {0}.\rIt carries a faint echo of another's struggle.\rThe bond between warriors is not so easily severed.",
            "An object from the world called {0}.\rThough foreign to Ashina, it is bound here by unseen threads of fate.\rWhat is lost in one world may be found in another.",
            "An item that does not belong to this land.\rIt came from a distant world known as {0}.\rIts arrival is proof that destinies intertwine beyond reason.",
            "An object delivered from {0}.\rIt arrived through means unknown, guided only by fate.\rPerhaps another warrior walks a parallel path.",
            "A treasure from a distant realm called {0}.\rIts craftsmanship differs from that of Ashina.\rStill, it answers the hand that wields it.",
            "An artifact carried across worlds from {0}.\rIt feels slightly out of place, as though reality itself shifted to allow its passage.\rSuch things are not coincidence.",
            "An object from {0}.\rIt once marked progress in another’s journey.\rNow, that journey is shared."            
        };

        public static string GetRandomItemDescription(string gameName)
        {
            return string.Format(_itemsDescriptions.OrderBy(x => Guid.NewGuid()).First(), gameName);
        }
    }
}
