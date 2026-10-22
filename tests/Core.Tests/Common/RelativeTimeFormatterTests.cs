namespace RbxManager.Core.Tests.Common;

using System;
using RbxManager.Core.Common;
using Xunit;

public sealed class RelativeTimeFormatterTests
{
    [Fact]
    public void Format_Vietnamese_Boundaries()
    {
        DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        // Just now
        Assert.Equal("vừa xong", RelativeTimeFormatter.Format(now, now, "vi-VN"));
        Assert.Equal("vừa xong", RelativeTimeFormatter.Format(now.AddSeconds(-4), now, "vi-VN"));

        // Seconds
        Assert.Equal("15 giây trước", RelativeTimeFormatter.Format(now.AddSeconds(-15), now, "vi-VN"));
        Assert.Equal("59 giây trước", RelativeTimeFormatter.Format(now.AddSeconds(-59), now, "vi-VN"));

        // Minutes
        Assert.Equal("1 phút trước", RelativeTimeFormatter.Format(now.AddMinutes(-1), now, "vi-VN"));
        Assert.Equal("5 phút trước", RelativeTimeFormatter.Format(now.AddMinutes(-5), now, "vi-VN"));
        Assert.Equal("59 phút trước", RelativeTimeFormatter.Format(now.AddMinutes(-59), now, "vi-VN"));

        // Hours
        Assert.Equal("1 giờ trước", RelativeTimeFormatter.Format(now.AddHours(-1), now, "vi-VN"));
        Assert.Equal("3 giờ trước", RelativeTimeFormatter.Format(now.AddHours(-3), now, "vi-VN"));
        Assert.Equal("23 giờ trước", RelativeTimeFormatter.Format(now.AddHours(-23), now, "vi-VN"));

        // Days
        Assert.Equal("1 ngày trước", RelativeTimeFormatter.Format(now.AddDays(-1), now, "vi-VN"));
        Assert.Equal("7 ngày trước", RelativeTimeFormatter.Format(now.AddDays(-7), now, "vi-VN"));
    }

    [Fact]
    public void Format_English_Boundaries()
    {
        DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        // Just now
        Assert.Equal("just now", RelativeTimeFormatter.Format(now, now, "en-US"));
        Assert.Equal("just now", RelativeTimeFormatter.Format(now.AddSeconds(-3), now, "en-US"));

        // Seconds
        Assert.Equal("20 seconds ago", RelativeTimeFormatter.Format(now.AddSeconds(-20), now, "en-US"));

        // Minutes
        Assert.Equal("1 minute ago", RelativeTimeFormatter.Format(now.AddMinutes(-1), now, "en-US"));
        Assert.Equal("5 minutes ago", RelativeTimeFormatter.Format(now.AddMinutes(-5), now, "en-US"));

        // Hours
        Assert.Equal("1 hour ago", RelativeTimeFormatter.Format(now.AddHours(-1), now, "en-US"));
        Assert.Equal("4 hours ago", RelativeTimeFormatter.Format(now.AddHours(-4), now, "en-US"));

        // Days
        Assert.Equal("1 day ago", RelativeTimeFormatter.Format(now.AddDays(-1), now, "en-US"));
        Assert.Equal("10 days ago", RelativeTimeFormatter.Format(now.AddDays(-10), now, "en-US"));
    }

    [Fact]
    public void Format_FutureTimestamp_ReturnsJustNow()
    {
        DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("vừa xong", RelativeTimeFormatter.Format(now.AddMinutes(5), now, "vi-VN"));
        Assert.Equal("just now", RelativeTimeFormatter.Format(now.AddMinutes(5), now, "en-US"));
    }
}
