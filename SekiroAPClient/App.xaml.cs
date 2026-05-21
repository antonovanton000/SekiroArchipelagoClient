using Archipelago.MultiClient.Net;
using NLog;
using SekiroAPClient.Classes;
using SekiroAPClient.Properties;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;

namespace SekiroAPClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static string AppVersion { get; private set; }
        public static SemanticVersioning.Version CompatibleApWorldVersion = new SemanticVersioning.Version("3.0.0");
        public static Logger Logger = default!;
        public static PipeServer PipeServer = default!;
#if DEBUG
        public static string Location => System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#else
        public static string Location =>AppContext.BaseDirectory;
#endif
        public static ArchipelagoSession? CurrentSession { get; private set; }
        public App()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var attribute = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            AppVersion = attribute.Version.ToString();
            InitNLog();
            InitializePipeServer();
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Logger.Fatal(args.ExceptionObject as Exception, "Unexpected exception!");
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                Logger.Fatal(args.Exception, "Error in UI-thread!");
                args.Handled = true;
            };
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static void SetActiveSession(ArchipelagoSession session)
        {
            CurrentSession = session;
        }

        private void InitNLog()
        {
            var config = new NLog.Config.LoggingConfiguration();
            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = $"errorlog_{DateTime.Now:yyyyMMdd}.log" };
            if (Settings.Default.IsDebug)
            {
                config.AddRule(LogLevel.Info, LogLevel.Info, logfile);
                config.AddRule(LogLevel.Warn, LogLevel.Warn, logfile);
            }
            config.AddRule(LogLevel.Error, LogLevel.Error, logfile);
            config.AddRule(LogLevel.Fatal, LogLevel.Fatal, logfile);

            // Apply config           
            NLog.LogManager.Configuration = config;
            Logger = LogManager.GetCurrentClassLogger();
        }

        private void InitializePipeServer()
        {
            PipeServer = new PipeServer("SekiroAP");            
            PipeServer.ShowDebugLog = true;
            PipeServer.Start();            
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ThemeManager.ApplyTheme(Settings.Default.IsDarkTheme);
        }
    }

}
