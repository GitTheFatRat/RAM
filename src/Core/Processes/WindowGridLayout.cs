namespace RbxManager.Core.Processes;

using System;
using System.Collections.Generic;
using System.Linq;

public readonly record struct ScreenArea(int Left, int Top, int Width, int Height);

public readonly record struct GridSlot(int X, int Y);

/// <summary>Row-major grid of equally sized windows inside a screen work area.</summary>
public static class WindowGridLayout
{
    public static int Columns(ScreenArea area, int windowWidth) => Math.Max(1, area.Width / Math.Max(1, windowWidth));

    public static int Rows(ScreenArea area, int windowHeight) => Math.Max(1, area.Height / Math.Max(1, windowHeight));

    public static int Capacity(ScreenArea area, int windowWidth, int windowHeight) =>
        Columns(area, windowWidth) * Rows(area, windowHeight);

    /// <summary>
    /// Top-left corner of grid slot <paramref name="index"/>. Indices past <see cref="Capacity"/> wrap to the
    /// first slot again: once the screen is full, stacking on an existing tile beats placing windows off-screen.
    /// </summary>
    public static GridSlot GetSlot(int index, ScreenArea area, int windowWidth, int windowHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int columns = Columns(area, windowWidth);
        int slot = index % Capacity(area, windowWidth, windowHeight);
        return new GridSlot(
            area.Left + (slot % columns * windowWidth),
            area.Top + (slot / columns * windowHeight));
    }

    /// <summary>Lowest slot index not in <paramref name="occupiedSlots"/>, so closed windows free their tile for reuse.</summary>
    public static int NextFreeSlot(IReadOnlyCollection<int> occupiedSlots)
    {
        int candidate = 0;
        while (occupiedSlots.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }
}
