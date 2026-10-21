namespace RbxManager.Infrastructure.Instance;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using RbxManager.Core.Common;
using RbxManager.Core.Instance;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed class NamedMutexInstanceGate : IInstanceGate, IDisposable
{
    private readonly IProcessProvider _processProvider;
    private readonly string? _mutexName;
    private Mutex? _mutex;
    private bool _isEnabled;
    private bool _disposed;

    public bool IsAvailable => !string.IsNullOrEmpty(_mutexName);

    public bool IsMultiInstanceEnabled => _isEnabled;

    public NamedMutexInstanceGate(IProcessProvider processProvider, string? mutexName = null)
    {
        _processProvider = processProvider;
        _mutexName = mutexName;
    }

    public Result TryEnable()
    {
        if (string.IsNullOrEmpty(_mutexName))
        {
            return Result.Failure("Tính năng chạy nhiều tài khoản không khả dụng trong bản dựng này.", "MultiInstanceNotAvailable");
        }

        IReadOnlyList<ProcessSnapshot> running = _processProvider.GetRobloxProcesses();
        if (running.Count > 0)
        {
            return Result.Failure("Roblox đang chạy; vui lòng đóng tất cả tiến trình Roblox trước khi bật.", "RobloxRunning");
        }

        try
        {
            _mutex = new Mutex(initiallyOwned: true, _mutexName, out _);
            _isEnabled = true;
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Không thể kích hoạt đối tượng đồng bộ hóa: {ex.Message}", "MutexError");
        }
    }

    public void Disable()
    {
        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if mutex was not acquired or already released
            }

            _mutex.Dispose();
            _mutex = null;
        }

        _isEnabled = false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Disable();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
