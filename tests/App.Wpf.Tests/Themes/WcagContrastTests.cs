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
    // ── Dark: Blue-Slate Palette ──
    [InlineData("#F1F5F9", "#0A0E1A", 4.5)] // Dark: TextPrimary on AppBackground
    [InlineData("#F1F5F9", "#151C2C", 4.5)] // Dark: TextPrimary on CardBackground
    [InlineData("#F1F5F9", "#1E293B", 4.5)] // Dark: TextPrimary on ControlSecondary
    [InlineData("#94A3B8", "#0A0E1A", 4.5)] // Dark: TextSecondary on AppBackground
    [InlineData("#94A3B8", "#151C2C", 4.5)] // Dark: TextSecondary on CardBackground
    [InlineData("#94A3B8", "#1E293B", 3.0)] // Dark: TextSecondary on ControlSecondary (AA Large) — also covers Unknown status chip
    // ── Light: Cool Modern White Palette ──
    [InlineData("#0F172A", "#F8FAFC", 4.5)] // Light: TextPrimary on AppBackground
    [InlineData("#0F172A", "#FFFFFF", 4.5)] // Light: TextPrimary on CardBackground
    [InlineData("#0F172A", "#F1F5F9", 4.5)] // Light: TextPrimary on ControlSecondary
    [InlineData("#64748B", "#F8FAFC", 4.5)] // Light: TextSecondary on AppBackground
    [InlineData("#64748B", "#FFFFFF", 4.5)] // Light: TextSecondary on CardBackground
    [InlineData("#64748B", "#F1F5F9", 3.0)] // Light: TextSecondary on ControlSecondary (AA Large)
    // ── Terminal Readability ──
    [InlineData("#CBD5E1", "#070B14", 4.5)] // Dark: TerminalForeground on TerminalBackground
    [InlineData("#E2E8F0", "#1E293B", 4.5)] // Light: TerminalForeground on TerminalBackground
    // ── Status Chip Foreground on Status Background ──
    [InlineData("#34D399", "#052E16", 4.5)] // Dark: Valid foreground on valid background
    [InlineData("#FBBF24", "#451A03", 4.5)] // Dark: Expired foreground on expired background
    [InlineData("#FB7185", "#4C0519", 4.5)] // Dark: Error foreground on error background
    [InlineData("#38BDF8", "#0C2D57", 4.5)] // Dark: Running foreground on running background
    public void TokenContrast_SatisfiesWcagRequirements(string fgHex, string bgHex, double minimumRatio)
    {
        Color fg = (Color)ColorConverter.ConvertFromString(fgHex);
        Color bg = (Color)ColorConverter.ConvertFromString(bgHex);

        double ratio = ComputeContrastRatio(fg, bg);

        Assert.True(ratio >= minimumRatio,
            $"Contrast ratio between {fgHex} and {bgHex} was {ratio:F2}:1, which is below the minimum {minimumRatio}:1 required by WCAG.");
    }
}
