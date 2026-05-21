using AppUpdater.Classes;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace AppUpdater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        CancellationTokenSource cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            cancellationTokenSource = new CancellationTokenSource();
        }
        private string archiveName = "tempdownload.zip";
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                progress.IsIndeterminate = true;
                var appProcess = Process.GetProcessesByName("SekiroAPClient");
                if (appProcess.Length > 0)
                {
                    foreach (var process in appProcess)
                    {
                        process.Kill();
                    }
                }
                await Task.Delay(1000);

                tbl.Text = "Checking for updates...";

                using var http = new HttpClient();
                var provider = new GitHubReleaseProvider(http, "antonovanton000", "SekiroArchipelagoClient");

                var alpha = await provider.GetLatestAsync(includePrerelease: true, assetName: "randomizerAP.zip");
                var stable = await provider.GetLatestAsync(includePrerelease: false, assetName: "randomizerAP.zip");

                if (stable == null)
                {
                    if (alpha == null)
                    {
                         throw new Exception("No releases found.");
                    }                    
                }

                var url = stable == null ? alpha.DownloadUrl : stable.DownloadUrl;
                var version = stable == null ? alpha.Version.ToString() : stable.Version.ToString();

                tbl.Text = "Downloading update...";
                progress.IsIndeterminate = false;                
                var pr = new Progress<double>(p =>
                {
                    // Эти колбэки выполняются в UI thread автоматически
                    progress.Value = p;                 // ProgressBar: Minimum=0 Maximum=100
                    progressText.Text = $"{p:0.0}%";
                });

                await DownloadHelper.DownloadFileWithProgressAsync(http, url, archiveName, pr, cancellationTokenSource.Token);
                progressText.Text = "";
                tbl.Text = "Installing update...";
                progress.IsIndeterminate = true;
                using var archive = System.IO.Compression.ZipFile.Open(archiveName, System.IO.Compression.ZipArchiveMode.Read);
                foreach (var item in archive.Entries)
                {
                    var name = item.FullName.Replace("randomizerAP/", "");

                    if (name == "" || name == "AppUpdater.exe" || name == "AppUpdater.dll.config") 
                        continue;
                    
                    if (name.Contains(@"/"))
                    {
                        var folderName = System.IO.Path.GetDirectoryName(name);
                        if (!Directory.Exists(folderName))
                            Directory.CreateDirectory(folderName);
                    }
                    if (item.Length > 0)
                        item.ExtractToFile(name, true);
                }
                archive.Dispose();

#if !DEBUG

                await Task.Delay(1000);
                Process.Start("SekiroAPClient.exe");
                await Task.Delay(500);
#endif
                File.Delete(archiveName);
                progress.IsIndeterminate = false;
                progress.Value = 0;
                tbl.Text = "Done!";
                await Task.Delay(1000);
                App.Current.Shutdown();                

            }
            catch (Exception ex)
            {
                await Task.Delay(1000);
                if (File.Exists(archiveName))
                    File.Delete(archiveName);
                
                if (!(ex is TaskCanceledException))
                    File.WriteAllText("update_errors.log", ex.Message); 

                App.Current.Shutdown();
            }

        }

        private async void Cancel_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource.Cancel();           
        }

        private void Button_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {

        }
    }
}