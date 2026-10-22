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
                return isVi ? "vừa xong" : "just now";
            }

            if (isVi)
            {
                return $"{seconds} giây trước";
            }

            return seconds == 1 ? "1 second ago" : $"{seconds} seconds ago";
        }

        if (elapsed.TotalMinutes < 60)
        {
            int minutes = Math.Max(1, (int)elapsed.TotalMinutes);
            if (isVi)
            {
                return $"{minutes} phút trước";
            }

            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        if (elapsed.TotalHours < 24)
        {
            int hours = Math.Max(1, (int)elapsed.TotalHours);
            if (isVi)
            {
                return $"{hours} giờ trước";
            }

            return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
        }

        int days = Math.Max(1, (int)elapsed.TotalDays);
        if (isVi)
        {
            return $"{days} ngày trước";
        }

        return days == 1 ? "1 day ago" : $"{days} days ago";
    }
}
