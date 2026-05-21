using InjustUILibrary.Classes;
using InjustUILibrary.Validation;
using SekiroAPClient.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    /// Interaction logic for InputField.xaml
    /// </summary>
    public partial class InputField : UserControl, IValidationField
    {

        public new event EventHandler<KeyEventArgs> KeyDown;
        public new event EventHandler<KeyEventArgs> PreviewKeyDown;

        public InputField()
        {
            SetValue(ValidationRulesProperty, new Collection<ValidationRule>());
            InitializeComponent();
            Loaded += InputField_Loaded;
            Unloaded += InputField_Unloaded;
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
            pbx.KeyDown += (s, e) => KeyDown?.Invoke(this, e);
            pbx.PreviewKeyDown += (s, e) => PreviewKeyDown?.Invoke(this, e);
            tbx.KeyDown += (s, e) => KeyDown?.Invoke(this, e);
            tbx.PreviewKeyDown += (s, e) => PreviewKeyDown?.Invoke(this, e);
        }

        private void InputField_Unloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            RefreshThemeResources();
        }

        private void InputField_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (var item in ValidationRules)
            {
                if (item is RequierdRule)
                    this.IsRequiered = true;

                tbxTextBinding.ValidationRules.Add(item);
            }
            var binding = BindingOperations.GetBinding(this, InputField.TextProperty);
            if (binding != null)
            {
                var propertyPath = binding.Path.Path.Split(".").LastOrDefault();
                this.BindPropertyName = propertyPath ?? "";
            }
            RefreshThemeResources();
        }

        private void RefreshThemeResources()
        {
            if (plc.Foreground is SolidColorBrush placeholderBrush)
            {
                placeholderBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            }

            plc.SetResourceReference(TextBlock.ForegroundProperty, IsReadOnly ? "AccentBrushDisabled" : (IsPlaceholderUp ? "InputFieldLabelBrush" : "PrimaryTextDarkerBrush"));
            plc.Opacity = IsPlaceholderUp ? 1 : 0.4;
            tbx.SetResourceReference(TextBox.ForegroundProperty, "PrimaryTextDarkerBrush");
            tbx.SetResourceReference(TextBox.CaretBrushProperty, "PrimaryTextDarkerBrush");
            pbx.SetResourceReference(PasswordBox.ForegroundProperty, "PrimaryTextDarkerBrush");
            pbx.SetResourceReference(PasswordBox.CaretBrushProperty, "PrimaryTextDarkerBrush");
            psw_tbl.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextDarkerBrush");
            cmpBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            cmpBorder.SetResourceReference(Border.BorderBrushProperty, IsError ? "Danger" : "BorderColorBrush");
        }

        public string BindPropertyName { get; set; }


        public bool IsPlaceholderUp
        {
            get { return (bool)GetValue(IsPlaceholderUpProperty); }
            set { SetValue(IsPlaceholderUpProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsPlaceholderUp.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsPlaceholderUpProperty =
            DependencyProperty.Register("IsPlaceholderUp", typeof(bool), typeof(InputField), new PropertyMetadata(false));



        public bool IsRequiered
        {
            get { return (bool)GetValue(IsRequieredProperty); }
            set { SetValue(IsRequieredProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsRequiered.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsRequieredProperty =
            DependencyProperty.Register("IsRequiered", typeof(bool), typeof(InputField), new PropertyMetadata(false));


        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Text.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(InputField), new PropertyMetadata(string.Empty, OnTextChangedCallBack));

        private static void OnTextChangedCallBack(
        DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            InputField c = sender as InputField;
            if (c != null)
            {
                if (!c.IsFocused && string.IsNullOrEmpty(c.Text))
                {
                    if (c.IsPlaceholderUp)
                    {
                        var sb = c.FindResource("goDown") as Storyboard;
                        c.plc.Text = c.Placeholder;
                        sb.Begin();
                        c.IsPlaceholderUp = false;
                    }
                }
                c.OnTextChanged(c.Text);
            }
        }


        protected virtual void OnTextChanged(string text)
        {
            // Grab related data.
            // Raises INotifyPropertyChanged.PropertyChanged
            if (!string.IsNullOrEmpty(text) && !IsPlaceholderUp)
            {
                plc.Text = Label;
                var sb = FindResource("goUp") as Storyboard;
                sb.Begin();
                IsPlaceholderUp = true;
            }
        }

        public string Password
        {
            get { return (string)GetValue(PasswordProperty); }
            set { SetValue(PasswordProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Password.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PasswordProperty =
            DependencyProperty.Register("Password", typeof(string), typeof(InputField), new PropertyMetadata("", OnPasswordChangedCallBack));


        private static void OnPasswordChangedCallBack(
        DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            InputField c = sender as InputField;
            if (c != null)
            {
                c.OnPasswordChanged(c.Password);
            }
        }

        protected virtual void OnPasswordChanged(string text)
        {
            // Grab related data.
            // Raises INotifyPropertyChanged.PropertyChanged
            if (!string.IsNullOrEmpty(text) && !IsPlaceholderUp)
            {
                plc.Text = Label;
                var sb = FindResource("goUp") as Storyboard;
                sb.Begin();
                pbx.Password = text;
                IsPlaceholderUp = true;
            }
        }


        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Label.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(InputField), new PropertyMetadata(""));


        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Placeholder.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(InputField), new PropertyMetadata(string.Empty));


        public bool IsReadOnly
        {
            get { return (bool)GetValue(IsReadOnlyProperty); }
            set { SetValue(IsReadOnlyProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsReadOnly.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsReadOnlyProperty =
            DependencyProperty.Register("IsReadOnly", typeof(bool), typeof(InputField), new PropertyMetadata(false));



        public bool IsError
        {
            get { return (bool)GetValue(IsErrorProperty); }
            set { SetValue(IsErrorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsError.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsErrorProperty =
            DependencyProperty.Register("IsError", typeof(bool), typeof(InputField), new PropertyMetadata(false));


        public string ErrorMessage
        {
            get { return (string)GetValue(ErrorMessageProperty); }
            set { SetValue(ErrorMessageProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ErrorMessage.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ErrorMessageProperty =
            DependencyProperty.Register("ErrorMessage", typeof(string), typeof(InputField), new PropertyMetadata(string.Empty));



        public bool IsPassword
        {
            get { return (bool)GetValue(IsPasswordProperty); }
            set { SetValue(IsPasswordProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsPassword.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsPasswordProperty =
            DependencyProperty.Register("IsPassword", typeof(bool), typeof(InputField), new PropertyMetadata(false));


        public bool IsNumberOnly
        {
            get { return (bool)GetValue(IsNumberOnlyProperty); }
            set { SetValue(IsNumberOnlyProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsNumberOnly.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsNumberOnlyProperty =
            DependencyProperty.Register("IsNumberOnly", typeof(bool), typeof(InputField), new PropertyMetadata(false));


        public Color PlaceholderBackgroundColor
        {
            get { return (Color)GetValue(PlaceholderBackgroundColorProperty); }
            set { SetValue(PlaceholderBackgroundColorProperty, value); }
        }

        // Using a DependencyProperty as the backing store for PlaceholderBackgroundColor.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty PlaceholderBackgroundColorProperty =
            DependencyProperty.Register("PlaceholderBackgroundColor", typeof(Color), typeof(InputField), new PropertyMetadata(Color.FromRgb(0x1a, 0x1b, 0x1c)));

        public Collection<ValidationRule> ValidationRules
        {
            get => (Collection<ValidationRule>)GetValue(ValidationRulesProperty);            
            set => SetValue(ValidationRulesProperty, value);
        }

        // Using a DependencyProperty as the backing store for MyProperty.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ValidationRulesProperty =
            DependencyProperty.Register("ValidationRules", typeof(Collection<ValidationRule>), typeof(InputField), new PropertyMetadata(null));

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            plc.Text = Label;
            if (!IsPlaceholderUp)
            {
                var sb = FindResource("goUp") as Storyboard;
                sb.Begin();
                IsPlaceholderUp = true;
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var sb = FindResource("goDown") as Storyboard;
            if ((sender as TextBox).Text == "")
            {
                if (IsPlaceholderUp)
                {
                    plc.Text = Placeholder;
                    sb.Begin();
                    IsPlaceholderUp = false;
                }
            }
        }

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            plc.Text = Label;
            if (!IsPlaceholderUp)
            {
                var sb = FindResource("goUp") as Storyboard;
                sb.Begin();
                IsPlaceholderUp = true;
            }
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var sb = FindResource("goDown") as Storyboard;
            if ((sender as PasswordBox).Password == "")
            {
                if (IsPlaceholderUp)
                {
                    plc.Text = Placeholder;
                    sb.Begin();
                    IsPlaceholderUp = false;
                }

            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext != null && (sender as PasswordBox).IsFocused)
            {
                Password = ((PasswordBox)sender).Password;
                Text = ((PasswordBox)sender).Password;
            }
        }

        private void Button_MouseDown(object sender, MouseButtonEventArgs e)
        {
            psw_tbl.Text = pbx.Password;
            psw_tbl.Visibility = Visibility.Visible;
            pbx.Opacity = 0;
        }

        private void Button_MouseUp(object sender, MouseButtonEventArgs e)
        {
            psw_tbl.Text = "";
            psw_tbl.Visibility = Visibility.Collapsed;
            pbx.Opacity = 1;
        }

        private static readonly Regex _regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
        private static bool IsTextAllowed(string text)
        {
            return !_regex.IsMatch(text);
        }

        private void tbx_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (IsNumberOnly)
            {
                e.Handled = !IsTextAllowed(e.Text);
            }
        }

        private void TextBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (IsNumberOnly)
            {
                if (e.DataObject.GetDataPresent(typeof(String)))
                {
                    String text = (String)e.DataObject.GetData(typeof(String));
                    if (!IsTextAllowed(text))
                    {
                        e.CancelCommand();
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            }
        }

        public bool Validate()
        {
            if (!IsPassword)
            {
                tbx.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                if (System.Windows.Controls.Validation.GetHasError(tbx))
                {
                    var errors = System.Windows.Controls.Validation.GetErrors(tbx);
                    ErrorMessage = string.Join(" | ", errors.Select(i => i.ErrorContent));
                    IsError = true;
                    return false;
                }
                else
                {
                    IsError = false;
                    ErrorMessage = string.Empty;

                }

                return true;
            }
            else
            {
                if (IsRequiered)
                {
                    return !string.IsNullOrEmpty(Password);
                }
                return true;
            }
        }
    }
}
