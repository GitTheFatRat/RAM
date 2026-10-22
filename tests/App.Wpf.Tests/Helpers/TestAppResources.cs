namespace RbxManager.App.Tests.Helpers;

using System;
using System.Windows;

public static class TestAppResources
{
    private static bool _initialized;

    public static void Initialize(string theme = "Dark")
    {
        if (Application.Current == null)
        {
            _ = new Application();
        }

        if (!_initialized)
        {
            try
            {
                Application.Current?.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
                Application.Current?.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());

                Uri tokensUri = new("/RbxManager.App;component/Themes/Tokens.xaml", UriKind.Relative);
                ResourceDictionary tokensDictionary = (ResourceDictionary)Application.LoadComponent(tokensUri);
                Application.Current?.Resources.MergedDictionaries.Add(tokensDictionary);
            }
            catch
            {
                throw;
            }

            _initialized = true;
        }

        try
        {
            if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            else
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            }
        }
        catch
        {
        }
    }
}
