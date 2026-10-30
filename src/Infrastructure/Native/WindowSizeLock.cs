namespace RbxManager.Infrastructure.Native;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Processes;

/// <summary>
/// Keeps an external process's top-level window at a fixed size without touching its position.
/// Windows' modal move loop re-applies the target's ptMinTrackSize (Roblox: 800x600) whenever a drag
/// starts, which cannot be prevented from outside the process without DLL injection. Instead we listen
/// with an out-of-context WinEvent hook (no code runs inside Roblox) and restore the size once the
/// move/size loop ends, or whenever the window changes size outside of a drag. Roblox can recreate its
/// main window (app shell to game, teleports), so the lock follows the process, not a single handle.
/// </summary>
/// <remarks>
/// Out-of-context WinEvent callbacks are delivered through the installing thread's message queue,
/// so <see cref="Attach"/> must be called on a thread that pumps messages (the WPF UI thread).
/// </remarks>
public sealed class WindowSizeLock : IDisposable
{
    internal const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    internal const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint GA_ROOT = 2;
    private const uint GW_OWNER = 4;
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;
    private const long WS_POPUP = 0x80000000L;
    private const int CorrectionBurstWindowMs = 1000;
    private const int MaxCorrectionsPerBurst = 8;

    // Rooted here so neither the lock nor its native callback delegate can be collected while hooked.
    private static readonly ConcurrentDictionary<uint, WindowSizeLock> ActiveLocks = new();

    private readonly uint _processId;
    private readonly int _width;
    private readonly int _height;
    private readonly GridSlot? _tilePosition;
    private readonly Func<(int Left, int Top, int Right, int Bottom)>? _getBoundsOverride;
    private readonly WinEventProc _callback;
    private readonly IntPtr[] _hooks;
    private readonly SynchronizationContext? _ownerContext;
    private IntPtr _hWnd;
    private bool _isUserDragging;
    private bool _isApplying;
    private bool _pendingTileMove;
    private long _burstStartedAt;
    private int _burstCorrections;
    private int _trailingScheduled;
    private int _disposed;

    internal WindowSizeLock(
        IntPtr hWnd,
        int width,
        int height,
        GridSlot? tilePosition = null,
        Func<(int Left, int Top, int Right, int Bottom)>? getBoundsOverride = null)
    {
        _hWnd = hWnd;
        _width = width;
        _height = height;
        _tilePosition = tilePosition;
        _getBoundsOverride = getBoundsOverride;
        _callback = OnWinEvent;
        _hooks = [];
    }

    private WindowSizeLock(IntPtr hWnd, uint processId, int width, int height, GridSlot? tilePosition)
    {
        _hWnd = hWnd;
        _processId = processId;
        _width = width;
        _height = height;
        _tilePosition = tilePosition;
        _callback = OnWinEvent;
        _ownerContext = SynchronizationContext.Current;
        _hooks =
        [
            SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
        ];
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime);

    /// <param name="tilePosition">Auto-tile slot; when set, recreated main windows are moved back to it.</param>
    public static bool Attach(IntPtr hWnd, int width, int height, GridSlot? tilePosition = null)
    {
        if (hWnd == IntPtr.Zero || width <= 0 || height <= 0 || GetWindowThreadProcessId(hWnd, out uint processId) == 0)
        {
            return false;
        }

        if (ActiveLocks.TryRemove(processId, out WindowSizeLock? previous))
        {
            previous.Dispose();
        }

        WindowSizeLock sizeLock = new(hWnd, processId, width, height, tilePosition);
        if (Array.TrueForAll(sizeLock._hooks, hook => hook == IntPtr.Zero))
        {
            return false;
        }

        ActiveLocks[processId] = sizeLock;
        sizeLock.DisposeWhenProcessExits();
        sizeLock.EnforceSize();
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ActiveLocks.TryRemove(new(_processId, this));
        if (_ownerContext is null || SynchronizationContext.Current == _ownerContext)
        {
            Unhook();
        }
        else
        {
            _ownerContext.Post(_ => Unhook(), null);
        }
    }

    private static bool IsMainWindowCandidate(IntPtr hWnd)
    {
        if (GetAncestor(hWnd, GA_ROOT) != hWnd || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero || !IsWindowVisible(hWnd))
        {
            return false;
        }

        long style = GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64();
        if ((style & WS_CAPTION) == WS_CAPTION)
        {
            return true;
        }

        return (style & WS_POPUP) != 0 &&
               GetWindowRect(hWnd, out Rect bounds) &&
               bounds.Right - bounds.Left >= 200 &&
               bounds.Bottom - bounds.Top >= 200;
    }

    private void DisposeWhenProcessExits()
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById((int)_processId);
            _ = process.WaitForExitAsync().ContinueWith(
                _ =>
                {
                    process.Dispose();
                    Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception)
        {
            process?.Dispose();
            Dispose();
        }
    }

