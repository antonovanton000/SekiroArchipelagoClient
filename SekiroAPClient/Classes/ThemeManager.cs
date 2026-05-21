using System.Windows;

namespace SekiroAPClient.Classes;

public static class ThemeManager
{
    private const string LightThemeSource = "Styles/LightTheme.xaml";
    private const string DarkThemeSource = "Styles/DarkTheme.xaml";

    public static event EventHandler? ThemeChanged;

    public static void ApplyTheme(bool isDarkTheme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var nextSource = new Uri(isDarkTheme ? DarkThemeSource : LightThemeSource, UriKind.Relative);

        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString;
            if (source == LightThemeSource || source == DarkThemeSource)
            {
                dictionaries[i] = new ResourceDictionary { Source = nextSource };
                ThemeChanged?.Invoke(null, EventArgs.Empty);
                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = nextSource });
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
