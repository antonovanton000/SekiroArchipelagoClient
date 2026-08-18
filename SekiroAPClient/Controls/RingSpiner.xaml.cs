using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace InjustUILibrary.Controls
{
    /// <summary>
    /// Interaction logic for Spiner.xaml
    /// </summary>
    public partial class RingSpiner : UserControl
    {
        private Storyboard sb;
        public RingSpiner()
        {
            InitializeComponent();            
            sb = FindResource("roateAnimation") as Storyboard;                    
            Loaded += SpinerControl_Loaded;
        }

        private void SpinerControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsActive)
                sb.Begin();                        
        }

        public bool IsActive
        {
            get {             
                return (bool)GetValue(IsActiveProperty); 
            }
            set { 
                SetValue(IsActiveProperty, value);   
                if (value == false)
                {
                    sb.Stop();
                }
                if (value == true)
                {
                    sb.Begin();
                }
            }
        }

        // Using a DependencyProperty as the backing store for IsActive.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register("IsActive", typeof(bool), typeof(RingSpiner), new PropertyMetadata(true));


        public SolidColorBrush SpinerColor
        {
            get { return (SolidColorBrush)GetValue(SpinerColorProperty); }
            set { SetValue(SpinerColorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SpinerColor.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SpinerColorProperty =
            DependencyProperty.Register("SpinerColor", typeof(SolidColorBrush), typeof(RingSpiner), new PropertyMetadata(new SolidColorBrush(Colors.White)));



        public bool IsSmall
        {
            get { return (bool)GetValue(IsSmallProperty); }
            set { SetValue(IsSmallProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsSmall.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsSmallProperty =
            DependencyProperty.Register("IsSmall", typeof(bool), typeof(RingSpiner), new PropertyMetadata(false));



    }
}
