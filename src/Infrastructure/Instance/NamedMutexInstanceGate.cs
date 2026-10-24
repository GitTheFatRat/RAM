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
    public const string DefaultMutexName = "ROBLOX_singletonMutex";
    public const string DefaultEventName = "ROBLOX_singletonEvent";

    private readonly IProcessProvider _processProvider;
    private readonly string? _mutexName;
    private readonly string? _eventName;

    private readonly object _syncLock = new();
    private readonly ManualResetEventSlim _commandReady = new(false);
    private readonly ManualResetEventSlim _commandDone = new(false);
    private readonly Thread _workerThread;

    private enum GateCommand
    {
        None,
        Enable,
        Disable,
        Shutdown
    }

    private GateCommand _pendingCommand = GateCommand.None;
    private Result _commandResult = Result.Success();

    private volatile bool _isEnabled;
    private bool _disposed;

    public bool IsAvailable => !string.IsNullOrEmpty(_mutexName) && !string.IsNullOrEmpty(_eventName);

    public bool IsMultiInstanceEnabled => _isEnabled;

    public NamedMutexInstanceGate(IProcessProvider processProvider)
        : this(processProvider, DefaultMutexName, DefaultEventName)
    {
    }

    public NamedMutexInstanceGate(
        IProcessProvider processProvider,
        string? mutexName,
        string? eventName = DefaultEventName)
    {
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _mutexName = mutexName;
        _eventName = eventName;

        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "RbxManager_InstanceGateWorker"
        };
        _workerThread.Start();
    }

    public Result TryEnable()
    {
        if (!IsAvailable)
        {
            return Result.Failure("MultiInstanceNotAvailable", "MultiInstanceNotAvailable");
        }

        IReadOnlyList<ProcessSnapshot> running = _processProvider.GetRobloxProcesses();
        if (running.Count > 0)
        {
            return Result.Failure("RobloxRunning", "RobloxRunning");
        }

        lock (_syncLock)
        {
            if (_disposed)
            {
                return Result.Failure("Disposed", "Disposed");
            }

            if (_isEnabled)
            {
                return Result.Success();
            }

            _pendingCommand = GateCommand.Enable;
            _commandDone.Reset();
            _commandReady.Set();
            if (!_commandDone.Wait(TimeSpan.FromSeconds(5)))
            {
                return Result.Failure("Timeout", "Timeout");
            }

            return _commandResult;
        }
    }

    public void Disable()
    {
        lock (_syncLock)
        {
            if (_disposed || !_isEnabled)
            {
                return;
            }

            _pendingCommand = GateCommand.Disable;
            _commandDone.Reset();
            _commandReady.Set();
            _commandDone.Wait(TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingCommand = GateCommand.Shutdown;
            _commandDone.Reset();
            _commandReady.Set();
            _commandDone.Wait(TimeSpan.FromSeconds(5));
        }

        if (_workerThread.IsAlive)
        {
            _workerThread.Join(TimeSpan.FromSeconds(5));
        }

        _commandReady.Dispose();
        _commandDone.Dispose();
        GC.SuppressFinalize(this);
    }

    private void WorkerLoop()
    {
        Mutex? mutex = null;
        Mutex? eventMutex = null;

        while (true)
        {
            _commandReady.Wait();
            _commandReady.Reset();

            GateCommand cmd = _pendingCommand;
            _pendingCommand = GateCommand.None;

            if (cmd == GateCommand.Enable)
            {
                try
                {
                    mutex = new Mutex(initiallyOwned: true, _mutexName, out bool mutexCreatedNew);
                    if (!mutexCreatedNew)
                    {
                        CleanupHandles(ref mutex, ref eventMutex);
                        _isEnabled = false;
                        _commandResult = Result.Failure("CouldNotTakeOver", "CouldNotTakeOver");
                    }
                    else
                    {
                        // Lock ROBLOX_singletonEvent as a Mutex so RobloxPlayerBeta.exe's CreateEventW
                        // fails with ERROR_INVALID_HANDLE and cannot signal existing clients to exit.
                        eventMutex = new Mutex(initiallyOwned: true, _eventName, out bool eventCreatedNew);

                        if (!eventCreatedNew)
                        {
                            CleanupHandles(ref mutex, ref eventMutex);
                            _isEnabled = false;
                            _commandResult = Result.Failure("CouldNotTakeOver", "CouldNotTakeOver");
                        }
                        else
                        {
                            _isEnabled = true;
                            _commandResult = Result.Success();
                        }
                    }
                }
                catch (Exception)
                {
                    CleanupHandles(ref mutex, ref eventMutex);
                    _isEnabled = false;
                    _commandResult = Result.Failure("MutexError", "MutexError");
                }

                _commandDone.Set();
            }
            else if (cmd == GateCommand.Disable)
            {
                CleanupHandles(ref mutex, ref eventMutex);
                _isEnabled = false;
                _commandResult = Result.Success();
                _commandDone.Set();
            }
            else if (cmd == GateCommand.Shutdown)
            {
                CleanupHandles(ref mutex, ref eventMutex);
                _isEnabled = false;
                _commandDone.Set();
                break;
            }
        }
    }

    private static void CleanupHandles(ref Mutex? mutex, ref Mutex? eventMutex)
    {
        ReleaseAndDisposeMutex(ref mutex);
        ReleaseAndDisposeMutex(ref eventMutex);
    }

    private static void ReleaseAndDisposeMutex(ref Mutex? mutex)
    {
        if (mutex != null)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch
            {
            }

            try
            {
                mutex.Dispose();
            }
            catch
            {
            }

            mutex = null;
        }
    }
}
