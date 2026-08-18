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

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(
                new ProcessStartInfo
                {
                    FileName = "https://discordapp.com/users/742384872584118434",
                    UseShellExecute = true
                });
    }
}
