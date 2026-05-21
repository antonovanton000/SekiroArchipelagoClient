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

namespace SekiroAPClient.Controls;

/// <summary>
/// Interaction logic for SpinerControl.xaml
/// </summary>
public partial class SpinerControl : UserControl
{
    private Storyboard sb;
    public SpinerControl()
    {
        InitializeComponent();            
        sb = FindResource("roateAnimation") as Storyboard;                    
        Loaded += SpinerControl_Loaded;
    }

    private void SpinerControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsActive)
            sb.Begin();            
        Resize(IsSmall);
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
        DependencyProperty.Register("IsActive", typeof(bool), typeof(SpinerControl), new PropertyMetadata(true));


    public SolidColorBrush SpinerColor
    {
        get { return (SolidColorBrush)GetValue(SpinerColorProperty); }
        set { SetValue(SpinerColorProperty, value); }
    }

    // Using a DependencyProperty as the backing store for SpinerColor.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty SpinerColorProperty =
        DependencyProperty.Register("SpinerColor", typeof(SolidColorBrush), typeof(SpinerControl), new PropertyMetadata(new SolidColorBrush(Colors.Black)));




    public bool IsSmall
    {
        get { return (bool)GetValue(IsSmallProperty); }
        set { SetValue(IsSmallProperty, value); Resize(value); }
    }

    // Using a DependencyProperty as the backing store for IsSmall.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty IsSmallProperty =
        DependencyProperty.Register("IsSmall", typeof(bool), typeof(SpinerControl), new PropertyMetadata(false));




    private void Resize(bool isSmall)
    {
        if (isSmall)
        {
            Height = 20;
            Width = 20;
            foreach (var el in mainGrid.Children.Cast<Ellipse>())
            {
                el.Height = 4;
                el.Width = 4;
                ((el.RenderTransform as TransformGroup).Children[0] as TranslateTransform).Y = -9;
            }
        }
        else
        {
            Height = 80;
            Width = 80;
            foreach (var el in mainGrid.Children.Cast<Ellipse>())
            {
                el.Height = 10;
                el.Width = 10;
                ((el.RenderTransform as TransformGroup).Children[0] as TranslateTransform).Y = -35;
            }
        }
    }

}
