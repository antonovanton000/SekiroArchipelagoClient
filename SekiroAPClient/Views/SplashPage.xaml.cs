using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SekiroAPClient.Views;

/// <summary>
/// Interaction logic for SplashPage.xaml
/// </summary>
public partial class SplashPage : Page
{
    public SplashPage()
    {
        InitializeComponent();
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/antonovanton000/SekiroArchipelagoClient",
            UseShellExecute = true
        });
    }
}
