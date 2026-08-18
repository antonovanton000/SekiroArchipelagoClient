using System.Windows;

namespace SekiroAPClient.Views;

public partial class SteamLaunchOptionsWindow : Window
{
    public SteamLaunchOptionsWindow()
    {
        InitializeComponent();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LaunchOptionsTextBox.Text);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
