using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
namespace SekiroAPClient.Views;

/// <summary>
/// Interaction logic for FeedbackPage.xaml
/// </summary>
public partial class FeedbackPage : Page
{   
    public FeedbackPage()
    {
        InitializeComponent();
        runVersion.Text = App.AppVersion;
    }   
}
