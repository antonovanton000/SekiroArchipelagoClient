using Newtonsoft.Json;
using SekiroAPClient.Classes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Models;

public class ApRandomizationState
{
    public string ServerAddress { get; set; } = "";
    public string RoomName { get; set; } = "";
    public string SlotName { get; set; } = "";
    public string Seed { get; set; }      // Local seed
    public long LocalSeed { get; set; }      // Local seed
    public string Game { get; set; } = ""; // "Sekiro: Shadows Die Twice"
    public List<ApLotEntry> ApLotMap { get; set; } = new();
    public List<int> CheckedKeyItems { get; set; } = new();
    public RoomRandomizerOptions RoomRandomizerOptions { get; set; } = new();
}

public class ApLotEntry
{
    public long LotId { get; set; }
    public long BaseLotId { get; set; }
    public long LocationId { get; set; }    
    public int EventFlagId { get; set; } = -1;
    public long GoodId { get; set; }
    public long FullId { get; set; }
    public bool IsShop { get; set; }
    public bool IsForeign { get; set; }
    public int Quantity { get; set; }
    public string LocationName { get; set; } = default!;

    public string DebugText { get; set; } = default!;
}

