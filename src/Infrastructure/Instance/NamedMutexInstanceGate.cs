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
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IProcessProvider _processProvider;
    private readonly string? _mutexName;
    private readonly string? _eventName;
    private readonly bool _isDefaultProductionGate;

    private readonly object _syncLock = new();
    private readonly object _completionLock = new();
    private readonly ManualResetEventSlim _commandReady = new(false);
    // Set while the worker is idle; reset for the lifetime of a submitted command.
    private readonly ManualResetEventSlim _commandDone = new(true);
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
    private bool _rollbackPendingEnable;

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
        : this(processProvider, mutexName, eventName, IsProductionNames(mutexName, eventName))
    {
    }

    /// <param name="useProductionSemantics">
    /// Forces the Roblox takeover rules (ownership pre-check, auxiliary locks) regardless of names, so tests can
    /// exercise them with unique object names.
    /// </param>
    internal NamedMutexInstanceGate(
        IProcessProvider processProvider,
        string? mutexName,
        string? eventName,
        bool useProductionSemantics)
    {
        _processProvider = processProvider ?? throw new ArgumentNullException(nameof(processProvider));
        _mutexName = mutexName;
        _eventName = eventName;
        _isDefaultProductionGate = useProductionSemantics;

        _workerThread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "RbxManager_InstanceGateWorker"
        };
        _workerThread.Start();
    }

    private static bool IsProductionNames(string? mutexName, string? eventName) =>
        string.Equals(mutexName, DefaultMutexName, StringComparison.Ordinal) &&
        (string.Equals(eventName, DefaultEventName, StringComparison.Ordinal) ||
         string.Equals(eventName, LocalEventName, StringComparison.Ordinal));

    public static Mutex CreateLenientMutex(string name, bool initiallyOwned, out bool createdNew)
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
        if (running.Count > 0 && !_isDefaultProductionGate)
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

            if (SubmitCommand(GateCommand.Enable))
            {
                return _commandResult;
            }

            lock (_completionLock)
            {
                if (_commandDone.IsSet)
                {
                    return _commandResult;
                }

                // The caller is told "Timeout", so an Enable that finishes later must not leave the gate silently enabled.
                _rollbackPendingEnable = true;
            }

            return Result.Failure("Timeout", "Timeout");
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

            SubmitCommand(GateCommand.Disable);
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
            SubmitCommand(GateCommand.Shutdown);
        }

        if (_workerThread.IsAlive)
        {
            _workerThread.Join(CommandTimeout);
        }

        _commandReady.Dispose();
        _commandDone.Dispose();
        GC.SuppressFinalize(this);
    }

    // Caller must hold _syncLock.
    private bool SubmitCommand(GateCommand command)
    {
        // Never overlap commands: a previously timed-out command must drain first, otherwise its completion
        // would be mistaken for this one's.
        if (!_commandDone.Wait(CommandTimeout) && command != GateCommand.Shutdown)
        {
            return false;
        }

        _pendingCommand = command;
        _commandDone.Reset();
        _commandReady.Set();
        return _commandDone.Wait(CommandTimeout);
    }

    private void CompleteCommand(
        ref Mutex? mutex,
        ref Mutex? eventMutex,
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex)
    {
        lock (_completionLock)
        {
            if (_rollbackPendingEnable && _isEnabled)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
                _isEnabled = false;
                _commandResult = Result.Failure("Timeout", "Timeout");
            }

            _rollbackPendingEnable = false;
            _commandDone.Set();
        }
    }

    private void WorkerLoop()
    {
        Mutex? mutex = null;
        Mutex? eventMutex = null;
        Mutex? localEventMutex = null;
        Mutex? globalMutex = null;

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
                        ref globalMutex);
                }

                continue;
            }

            _commandReady.Reset();

            GateCommand cmd = _pendingCommand;
            _pendingCommand = GateCommand.None;

            if (cmd == GateCommand.Enable)
            {
                _commandResult = AcquireSingletonObjects(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
                _isEnabled = _commandResult.IsSuccess;
                CompleteCommand(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
            }
            else if (cmd == GateCommand.Disable)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
                _isEnabled = false;
                _commandResult = Result.Success();
                CompleteCommand(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
            }
            else if (cmd == GateCommand.Shutdown)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
                _isEnabled = false;
                _commandDone.Set();
                break;
            }
        }
    }

    private Result AcquireSingletonObjects(
        ref Mutex? mutex,
        ref Mutex? eventMutex,
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex)
    {
        try
        {
            mutex = CreateLenientMutex(_mutexName!, initiallyOwned: true, out bool mutexCreatedNew);
            Result claim = mutexCreatedNew ? Result.Success() : ClaimExistingMutex(mutex);

            if (claim.IsSuccess)
            {
                // Lock Global\ROBLOX_singletonEvent as a Mutex with WorldSid FullControl so
                // RobloxPlayerBeta.exe across User/Admin sessions cannot create/signal the singleton Event.
                eventMutex = CreateLenientMutex(_eventName!, initiallyOwned: true, out bool eventCreatedNew);
                claim = eventCreatedNew ? Result.Success() : ClaimExistingMutex(eventMutex);
            }

            if (!claim.IsSuccess)
            {
                CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
                return claim;
            }

            if (_isDefaultProductionGate)
            {
                TryAcquireAuxiliaryLocks(ref localEventMutex, ref globalMutex);
            }

            return Result.Success();
        }
        catch (WaitHandleCannotBeOpenedException) when (_isDefaultProductionGate)
        {
            // A running Roblox already created the singleton name as an Event, so it cannot be claimed as a Mutex.
            CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
            return Result.Failure("RobloxRunning", "RobloxRunning");
        }
        catch (Exception)
        {
            CleanupHandles(ref mutex, ref eventMutex, ref localEventMutex, ref globalMutex);
            return Result.Failure("MutexError", "MutexError");
        }
    }

    private Result ClaimExistingMutex(Mutex existing)
    {
        if (!_isDefaultProductionGate)
        {
            return Result.Failure("CouldNotTakeOver", "CouldNotTakeOver");
        }

        // initiallyOwned is ignored when the mutex already exists, so ownership has to be taken explicitly.
        // If someone else (a running Roblox) holds it, the bypass would be ineffective and must not report success.
        try
        {
            return existing.WaitOne(0)
                ? Result.Success()
                : Result.Failure("RobloxRunning", "RobloxRunning");
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited without releasing; ownership has been transferred to this thread.
            return Result.Success();
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
        ref Mutex? globalMutex)
    {
        try
        {
            GC.KeepAlive(mutex);
            GC.KeepAlive(eventMutex);
            GC.KeepAlive(localEventMutex);
            GC.KeepAlive(globalMutex);

            if (_isDefaultProductionGate)
            {
                TryAcquireAuxiliaryLocks(ref localEventMutex, ref globalMutex);
            }
        }
        catch
        {
            // Keep background maintenance loop resilient
        }
    }

    private void TryAcquireAuxiliaryLocks(
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex)
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
    }

    private static void CleanupHandles(
        ref Mutex? mutex,
        ref Mutex? eventMutex,
        ref Mutex? localEventMutex,
        ref Mutex? globalMutex)
    {
        ReleaseAndDisposeMutex(ref mutex);
        ReleaseAndDisposeMutex(ref eventMutex);
        ReleaseAndDisposeMutex(ref localEventMutex);
        ReleaseAndDisposeMutex(ref globalMutex);
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
