namespace RbxManager.App.Tests;

using System.Runtime.InteropServices;
using RbxManager.App.Services;
using RbxManager.Infrastructure.Native;
using Xunit;

/// <summary>M44 R3 real-data: classification uses this machine's real monitor layout (MonitorFromRect).</summary>
public sealed class OffScreenClassificationRealDataTests
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;

    [Fact]
    public void ParkedMinimizedCoordinates_AreOffScreen()
    {
        Assert.True(RobloxWindowSizer.IsOffScreenRect(-32000, -32000, 160, 28));
    }

    [Fact]
    public void RectInsidePrimaryWorkArea_IsOnScreen()
    {
        Core.Processes.ScreenArea workArea = User32.GetPrimaryWorkArea();
        Assert.False(RobloxWindowSizer.IsOffScreenRect(workArea.Left + 10, workArea.Top + 10, 200, 400));
    }

    [Fact]
    public void RectLeftOfEveryMonitor_IsOffScreen_ButNotTreatedAsParked()
    {
        int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int left = virtualLeft - 4000;
        Assert.InRange(left, -9999, -1001);

        Assert.True(RobloxWindowSizer.IsOffScreenRect(left, virtualTop + 10, 200, 400));
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
