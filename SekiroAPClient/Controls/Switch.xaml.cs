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
    /// Interaction logic for Switch.xaml
    /// </summary>
    public partial class Switch : UserControl
    {
        public Switch()
        {
            InitializeComponent();
        }


        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsChecked.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register("IsChecked", typeof(bool), typeof(Switch), new PropertyMetadata(false, OnIsCheckedChangedCallBack));

        private static void OnIsCheckedChangedCallBack(
        DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            Switch c = sender as Switch;
            if (c != null)
            {
                c.OnIsCheckedChanged(c.IsChecked);
            }
        }

        protected virtual void OnIsCheckedChanged(bool isChecked)
        {
            // Grab related data.
            // Raises INotifyPropertyChanged.PropertyChanged
            if (IsChecked)
            {
                var sb = FindResource("switch_on") as Storyboard;
                sb.Begin();
            }
            else
            {
                var sb = FindResource("switch_off") as Storyboard;
                sb.Begin();
            }
        }



        public ICommand ChangeStateCommand
        {
            get { return (ICommand)GetValue(ChangeStateCommandProperty); }
            set { SetValue(ChangeStateCommandProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ChangeStateCommand.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ChangeStateCommandProperty =
            DependencyProperty.Register(nameof(ChangeStateCommand), typeof(ICommand), typeof(Switch), new PropertyMetadata(null));




        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Text.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(Switch), new PropertyMetadata(""));



        private void switchBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IsChecked = !IsChecked;
            if (IsChecked)
            {
                var sb = FindResource("switch_on") as Storyboard;
                sb.Begin();
            }
            else
            {
                var sb = FindResource("switch_off") as Storyboard;
                sb.Begin();
            }
            ChangeStateCommand?.Execute(null);
        }
    }
}
