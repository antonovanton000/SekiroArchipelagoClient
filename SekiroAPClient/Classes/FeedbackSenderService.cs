using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Windows.Data;

namespace SekiroAPClient.Classes
{
    public class FeedbackSenderService
    {
        private const string FormspreeUrl = "https://formspree.io/f/xjgnbaol";

        public static async Task<bool> SendFeedbackAsync(FeedbackData feedback)
        {
            try
            {
                using var httpClient = new HttpClient();

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("name", feedback.Name),
                    new KeyValuePair<string, string>("message", feedback.Message),
                    new KeyValuePair<string, string>("isPositive", feedback.IsPositive.ToString())
                });

                await httpClient.PostAsync(FormspreeUrl, content);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
                return false;
            }
        }


        public record FeedbackData(string Name, string Message, bool IsPositive);
    }
}
