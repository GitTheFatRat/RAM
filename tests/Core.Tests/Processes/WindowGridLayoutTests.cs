namespace RbxManager.Core.Tests.Processes;

using System.Collections.Generic;
using RbxManager.Core.Processes;

public sealed class WindowGridLayoutTests
{
    // 1920x1080 primary monitor with a 40 px bottom taskbar.
    private static readonly ScreenArea WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void Slots_200x400_FillRowMajorWithoutOverlap()
    {
        Assert.Equal(9, WindowGridLayout.Columns(WorkArea, 200));
        Assert.Equal(2, WindowGridLayout.Rows(WorkArea, 400));

        List<(int X, int Y)> corners = [];
        for (int index = 0; index < WindowGridLayout.Capacity(WorkArea, 200, 400); index++)
        {
            GridSlot slot = WindowGridLayout.GetSlot(index, WorkArea, 200, 400);
            Assert.True(slot.X + 200 <= WorkArea.Width && slot.Y + 400 <= WorkArea.Height, $"slot {index} leaves the work area");
            foreach ((int x, int y) in corners)
            {
                bool overlaps = slot.X < x + 200 && x < slot.X + 200 && slot.Y < y + 400 && y < slot.Y + 400;
                Assert.False(overlaps, $"slot {index} at ({slot.X},{slot.Y}) overlaps ({x},{y})");
            }

            corners.Add((slot.X, slot.Y));
        }

        Assert.Equal(new GridSlot(0, 0), WindowGridLayout.GetSlot(0, WorkArea, 200, 400));
        Assert.Equal(new GridSlot(200, 0), WindowGridLayout.GetSlot(1, WorkArea, 200, 400));
        Assert.Equal(new GridSlot(0, 400), WindowGridLayout.GetSlot(9, WorkArea, 200, 400));
    }

    [Fact]
    public void GetSlot_PastCapacity_WrapsToFirstSlot()
    {
        int capacity = WindowGridLayout.Capacity(WorkArea, 800, 600);

        Assert.Equal(2, capacity);
        Assert.Equal(WindowGridLayout.GetSlot(0, WorkArea, 800, 600), WindowGridLayout.GetSlot(capacity, WorkArea, 800, 600));
    }

    [Fact]
    public void GetSlot_HonoursWorkAreaOffset()
    {
        ScreenArea leftTaskbar = new(60, 0, 1860, 1080);

        Assert.Equal(new GridSlot(60, 0), WindowGridLayout.GetSlot(0, leftTaskbar, 300, 300));
    }

    [Fact]
    public void WindowLargerThanScreen_StillGetsOneSlot()
    {
        Assert.Equal(1, WindowGridLayout.Capacity(WorkArea, 4000, 3000));
        Assert.Equal(new GridSlot(0, 0), WindowGridLayout.GetSlot(5, WorkArea, 4000, 3000));
    }

    [Fact]
    public void NextFreeSlot_ReusesLowestGap()
    {
        Assert.Equal(0, WindowGridLayout.NextFreeSlot([]));
        Assert.Equal(3, WindowGridLayout.NextFreeSlot([0, 1, 2]));
        Assert.Equal(1, WindowGridLayout.NextFreeSlot([0, 2, 3]));
    }
}
