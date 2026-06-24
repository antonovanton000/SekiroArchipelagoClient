using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Classes
{
    public class DeathLinkReasonHelper
    {
        private static List<string> _deathLinkReasons = new List<string>
        {
            "Skill Issue Detected",
            "Git Gud Failed",
            "Reaction Time Expired",
            "Unfortunate Circumstances",
            "Poor Life Choices",
            "Hesitation is defeat",
            "Unlucky",
            "Mistakes Were Made",
            "Lack of Awareness",
            "Button Mismanagement",
            "Parry.exe Stopped Working",
            "Camera Betrayal",
            "Victory Was Not Meant To Be",
            "Input Not Recognized"
        };

        public static string GetRandomDeathLinkReason()
        {            
            return _deathLinkReasons.OrderBy(x => Guid.NewGuid()).FirstOrDefault() ?? "Unknown Reason";
        }
    }
}
