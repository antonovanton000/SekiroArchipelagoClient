using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.Models;
public partial class RoomRandomizerOptions: ObservableObject
{
    [ObservableProperty]
    bool randomizeEnemies;
    
    [ObservableProperty]
    bool randomizeBosses;
    [ObservableProperty]
    bool randomizeMiniBosses;
    [ObservableProperty]
    bool randomizeRegularEnemies;
    [ObservableProperty]
    bool randomizeHeadless;
    [ObservableProperty]
    bool similarBossPhases;
    [ObservableProperty]
    bool balancedBossPhases;
    [ObservableProperty]
    bool simpleEarlyMinibosses;
    [ObservableProperty]
    bool scaleEnemies;
    [ObservableProperty]
    bool carpsanity;
    [ObservableProperty]
    bool deathLink;
    [ObservableProperty]
    bool deathLinkOnFullDeath;
    [ObservableProperty]
    bool randomSkills;
    [ObservableProperty]
    bool openBellDoor;
    [ObservableProperty]
    bool headlessSlowWalk;
    [ObservableProperty]
    string presetName = default!;
}

