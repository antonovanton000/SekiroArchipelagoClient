using Archipelago.MultiClient.Net.Helpers;
using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Models
{
    public class ServerNotification
    {
        public string Player { get; set; }

        public string Item { get; set; }

        public string Location { get; set; }

        public string Text { get; set; }

        public bool IsItemNotification { get; set; }
        
    }
}
