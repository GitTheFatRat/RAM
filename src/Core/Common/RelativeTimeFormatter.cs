namespace RbxManager.Core.Common;

using System;
using System.Globalization;

public static class RelativeTimeFormatter
{
    public static string Format(DateTimeOffset timestamp, DateTimeOffset now, string? cultureName = null)
    {
        string culture = cultureName ?? CultureInfo.CurrentUICulture.Name;
        bool isVi = culture.StartsWith("vi", StringComparison.OrdinalIgnoreCase);

        TimeSpan elapsed = now - timestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalSeconds < 60)
        {
            int seconds = Math.Max(0, (int)elapsed.TotalSeconds);
            if (seconds <= 5)
            {
                return isVi ? "v\u1EEBa xong" : "just now";
            }

            if (isVi)
            {
                return $"{seconds} gi\u00E2y tr\u01B0\u1EDBc";
            }

            return seconds == 1 ? "1 second ago" : $"{seconds} seconds ago";
        }

        if (elapsed.TotalMinutes < 60)
        {
            int minutes = Math.Max(1, (int)elapsed.TotalMinutes);
            if (isVi)
            {
                return $"{minutes} ph\u00FAt tr\u01B0\u1EDBc";
            }

            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed.TotalHours < 24)
        {
            int hours = Math.Max(1, (int)elapsed.TotalHours);
            if (isVi)
            {
                return $"{hours} gi\u1EDD tr\u01B0\u1EDBc";
            }

            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        int days = Math.Max(1, (int)elapsed.TotalDays);
        if (isVi)
        {
            return $"{days} ng\u00E0y tr\u01B0\u1EDBc";
        }

        return days == 1 ? "1 day ago" : $"{days} days ago";
    }
}
