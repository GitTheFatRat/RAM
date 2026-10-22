namespace RbxManager.Infrastructure.Processes;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsMemoryOptimizer : IMemoryOptimizer
{
    private const uint ProcessSetQuota = 0x0100;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint DesiredAccess = ProcessSetQuota | ProcessQueryInformation;

    private readonly IProcessProvider _processProvider;
    private readonly ILogger<WindowsMemoryOptimizer> _logger;
    private readonly Func<int, bool>? _customTrimProcess;

    public int LastTrimmedCount { get; private set; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    public WindowsMemoryOptimizer(
        IProcessProvider processProvider,
        ILogger<WindowsMemoryOptimizer>? logger = null)
        : this(processProvider, null, logger)
    {
    }

    internal WindowsMemoryOptimizer(
        IProcessProvider processProvider,
        Func<int, bool>? customTrimProcess,
        ILogger<WindowsMemoryOptimizer>? logger = null)
    {
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _customTrimProcess = customTrimProcess;
        _logger = logger ?? NullLogger<WindowsMemoryOptimizer>.Instance;
    }

    public Task TrimMemoryAsync(CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        try
        {
            IReadOnlyList<ProcessSnapshot> processes = _processProvider.GetRobloxProcesses();
            int trimmed = 0;

            foreach (ProcessSnapshot process in processes)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                if (TrimProcessWorkingSet(process.Pid))
                {
                    trimmed++;
                }
            }

            LastTrimmedCount = trimmed;
            LogTrimCompleted(_logger, trimmed, processes.Count);
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

    private bool TrimProcessWorkingSet(int pid)
    {
        if (_customTrimProcess != null)
        {
            return _customTrimProcess(pid);
        }

        try
        {
            using SafeProcessHandle handle = OpenProcess(DesiredAccess, false, pid);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                // 5 = ERROR_ACCESS_DENIED, 87 = ERROR_INVALID_PARAMETER (process already terminated)
                LogProcessOpenWarning(_logger, pid, error);
                return false;
            }

            bool success = SetProcessWorkingSetSize(handle.DangerousGetHandle(), -1, -1);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                LogSetWorkingSetWarning(_logger, pid, error);
                return false;
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            LogProcessWarning(_logger, pid, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            LogProcessWarning(_logger, pid, ex.Message);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Memory trim completed: trimmed {TrimmedCount} of {TotalCount} Roblox processes.")]
    private static partial void LogTrimCompleted(ILogger logger, int trimmedCount, int totalCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Failed to open process {Pid} for working set trimming. Error: {Error}.")]
    private static partial void LogProcessOpenWarning(ILogger logger, int pid, int error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Failed to set working set size for process {Pid}. Error: {Error}.")]
    private static partial void LogSetWorkingSetWarning(ILogger logger, int pid, int error);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Error trimming process {Pid}: {Message}.")]
    private static partial void LogProcessWarning(ILogger logger, int pid, string message);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Unexpected error during memory trim.")]
    private static partial void LogTrimFailed(ILogger logger, Exception ex);
}
