using CommunityToolkit.Mvvm.ComponentModel;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SekiroAPClient.ViewModels;
public partial class MyBaseViewModel : ObservableObject
{

    public MyBaseViewModel()
    {
        source = new CancellationTokenSource();
    }

    protected CancellationTokenSource source;

    public readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ObservableProperty]
    bool isBusy;

    [ObservableProperty]
    string title = "";


    [ObservableProperty]
    bool hasValidationErrors;        

    public string GetCurrentMethod([CallerMemberName] string callerName = "")
    {
        return callerName;
    }
}
