using SekiroAPClient.Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SekiroAPClient
{
    /// <summary>
    /// Interaction logic for BoardWindow.xaml
    /// </summary>
    public partial class OverlayWindow : Window
    {

        WindowSinker sinker;

        public bool NotSavePosition { get; set; } = false;

        public OverlayWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) =>
            {
                if (Properties.Settings.Default.OWIsPositionSaved)
                {
                    this.Top = Properties.Settings.Default.OWTop;
                    this.Left = Properties.Settings.Default.OWLeft;
                    this.Height = Properties.Settings.Default.OWHeight;
                    this.Width = Properties.Settings.Default.OWWidth;
                }
            };
            Closing += (s, e) =>
            {
                if (NotSavePosition)
                    return;

                Properties.Settings.Default.OWTop = this.Top;
                Properties.Settings.Default.OWLeft = this.Left;
                Properties.Settings.Default.OWHeight = this.Height;
                Properties.Settings.Default.OWWidth = this.Width;                
                Properties.Settings.Default.OWIsPositionSaved = true;
                Properties.Settings.Default.Save();
            };
            sinker = new WindowSinker(this);
            sinker.Sink();
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
