using SekiroAPClient.ViewModels;
using SekiroAPClient.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace SekiroAPClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static MainWindow instance;

        public MainWindow()
        {
            InitializeComponent();
            themeSwitch.IsChecked = Properties.Settings.Default.IsDarkTheme;
            instance = this;

        }

        bool needClean;
        public bool NeedClean { get { return needClean; } set { needClean = value; } }

        public static void ClearHistory()
        {
            if (!instance.frame.CanGoBack && !instance.frame.CanGoForward)
            {
                return;
            }

            var entry = instance.frame.RemoveBackEntry();
            while (entry != null)
            {
                entry = instance.frame.RemoveBackEntry();
            }

            instance.frame.Navigate(new PageFunction<string>() { RemoveFromJournal = true });
            instance.NeedClean = true;
        }

        public static void NavigateTo(Page page)
        {
            instance.frame.Navigate(page);
        }

        public static void GoBack()
        {
            instance.frame.GoBack();
        }

        #region EvenHandlers        

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GoBack_Click(object sender, RoutedEventArgs e)
        {
            if (frame.CanGoBack)
                frame.GoBack();
        }

        

        private void window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void frame_Navigated(object sender, NavigationEventArgs e)
        {
            if (NeedClean)
            {
                frame.JournalOwnership = JournalOwnership.OwnsJournal;
                frame.NavigationService.RemoveBackEntry();
            }
            NeedClean = false;
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
                this.WindowState = WindowState.Maximized;
            else
                this.WindowState = WindowState.Normal;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsPage = new SettingsPage() { DataContext = new SettingsViewModel() };
            NavigateTo(settingsPage);
        }

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            //var vm = new AllNewsViewModel();
            //var page = new AllNewsPage() { DataContext = vm };

            //MainWindow.NavigateTo(page);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            if (frame.Content is AboutPage)
                return;

            NavigateTo(new AboutPage());
        }

        private void ThemeSwitch_Changed(object sender, bool isDarkTheme)
        {
            Properties.Settings.Default.IsDarkTheme = isDarkTheme;
            Properties.Settings.Default.Save();
            Classes.ThemeManager.ApplyTheme(isDarkTheme);
        }

        #endregion

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.IsPositionSaved)
            {

                this.Top = Properties.Settings.Default.Top;
                this.Left = Properties.Settings.Default.Left;
                this.Height = Properties.Settings.Default.Height;
                this.Width = Properties.Settings.Default.Width;
                // Very quick and dirty - but it does the job
                if (Properties.Settings.Default.Maximized)
                {
                    WindowState = WindowState.Maximized;
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                // Use the RestoreBounds as the current values will be 0, 0 and the size of the screen
                Properties.Settings.Default.Top = RestoreBounds.Top;
                Properties.Settings.Default.Left = RestoreBounds.Left;
                Properties.Settings.Default.Height = RestoreBounds.Height;
                Properties.Settings.Default.Width = RestoreBounds.Width;
                Properties.Settings.Default.Maximized = true;
            }
            else
            {
                Properties.Settings.Default.Top = this.Top;
                Properties.Settings.Default.Left = this.Left;
                Properties.Settings.Default.Height = this.Height;
                Properties.Settings.Default.Width = this.Width;
                Properties.Settings.Default.Maximized = false;
            }
            Properties.Settings.Default.IsPositionSaved = true;
            Properties.Settings.Default.Save();
        }

        public static void MakeTransparent()
        {
            instance.ResizeMode = ResizeMode.NoResize;
            instance.frameBorder.BorderThickness = new Thickness(0);
            instance.mainborder.Background = Brushes.Transparent;
            instance.mainborder.BorderThickness = new Thickness(0);
            instance.windowTop.Visibility = Visibility.Collapsed;
        }

        public static void RemoveTransparent()
        {
            instance.ResizeMode = ResizeMode.CanResizeWithGrip;
            instance.frameBorder.BorderThickness = new Thickness(0, 1, 0, 0);
            instance.mainborder.BorderThickness = new Thickness(1);
            instance.mainborder.Background = App.Current.FindResource("BackgroundBrush") as Brush;
            instance.windowTop.Visibility = Visibility.Visible;
        }

        #region NotificationStuff
        private Action? _yesCallback;
        private Action? _noCallback;
        private Action? _okCallback;

        private Action<string> _promtCallback;
        public static void ShowMessage(string messageText, MessageNotificationType messageType, Action? yesCallback = null, Action? noCallback = null, Action? okCallback = null)
        {
            instance.tblNotificationMessage.Text = messageText;
            switch (messageType)
            {
                case MessageNotificationType.Ok:
                    instance.btnNotificationOk.Visibility = Visibility.Visible;
                    instance.btnNotificationYes.Visibility = Visibility.Collapsed;
                    instance.btnNotificationNo.Visibility = Visibility.Collapsed;
                    break;
                case MessageNotificationType.YesNo:
                    instance.btnNotificationOk.Visibility = Visibility.Collapsed;
                    instance.btnNotificationYes.Visibility = Visibility.Visible;
                    instance.btnNotificationNo.Visibility = Visibility.Visible;
                    break;
                default:
                    break;
            }
            instance._yesCallback = yesCallback;
            instance._noCallback = noCallback;
            instance._okCallback = okCallback;
            instance.NotificationGrid.Visibility = Visibility.Visible;
        }

        public static Task<bool> ShowYesNoMessageAsync(string message)
        {
            var tcs = new TaskCompletionSource<bool>();

            ShowMessage(
                message,
                MessageNotificationType.YesNo,
                () => tcs.SetResult(true),
                () => tcs.SetResult(false));

            return tcs.Task;
        }



        private void Notification_Yes_Click(object sender, RoutedEventArgs e)
        {
            NotificationGrid.Visibility = Visibility.Collapsed;
            _yesCallback?.Invoke();
        }

        private void Notification_No_Click(object sender, RoutedEventArgs e)
        {
            NotificationGrid.Visibility = Visibility.Collapsed;
            _noCallback?.Invoke();

        }

        private void Notification_Ok_Click(object sender, RoutedEventArgs e)
        {
            NotificationGrid.Visibility = Visibility.Collapsed;
            _okCallback?.Invoke();
        }

        public static void ShowTopButtons()
        {
            instance.btnSettingsGrid.Visibility = Visibility.Visible;
        }

        public static void HideTopButtons()
        {
            instance.btnSettingsGrid.Visibility = Visibility.Collapsed;
        }

        public static void CloseMessage()
        {
            instance.NotificationGrid.Visibility = Visibility.Collapsed;
        }

        public static void ShowErrorMessage(string errorMessage, string page, string procedure)
        {
            instance.tbxErrorMessage.Text = $"Страница: {page} \r\nПроцедура: {procedure}\r\nТекст ошибки: {errorMessage}";
            instance.ErrorMessageGrid.Visibility = Visibility.Visible;
        }

        public static void ShowToast(ToastInfo toastInfo)
        {
            instance.toastGrid.DataContext = toastInfo;
            Storyboard sbShow;
            Storyboard sbHide;
            Storyboard pbAnimation;

            var timer = new DispatcherTimer() { Interval = toastInfo.Duration };
            var ticksCount = toastInfo.Duration.TotalMilliseconds / 10;
            var tickValue = toastInfo.Duration.TotalMilliseconds / ticksCount;
            switch (toastInfo.ToastType)
            {
                case ToastType.Success:
                    sbShow = instance.FindResource("showSuccess") as Storyboard;
                    sbHide = instance.FindResource("hideSuccess") as Storyboard;
                    pbAnimation = instance.FindResource("successPBAnimation") as Storyboard;
                    (pbAnimation.Children[0] as DoubleAnimation).Duration = toastInfo.Duration;
                    ((instance.successToast.Child as Grid).Children[0] as Button).Click += (s, e) => {
                        if (timer != null)
                        {
                            instance.BeginStoryboard(sbHide);
                            timer?.Stop();
                        }
                    };
                    break;
                case ToastType.Warning:
                    sbShow = instance.FindResource("showWarning") as Storyboard;
                    sbHide = instance.FindResource("hideWarning") as Storyboard;
                    pbAnimation = instance.FindResource("warningPBAnimation") as Storyboard;
                    ((instance.warningToast.Child as Grid).Children[0] as Button).Click += (s, e) => {
                        if (timer != null)
                        {
                            instance.BeginStoryboard(sbHide);
                            timer.Stop();
                        }
                    };
                    break;
                case ToastType.Error:
                    sbShow = instance.FindResource("showError") as Storyboard;
                    sbHide = instance.FindResource("hideError") as Storyboard;
                    pbAnimation = instance.FindResource("dangerPBAnimation") as Storyboard;
                    ((instance.errorToast.Child as Grid).Children[0] as Button).Click += (s, e) => {
                        if (timer != null)
                        {
                            instance.BeginStoryboard(sbHide);
                            timer.Stop();
                        }
                    };
                    break;
                default:
                    sbShow = instance.FindResource("showSuccess") as Storyboard;
                    sbHide = instance.FindResource("hideSuccess") as Storyboard;
                    pbAnimation = instance.FindResource("successPBAnimation") as Storyboard;
                    break;
            }

            (pbAnimation.Children[0] as DoubleAnimation).Duration = toastInfo.Duration;
            timer.Tick += (s, e) => {
                instance.BeginStoryboard(sbHide);
                timer.Stop();
                timer = null;
            };

            instance.BeginStoryboard(sbShow);
            instance.BeginStoryboard(pbAnimation);
            timer.Start();
        }

        private void ErrorCopy_Click(object sender, RoutedEventArgs e)
        {
            tbxErrorMessage.Copy();
        }

        private void ErrorOk_Click(object sender, RoutedEventArgs e)
        {
            ErrorMessageGrid.Visibility = Visibility.Collapsed;
        }

        #endregion

    }

    public enum MessageNotificationType
    {
        Ok,
        YesNo
    }

    public enum ToastType
    {
        Success,
        Warning,
        Error
    }

    public class ToastInfo
    {
        public ToastInfo()
        {
            Title = "";
            Detail = "";
            ToastType = ToastType.Success;
            Duration = TimeSpan.FromMilliseconds(2000);
        }

        public ToastInfo(string title, string detail, ToastType toastType = ToastType.Success, int durationMs = 3000)
        {
            Title = title;
            Detail = detail;
            ToastType = toastType;
            Duration = TimeSpan.FromMilliseconds(durationMs);
        }
        public string Title { get; set; }
        public string Detail { get; set; }
        public ToastType ToastType { get; set; }
        public TimeSpan Duration { get; set; }
    }
}
