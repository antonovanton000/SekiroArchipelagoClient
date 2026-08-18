using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SekiroAPClient.Classes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SekiroAPClient.ViewModels
{
    public partial class FeedbackViewModel : MyBaseViewModel
    {
        [ObservableProperty]
        bool isPositive = false;

        [ObservableProperty]
        bool isNegative = false;

        [ObservableProperty]
        string name = default!;

        [ObservableProperty]
        string message = default!;

        [RelayCommand]
        void Appearing()
        {
            MainWindow.HideTopButtons();
        }

        [RelayCommand]
        async Task SendFeedback()
        {            
            var lastFeedbackSentAt = Properties.Settings.Default.LastFeedbackSentAt;

            if ((DateTime.Now - lastFeedbackSentAt).TotalSeconds < 120)
            {
                MainWindow.ShowToast(new ToastInfo()
                {
                    Title = "Feedback Rate Limit",
                    Detail = "Please wait a few minutes...",
                    ToastType = ToastType.Warning,
                });
                return;
            }

            var res = await FeedbackSenderService.SendFeedbackAsync(new FeedbackSenderService.FeedbackData(Name, Message, IsPositive));
            if (res)
            {
                MainWindow.ShowToast(new ToastInfo()
                {
                    Title = "Feedback Sent",
                    Detail = "Thank you for your feedback!",
                    ToastType = ToastType.Success,
                });
                
                ClearFeedback();
            }
            else
            {
                MainWindow.ShowToast(new ToastInfo()
                {
                    Title = "Error",
                    Detail = "Something went wrong we will fix that :)",
                    ToastType = ToastType.Error,
                });
            }

            Properties.Settings.Default.LastFeedbackSentAt = DateTime.Now;
            Properties.Settings.Default.Save();
            MainWindow.GoBack();
        }

        void ClearFeedback()
        {
            Name = string.Empty;
            Message = string.Empty;
            IsPositive = false;
            IsNegative = false;
        }
    }
}
