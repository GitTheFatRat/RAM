namespace RbxManager.Infrastructure.Instance;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using RbxManager.Core.Common;
using RbxManager.Core.Instance;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed class NamedMutexInstanceGate : IInstanceGate, IDisposable
{
    public const string DefaultMutexName = "ROBLOX_singletonMutex";
    public const string DefaultEventName = @"Global\ROBLOX_singletonEvent";
    public const string LocalEventName = "ROBLOX_singletonEvent";
    public const string GlobalMutexName = @"Global\ROBLOX_singletonMutex";

    private static readonly TimeSpan HandleScanInterval = TimeSpan.FromSeconds(3);

    private readonly IProcessProvider _processProvider;
    private readonly string? _mutexName;
    private readonly string? _eventName;
    private readonly bool _isDefaultProductionGate;

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
        _isDefaultProductionGate =
            string.Equals(_mutexName, DefaultMutexName, StringComparison.Ordinal) &&
            (string.Equals(_eventName, DefaultEventName, StringComparison.Ordinal) ||
             string.Equals(_eventName, LocalEventName, StringComparison.Ordinal));

        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "RbxManager_InstanceGateWorker"
        };
        _workerThread.Start();
    }

    public static Mutex? CreateLenientMutex(string name, bool initiallyOwned, out bool createdNew)
    {
        Mutex mutex = new(initiallyOwned, name, out createdNew);
        ApplyWorldFullControlSecurity(mutex);
        return mutex;
    }

    public static void ApplyWorldFullControlSecurity(Mutex mutex)
    {
        ArgumentNullException.ThrowIfNull(mutex);
        try
        {
            MutexSecurity security = mutex.GetAccessControl();
            MutexAccessRule securityBehavior = new(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow);
            security.AddAccessRule(securityBehavior);
            mutex.SetAccessControl(security);
        }
        catch
        {
            // Ignore ACL update errors when attaching to a mutex owned by another process or restricted token
        }
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
            if (_isDefaultProductionGate)
            {
                RobloxHandleCloser.CloseSingletonHandlesInRobloxProcesses(
                    _processProvider,
                    LocalEventName,
                    DefaultMutexName);
            }
            else
            {
                return Result.Failure("RobloxRunning", "RobloxRunning");
            }
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
        Mutex? localEventMutex = null;
        Mutex? globalMutex = null;
        FileStream? cookieLock = null;

        while (true)
        {
            bool signaled = _isEnabled
                ? _commandReady.Wait(HandleScanInterval)
                : SubmitWaitInfinite();

            if (!signaled)
            {
                if (_isEnabled)
                {
                    MaintainMultiInstanceBypass(
                        ref mutex,
                        ref eventMutex,
                        ref localEventMutex,
                        ref globalMutex,
                        ref cookieLock);
                }

                continue;
            }

            _commandReady.Reset();

            GateCommand cmd = _pendingCommand;
            _pendingCommand = GateCommand.None;

            if (cmd == GateCommand.Enable)
            {
                try
                {
                    if (_isDefaultProductionGate)
                    {
                        TryCloseExistingSingletonHandles();
                    }

                    mutex = CreateLenientMutex(_mutexName!, initiallyOwned: true, out bool mutexCreatedNew);
                    if (!mutexCreatedNew && !_isDefaultProductionGate)
                    {
                        CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex, ref cookieLock);
                        _isEnabled = false;
                        _commandResult = Result.Failure("CouldNotTakeOver", "CouldNotTakeOver");
                    }
                    else
                    {
                        // Lock Global\ROBLOX_singletonEvent as a Mutex with WorldSid FullControl so
                        // RobloxPlayerBeta.exe across User/Admin sessions cannot create/signal the singleton Event.
                        eventMutex = CreateLenientMutex(_eventName!, initiallyOwned: true, out bool eventCreatedNew);

                        if (!eventCreatedNew && !_isDefaultProductionGate)
                        {
                            CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex, ref cookieLock);
                            _isEnabled = false;
                            _commandResult = Result.Failure("CouldNotTakeOver", "CouldNotTakeOver");
                        }
                        else
                        {
                            if (_isDefaultProductionGate)
                            {
                                TryAcquireAuxiliaryLocks(ref localEventMutex, ref globalMutex, ref cookieLock);
                                RobloxHandleCloser.CloseSingletonHandlesInRobloxProcesses(
                                    _processProvider,
                                    LocalEventName,
                                    DefaultMutexName);
                            }

                            _isEnabled = true;
                            _commandResult = Result.Success();
                        }
                    }
                }
                catch (Exception)
                {
                    CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex, ref cookieLock);
                    _isEnabled = false;
                    _commandResult = Result.Failure("MutexError", "MutexError");
                }

                _commandDone.Set();
            }
            else if (cmd == GateCommand.Disable)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex, ref cookieLock);
                _isEnabled = false;
                _commandResult = Result.Success();
                _commandDone.Set();
            }
            else if (cmd == GateCommand.Shutdown)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex, ref cookieLock);
                _isEnabled = false;
                _commandDone.Set();
                break;
            }
        }
    }

    private bool SubmitWaitInfinite()
    {
        _commandReady.Wait();
        return true;
    }

    private void MaintainMultiInstanceBypass(
        ref Mutex? mutex,
        ref Mutex? eventMutex,
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex,
        ref FileStream? cookieLock)
    {
        try
        {
            GC.KeepAlive(mutex);
            GC.KeepAlive(eventMutex);
            GC.KeepAlive(localEventMutex);
            GC.KeepAlive(globalMutex);
            GC.KeepAlive(cookieLock);

            if (_isDefaultProductionGate)
            {
                RobloxHandleCloser.CloseSingletonHandlesInRobloxProcesses(
                    _processProvider,
                    LocalEventName,
                    DefaultMutexName);

                TryAcquireAuxiliaryLocks(ref localEventMutex, ref globalMutex, ref cookieLock);
            }
        }
        catch
        {
            // Keep background maintenance loop resilient
        }
    }

    private static void TryCloseExistingSingletonHandles()
    {
        // Replicate Multiple Roblox V2.2 startup cleanup:
        // try { Mutex.OpenExisting("ROBLOX_singletonMutex").Close(); Mutex.OpenExisting("ROBLOX_singletonEvent").Close(); } catch { }
        TryOpenAndCloseMutex(DefaultMutexName);
        TryOpenAndCloseMutex(LocalEventName);
        TryOpenAndCloseMutex(DefaultEventName);
        TryOpenAndCloseMutex(GlobalMutexName);
    }

    private static void TryOpenAndCloseMutex(string name)
    {
        try
        {
            using Mutex existing = Mutex.OpenExisting(name);
            existing.Close();
        }
        catch
        {
        }
    }

    private void TryAcquireAuxiliaryLocks(
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex,
        ref FileStream? cookieLock)
    {
        if (localEventMutex == null && !string.Equals(_eventName, LocalEventName, StringComparison.Ordinal))
        {
            try
            {
                localEventMutex = CreateLenientMutex(LocalEventName, initiallyOwned: true, out _);
            }
            catch
            {
            }
        }

        if (globalMutex == null && !string.Equals(_mutexName, GlobalMutexName, StringComparison.Ordinal))
        {
            try
            {
                globalMutex = CreateLenientMutex(GlobalMutexName, initiallyOwned: true, out _);
            }
            catch
            {
            }
        }

        // Replicate Multiple Roblox V2.2 Error 773 fix: lock RobloxCookies.dat with FileShare.None
        if (cookieLock == null)
        {
            try
            {
                string robloxCookiePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Roblox",
                    "LocalStorage",
                    "RobloxCookies.dat");
                if (File.Exists(robloxCookiePath))
                {
                    cookieLock = new FileStream(
                        robloxCookiePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None);
                }
            }
            catch
            {
            }
        }
    }

    private static void CleanupHandles(
        ref Mutex? mutex,
        ref Mutex? eventMutex,
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex,
        ref FileStream? cookieLock)
    {
        ReleaseAndDisposeMutex(ref mutex);
        ReleaseAndDisposeMutex(ref eventMutex);
        ReleaseAndDisposeMutex(ref localEventMutex);
        ReleaseAndDisposeMutex(ref globalMutex);

        if (cookieLock != null)
        {
            try
            {
                cookieLock.Dispose();
            }
            catch
            {
            }

            cookieLock = null;
        }
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
