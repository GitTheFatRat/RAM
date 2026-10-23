namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsMemoryOptimizer : IMemoryOptimizer
{
    // Minimal rights required by SetProcessWorkingSetSize: PROCESS_SET_QUOTA only
    private const uint ProcessSetQuota = 0x0100;

    private readonly IProcessProvider? _processProvider;
    private readonly INativeProcessMemoryMethods _nativeMemoryMethods;
    private readonly INativeWindowMethods _nativeWindowMethods;
    private readonly ILogger<WindowsMemoryOptimizer> _logger;
    private readonly IUatLogger? _uatLogger;
    private readonly Func<int, bool>? _customTrimProcess;

    public int LastTrimmedCount { get; private set; }
    public int LastAccessDeniedCount { get; private set; }
    public int LastFailedCount { get; private set; }

    public WindowsMemoryOptimizer(
        IProcessProvider? processProvider = null,
        INativeProcessMemoryMethods? nativeMemoryMethods = null,
        INativeWindowMethods? nativeWindowMethods = null,
        ILogger<WindowsMemoryOptimizer>? logger = null,
        IUatLogger? uatLogger = null)
        : this(processProvider, nativeMemoryMethods, nativeWindowMethods, null, logger, uatLogger)
    {
    }

    internal WindowsMemoryOptimizer(
        IProcessProvider? processProvider,
        INativeProcessMemoryMethods? nativeMemoryMethods,
        INativeWindowMethods? nativeWindowMethods,
        Func<int, bool>? customTrimProcess,
        ILogger<WindowsMemoryOptimizer>? logger = null,
        IUatLogger? uatLogger = null)
    {
        _processProvider = processProvider;
        _nativeMemoryMethods = nativeMemoryMethods ?? new WindowsNativeProcessMemoryMethods();
        _nativeWindowMethods = nativeWindowMethods ?? new WindowsNativeWindowMethods();
        _customTrimProcess = customTrimProcess;
        _logger = logger ?? NullLogger<WindowsMemoryOptimizer>.Instance;
        _uatLogger = uatLogger;
    }

    public Task TrimMemoryAsync(IReadOnlyCollection<int>? trackedPids = null, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        IReadOnlyCollection<int>? pidsToTrim = trackedPids;
        if ((pidsToTrim == null || pidsToTrim.Count == 0) && _processProvider != null)
        {
            pidsToTrim = _processProvider.GetRobloxProcesses().Select(p => p.Pid).ToList();
        }

        if (pidsToTrim == null || pidsToTrim.Count == 0)
        {
            LastTrimmedCount = 0;
            LastAccessDeniedCount = 0;
            LastFailedCount = 0;
            return Task.CompletedTask;
        }

        int trimmed = 0;
        int accessDenied = 0;
        int failed = 0;

        try
        {
            IntPtr foregroundHwnd = _nativeWindowMethods.GetForegroundWindow();
            uint foregroundPid = foregroundHwnd != IntPtr.Zero
                ? _nativeWindowMethods.GetWindowThreadProcessId(foregroundHwnd)
                : 0;

            foreach (int pid in pidsToTrim)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if ((uint)pid == foregroundPid)
                {
                    // Skip the foreground window's process
                    continue;
                }

                TrimResult result = TrimProcessWorkingSet(pid);
                switch (result)
                {
                    case TrimResult.Success:
                        trimmed++;
                        break;
                    case TrimResult.AccessDenied:
                        accessDenied++;
                        break;
                    case TrimResult.Failed:
                        failed++;
                        break;
                }
            }

            LastTrimmedCount = trimmed;
            LastAccessDeniedCount = accessDenied;
            LastFailedCount = failed;

            LogTrimCompleted(_logger, trimmed, accessDenied, failed, pidsToTrim.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogTrimFailed(_logger, ex);
        }

        return Task.CompletedTask;
    }

    private TrimResult TrimProcessWorkingSet(int pid)
    {
        if (_customTrimProcess != null)
        {
            _uatLogger?.LogTrace(string.Create(CultureInfo.InvariantCulture, $"WindowsMemoryOptimizer: Calling SetProcessWorkingSetSize(-1, -1) on PID {pid}"));
            return _customTrimProcess(pid) ? TrimResult.Success : TrimResult.Failed;
        }

        try
        {
            using SafeProcessHandle handle = _nativeMemoryMethods.OpenProcess(ProcessSetQuota, false, pid);
            if (handle.IsInvalid)
            {
                int error = _nativeMemoryMethods.GetLastWin32Error();
                LogProcessOpenWarning(_logger, pid, error);
                return error == 5 ? TrimResult.AccessDenied : TrimResult.Failed;
            }

            _uatLogger?.LogTrace(string.Create(CultureInfo.InvariantCulture, $"WindowsMemoryOptimizer: Calling SetProcessWorkingSetSize(-1, -1) on PID {pid}"));
            bool success = _nativeMemoryMethods.SetProcessWorkingSetSize(handle.DangerousGetHandle(), -1, -1);
            if (!success)
            {
                int error = _nativeMemoryMethods.GetLastWin32Error();
                LogSetWorkingSetWarning(_logger, pid, error);
                return error == 5 ? TrimResult.AccessDenied : TrimResult.Failed;
            }

            return TrimResult.Success;
        }
        catch (Exception ex)
        {
            LogProcessWarning(_logger, pid, ex.Message);
            return TrimResult.Failed;
        }
    }

    public enum TrimResult
    {
        Success,
        AccessDenied,
        Failed
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Memory trim completed: trimmed {Trimmed}, access denied {AccessDenied}, failed {Failed} of {Total} tracked processes.")]
    private static partial void LogTrimCompleted(ILogger logger, int trimmed, int accessDenied, int failed, int total);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Failed to open process {Pid} for working set trimming. Error: {Error}.")]
    private static partial void LogProcessOpenWarning(ILogger logger, int pid, int error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Failed to set working set size for process {Pid}. Error: {Error}.")]
    private static partial void LogSetWorkingSetWarning(ILogger logger, int pid, int error);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Error trimming process {Pid}: {Message}.")]
    private static partial void LogProcessWarning(ILogger logger, int pid, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Unexpected error during memory trim.")]
    private static partial void LogTrimFailed(ILogger logger, Exception ex);
}
