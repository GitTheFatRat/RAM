namespace RbxManager.App.Themes;

using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

public static class ThemeTokens
{
    public static void ApplyTheme(ApplicationTheme theme)
    {
        ResourceDictionary resources = Application.Current.Resources;
        bool isLight = theme == ApplicationTheme.Light;

        // Surface hierarchy
        SetBrush(resources, "ApplicationBackgroundBrush", isLight ? "ApplicationBackgroundLightColor" : "ApplicationBackgroundDarkColor");
        SetBrush(resources, "CardBackgroundBrush", isLight ? "CardBackgroundLightColor" : "CardBackgroundDarkColor");
        SetBrush(resources, "CardBackgroundFillColorDefaultBrush", isLight ? "CardBackgroundLightColor" : "CardBackgroundDarkColor");
        SetBrush(resources, "ControlFillColorDefaultBrush", isLight ? "ControlFillColorDefaultLightColor" : "ControlFillColorDefaultDarkColor");
        SetBrush(resources, "ControlFillColorSecondaryBrush", isLight ? "ControlFillColorSecondaryLightColor" : "ControlFillColorSecondaryDarkColor");
        SetBrush(resources, "ControlFillColorTertiaryBrush", isLight ? "ControlFillColorTertiaryLightColor" : "ControlFillColorTertiaryDarkColor");
        SetBrush(resources, "NavigationPaneBackgroundBrush", isLight ? "NavigationPaneBackgroundLightColor" : "NavigationPaneBackgroundDarkColor");
        SetBrush(resources, "SurfaceElevatedBrush", isLight ? "SurfaceElevatedLightColor" : "SurfaceElevatedDarkColor");

        // Text
        SetBrush(resources, "TextFillColorPrimaryBrush", isLight ? "TextFillColorPrimaryLightColor" : "TextFillColorPrimaryDarkColor");
        SetBrush(resources, "TextFillColorSecondaryBrush", isLight ? "TextFillColorSecondaryLightColor" : "TextFillColorSecondaryDarkColor");

        // DataGrid & dividers
        SetBrush(resources, "DataGridRowAlternatingBrush", isLight ? "DataGridRowAlternatingLightColor" : "DataGridRowAlternatingDarkColor");
        SetBrush(resources, "DataGridHeaderBorderBrush", isLight ? "DataGridHeaderBorderLightColor" : "DataGridHeaderBorderDarkColor");
        SetBrush(resources, "DividerStrokeColorDefaultBrush", isLight ? "DividerStrokeColorDefaultLightColor" : "DividerStrokeColorDefaultDarkColor");

        // Accent
        SetBrush(resources, "AccentBrush", isLight ? "AccentLightColor" : "AccentDarkColor");
        SetBrush(resources, "AccentHoverBrush", isLight ? "AccentHoverLightColor" : "AccentHoverDarkColor");
        SetBrush(resources, "AccentSubtleBrush", isLight ? "AccentSubtleLightColor" : "AccentSubtleDarkColor");
        SetBrush(resources, "AccentForegroundBrush", isLight ? "AccentForegroundLightColor" : "AccentForegroundDarkColor");

        // Status: Valid
        SetBrush(resources, "StatusValidForegroundBrush", isLight ? "StatusValidForegroundLightColor" : "StatusValidForegroundDarkColor");
        SetBrush(resources, "StatusValidBackgroundBrush", isLight ? "StatusValidBackgroundLightColor" : "StatusValidBackgroundDarkColor");
        SetBrush(resources, "StatusValidBorderBrush", isLight ? "StatusValidBorderLightColor" : "StatusValidBorderDarkColor");

        // Status: Expired
        SetBrush(resources, "StatusExpiredForegroundBrush", isLight ? "StatusExpiredForegroundLightColor" : "StatusExpiredForegroundDarkColor");
        SetBrush(resources, "StatusExpiredBackgroundBrush", isLight ? "StatusExpiredBackgroundLightColor" : "StatusExpiredBackgroundDarkColor");
        SetBrush(resources, "StatusExpiredBorderBrush", isLight ? "StatusExpiredBorderLightColor" : "StatusExpiredBorderDarkColor");

        // Status: Error
        SetBrush(resources, "StatusErrorForegroundBrush", isLight ? "StatusErrorForegroundLightColor" : "StatusErrorForegroundDarkColor");
        SetBrush(resources, "StatusErrorBackgroundBrush", isLight ? "StatusErrorBackgroundLightColor" : "StatusErrorBackgroundDarkColor");
        SetBrush(resources, "StatusErrorBorderBrush", isLight ? "StatusErrorBorderLightColor" : "StatusErrorBorderDarkColor");

        // Status: Unknown
        SetBrush(resources, "StatusUnknownForegroundBrush", isLight ? "StatusUnknownForegroundLightColor" : "StatusUnknownForegroundDarkColor");
        SetBrush(resources, "StatusUnknownBackgroundBrush", isLight ? "StatusUnknownBackgroundLightColor" : "StatusUnknownBackgroundDarkColor");
        SetBrush(resources, "StatusUnknownBorderBrush", isLight ? "StatusUnknownBorderLightColor" : "StatusUnknownBorderDarkColor");

        // Status: Running
        SetBrush(resources, "StatusRunningForegroundBrush", isLight ? "StatusRunningForegroundLightColor" : "StatusRunningForegroundDarkColor");
        SetBrush(resources, "StatusRunningBackgroundBrush", isLight ? "StatusRunningBackgroundLightColor" : "StatusRunningBackgroundDarkColor");
        SetBrush(resources, "StatusRunningBorderBrush", isLight ? "StatusRunningBorderLightColor" : "StatusRunningBorderDarkColor");

        // Terminal
        SetBrush(resources, "TerminalBackgroundBrush", isLight ? "TerminalBackgroundLightColor" : "TerminalBackgroundDarkColor");
        SetBrush(resources, "TerminalForegroundBrush", isLight ? "TerminalForegroundLightColor" : "TerminalForegroundDarkColor");
    }

    private static void SetBrush(ResourceDictionary resources, string brushKey, string colorKey)
    {
        if (resources.Contains(colorKey) && resources[colorKey] is Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            resources[brushKey] = brush;
        }
    }
}
