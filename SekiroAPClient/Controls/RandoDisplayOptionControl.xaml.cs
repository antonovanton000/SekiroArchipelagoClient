using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SekiroAPClient.Controls
{
    /// <summary>
    /// Interaction logic for RandoDisplayOptionControl.xaml
    /// </summary>
    public partial class RandoDisplayOptionControl : UserControl
    {
        public RandoDisplayOptionControl()
        {
            InitializeComponent();
        }



        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Text.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(RandoDisplayOptionControl), new PropertyMetadata(string.Empty));



        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Description.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(RandoDisplayOptionControl), new PropertyMetadata(string.Empty, new PropertyChangedCallback((obj, t) => { 
                    if (obj is RandoDisplayOptionControl control)
                    {
                        control.HasDescription = !string.IsNullOrEmpty(control.Description);
                    }
            })));

        public bool HasDescription
        {
            get { return (bool)GetValue(HasDescriptionProperty); }
            set { SetValue(HasDescriptionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for HasDescription.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty HasDescriptionProperty =
            DependencyProperty.Register(nameof(HasDescription), typeof(bool), typeof(RandoDisplayOptionControl), new PropertyMetadata(false));



        public bool IsChecked
        {
            get { return (bool)GetValue(IsCheckedProperty); }
            set { SetValue(IsCheckedProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsChecked.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsCheckedProperty =
            DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(RandoDisplayOptionControl), new PropertyMetadata(false));



    }
}
