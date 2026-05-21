using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace SekiroAPClient.Models
{
    public class GameItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class GameItemCategory
    {
        public string CategoryName { get; set; } = string.Empty;
        public ObservableCollection<GameItem> Items { get; set; } = [];
    }

    public class RawSekiroItem
    {
        public int code { get; set; }
        public string? name { get; set; }
        public string? type { get; set; }
    }
}
