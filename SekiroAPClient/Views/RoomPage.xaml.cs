using SekiroAPClient.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SekiroAPClient.Views
{
    /// <summary>
    /// Interaction logic for RoomInfoPage.xaml
    /// </summary>
    public partial class RoomPage : Page
    {
        public RoomPage()
        {
            InitializeComponent();        
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            (sender as TextBox).ScrollToEnd();
        }

        private void InputField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (this.DataContext is RoomViewModel vm)
                {
                    vm.SendCommandCommand.Execute(null);
                }
            }
        }

        private void InputField_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                if (this.DataContext is RoomViewModel vm)
                {
                    vm.GetNextCommandCommand.Execute(null);
                }
            }
            if (e.Key == Key.Up)
            {
                if (this.DataContext is RoomViewModel vm)
                {
                    vm.GetPrevCommandCommand.Execute(null);
                }
            }
        }
    }
}
