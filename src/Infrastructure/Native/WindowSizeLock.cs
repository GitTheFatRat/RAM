namespace RbxManager.Infrastructure.Native;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const int OBJID_WINDOW = 0;
    private const uint GA_ROOT = 2;
    private const uint GW_OWNER = 4;
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;
    private const int CorrectionBurstWindowMs = 1000;
    private const int MaxCorrectionsPerBurst = 8;

    // Rooted here so neither the lock nor its native callback delegate can be collected while hooked.
    private static readonly ConcurrentDictionary<uint, WindowSizeLock> ActiveLocks = new();

    private readonly uint _processId;
    private readonly int _width;
    private readonly int _height;
    private readonly WinEventProc _callback;
    private readonly IntPtr[] _hooks;
    private readonly SynchronizationContext? _ownerContext;
    private IntPtr _hWnd;
    private bool _isUserDragging;
    private bool _isApplying;
    private long _burstStartedAt;
    private int _burstCorrections;
    private int _disposed;

    private WindowSizeLock(IntPtr hWnd, uint processId, int width, int height)
    {
        _hWnd = hWnd;
        _processId = processId;
        _width = width;
        _height = height;
        _callback = OnWinEvent;
        _ownerContext = SynchronizationContext.Current;
        _hooks =
        [
            SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _callback, processId, 0, WINEVENT_OUTOFCONTEXT),
        ];
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime);

    public static bool Attach(IntPtr hWnd, int width, int height)
    {
        if (hWnd == IntPtr.Zero || width <= 0 || height <= 0 || GetWindowThreadProcessId(hWnd, out uint processId) == 0)
        {
            return false;
        }

        if (ActiveLocks.TryRemove(processId, out WindowSizeLock? previous))
        {
            previous.Dispose();
        }

        WindowSizeLock sizeLock = new(hWnd, processId, width, height);
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

    private static bool IsMainWindowCandidate(IntPtr hWnd) =>
        GetAncestor(hWnd, GA_ROOT) == hWnd &&
        GetWindow(hWnd, GW_OWNER) == IntPtr.Zero &&
        IsWindowVisible(hWnd) &&
        (GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION;

    private void DisposeWhenProcessExits()
    {
        try
        {
            Process process = Process.GetProcessById((int)_processId);
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
        catch (ArgumentException)
        {
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

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hWnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (idObject != OBJID_WINDOW || _disposed != 0)
        {
            return;
        }

        if (eventType == EVENT_OBJECT_SHOW && hWnd != _hWnd && IsMainWindowCandidate(hWnd))
        {
            _hWnd = hWnd;
            _isUserDragging = false;
            EnforceSize();
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
            case EVENT_OBJECT_LOCATIONCHANGE when !_isUserDragging:
                // Fighting the move loop mid-drag only causes flicker; correct after release instead.
                EnforceSize();
                break;
        }
    }

    private void EnforceSize()
    {
        if (_isApplying || IsIconic(_hWnd) || IsZoomed(_hWnd) || !GetWindowRect(_hWnd, out Rect bounds))
        {
            return;
        }

        if (bounds.Right - bounds.Left == _width && bounds.Bottom - bounds.Top == _height)
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);
}
