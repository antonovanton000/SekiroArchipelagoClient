using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Models;

public partial class ConnectModel : ObservableObject
{  
    public static string GameName => "Sekiro: Shadows Die Twice";

    [ObservableProperty]
    string roomUrl = default!;

    [ObservableProperty]
    string playerName = default!;

    [ObservableProperty]
    string password = default!;
}
