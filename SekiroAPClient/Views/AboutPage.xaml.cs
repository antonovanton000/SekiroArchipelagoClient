using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
namespace SekiroAPClient.Views;

/// <summary>
/// Interaction logic for AboutPage.xaml
/// </summary>
public partial class AboutPage : Page
{   
    public AboutPage()
    {
        InitializeComponent();
        runVersion.Text = App.AppVersion;
    }   
}
