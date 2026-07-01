using System.Configuration;
using System.Data;
using System.Windows;

namespace AppUpdater
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static bool IsLinux { get; private set; }

        public App()
        {
            IsLinux = Environment.GetCommandLineArgs()
                    .Skip(1)
                    .Any(arg => string.Equals(arg, "--linux", StringComparison.OrdinalIgnoreCase));
        }
    }

}
