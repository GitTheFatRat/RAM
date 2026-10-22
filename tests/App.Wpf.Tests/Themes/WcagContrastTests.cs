namespace RbxManager.App.Tests.Themes;

using System;
using System.Windows.Media;
using Xunit;

public sealed class WcagContrastTests
{
    private static double RelativeLuminance(byte r, byte g, byte b)
    {
        double R = ConvertChannel(r);
        double G = ConvertChannel(g);
        double B = ConvertChannel(b);
        return 0.2126 * R + 0.7152 * G + 0.0722 * B;
    }

    private static double ConvertChannel(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double ComputeContrastRatio(Color c1, Color c2)
    {
        double l1 = RelativeLuminance(c1.R, c1.G, c1.B);
        double l2 = RelativeLuminance(c2.R, c2.G, c2.B);
        double brighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (brighter + 0.05) / (darker + 0.05);
    }

    [Theory]
    [InlineData("#FFFFFF", "#111111", 4.5)] // Dark: TextPrimary on AppBackground
    [InlineData("#FFFFFF", "#2D2D2D", 4.5)] // Dark: TextPrimary on CardBackground
    [InlineData("#FFFFFF", "#363636", 4.5)] // Dark: TextPrimary on ControlSecondary
    [InlineData("#CCCCCC", "#111111", 4.5)] // Dark: TextSecondary on AppBackground
    [InlineData("#CCCCCC", "#2D2D2D", 4.5)] // Dark: TextSecondary on CardBackground
    [InlineData("#CCCCCC", "#363636", 4.5)] // Dark: TextSecondary on ControlSecondary
    [InlineData("#161616", "#F4F4F4", 4.5)] // Light: TextPrimary on AppBackground
    [InlineData("#161616", "#FFFFFF", 4.5)] // Light: TextPrimary on CardBackground
    [InlineData("#161616", "#E8E8E8", 4.5)] // Light: TextPrimary on ControlSecondary
    [InlineData("#5C5C5C", "#F4F4F4", 4.5)] // Light: TextSecondary on AppBackground
    [InlineData("#5C5C5C", "#FFFFFF", 4.5)] // Light: TextSecondary on CardBackground
    [InlineData("#5C5C5C", "#E8E8E8", 4.5)] // Light: TextSecondary on ControlSecondary
    public void TokenContrast_SatisfiesWcagRequirements(string fgHex, string bgHex, double minimumRatio)
    {
        Color fg = (Color)ColorConverter.ConvertFromString(fgHex);
        Color bg = (Color)ColorConverter.ConvertFromString(bgHex);

        double ratio = ComputeContrastRatio(fg, bg);

        Assert.True(ratio >= minimumRatio,
            $"Contrast ratio between {fgHex} and {bgHex} was {ratio:F2}:1, which is below the minimum {minimumRatio}:1 required by WCAG.");
    }
}