    private void Unhook()
    {
        foreach (IntPtr hook in _hooks)
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook);
            }
        }
    }

    internal void OnWinEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != OBJID_WINDOW || _disposed != 0)
        {
            return;
        }

        if (hWnd != _hWnd &&
            (eventType == EVENT_OBJECT_SHOW ||
             ((eventType is EVENT_OBJECT_LOCATIONCHANGE or EVENT_SYSTEM_MOVESIZEEND) && !IsWindowVisible(_hWnd))) &&
            IsMainWindowCandidate(hWnd))
        {
            _hWnd = hWnd;
            _isUserDragging = false;
            if (User32.IsIconicOverride?.Invoke(hWnd) ?? User32.IsIconic(hWnd))
            {
                // Never un-minimize behind the user's back: adopt the handle and finish placement on MINIMIZEEND.
                _pendingTileMove = _tilePosition.HasValue;
                return;
            }

            if (_tilePosition is GridSlot tile)
            {
                // A recreated window (app shell -> game, teleport) spawns centered again; put it back in its tile.
                MoveToTile(tile);
            }
            else
            {
                EnforceSize();
            }

            return;
        }

        if (hWnd != _hWnd)
        {
            return;
        }

        switch (eventType)
        {
            case EVENT_SYSTEM_MOVESIZESTART:
                _isUserDragging = true;
                break;
            case EVENT_SYSTEM_MOVESIZEEND:
                _isUserDragging = false;
                EnforceSize();
                break;
            case EVENT_SYSTEM_MINIMIZESTART:
                // The user (or the throttler) is minimizing: restoring here would bounce the window straight back.
                break;
            case EVENT_SYSTEM_MINIMIZEEND:
            case EVENT_OBJECT_LOCATIONCHANGE when !_isUserDragging:
                // Fighting the move loop mid-drag only causes flicker; correct after release instead.
                // A still-minimized window is parked at (-32000, -32000); resizing it would corrupt its restore rect.
                if (!(User32.IsIconicOverride?.Invoke(hWnd) ?? User32.IsIconic(hWnd)))
                {
                    if (_pendingTileMove && _tilePosition is GridSlot pendingTile)
                    {
                        _pendingTileMove = false;
                        MoveToTile(pendingTile);
                    }
                    else
                    {
                        EnforceSize();
                    }
                }

                break;
        }
    }

    private void MoveToTile(GridSlot tile)
    {
        _isApplying = true;
        try
        {
            User32.MoveResizeWindow(_hWnd, tile.X, tile.Y, _width, _height);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void EnforceSize()
    {
        // Single choke point for every caller (Attach, trailing burst, MOVESIZEEND, recreated window):
        // a minimized window sits at (-32000, -32000) and must never be restored or resized from here.
        bool isMinimized = User32.IsIconicOverride?.Invoke(_hWnd) ?? User32.IsIconic(_hWnd);
        if (_isApplying || isMinimized)
        {
            return;
        }

        bool isMaximized = User32.IsZoomedOverride?.Invoke(_hWnd) ?? User32.IsZoomed(_hWnd);
        Rect bounds;
        bool hasBounds;
        if (_getBoundsOverride is not null)
        {
            (int left, int top, int right, int bottom) = _getBoundsOverride();
            bounds = new Rect { Left = left, Top = top, Right = right, Bottom = bottom };
            hasBounds = true;
        }
        else
        {
            hasBounds = GetWindowRect(_hWnd, out bounds);
        }

        if (!isMaximized && hasBounds &&
            bounds.Right - bounds.Left == _width && bounds.Bottom - bounds.Top == _height)
        {
            return;
        }

        // If the client keeps resizing itself back, stop after a short burst instead of ping-ponging forever.
        long now = Environment.TickCount64;
        if (now - _burstStartedAt > CorrectionBurstWindowMs)
        {
            _burstStartedAt = now;
            _burstCorrections = 0;
        }

        if (++_burstCorrections > MaxCorrectionsPerBurst)
        {
            ScheduleTrailingEnforcement();
            return;
        }

        _isApplying = true;
        try
        {
            User32.ResizeWindow(_hWnd, _width, _height);
        }
        finally
        {
            _isApplying = false;
        }
    }

    private void ScheduleTrailingEnforcement()
    {
        if (Interlocked.CompareExchange(ref _trailingScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(CorrectionBurstWindowMs + 50).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _trailingScheduled, 0);
            }

            if (_disposed != 0)
            {
                return;
            }

            if (_ownerContext is not null)
            {
                _ownerContext.Post(_ =>
                {
                    if (_disposed == 0 && !_isUserDragging)
                    {
                        EnforceSize();
                    }
                }, null);
            }
            else if (!_isUserDragging)
            {
                EnforceSize();
            }
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc callback, uint idProcess, uint idThread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
}
