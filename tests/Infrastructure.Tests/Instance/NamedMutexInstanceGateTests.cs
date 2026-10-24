namespace RbxManager.Infrastructure.Tests.Instance;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using RbxManager.Core.Common;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Instance;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class NamedMutexInstanceGateTests
{
    private static string CreateUniqueMutexName() => $"RbxManagerTest_Mutex_{Guid.NewGuid():N}";
    private static string CreateUniqueEventName() => $"RbxManagerTest_Event_{Guid.NewGuid():N}";

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = [];

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful) => true;
    }

    [Fact]
    public void TryEnable_ProcessRunning_Refuses()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 50000)]
        };

        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        using NamedMutexInstanceGate gate = new(provider, mutexName, eventName);
        Result result = gate.TryEnable();

        Assert.False(result.IsSuccess);
        Assert.Equal("RobloxRunning", result.ErrorCode);
        Assert.False(gate.IsMultiInstanceEnabled);
    }

    [Fact]
    public void TryEnable_CreatesBothObjects_SecondProbeSeesThem()
    {
        FakeProcessProvider provider = new();
        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        using NamedMutexInstanceGate gate = new(provider, mutexName, eventName);
        Result result = gate.TryEnable();

        Assert.True(result.IsSuccess);
        Assert.True(gate.IsMultiInstanceEnabled);

        // Probe 1: attempt to create mutex with initiallyOwned: false; must detect that it already exists (createdNew = false)
        using Mutex probeMutex = new(initiallyOwned: false, mutexName, out bool mutexCreatedNew);
        Assert.False(mutexCreatedNew);

        // Probe 2: attempt to create event mutex; must detect that it already exists (createdNew = false)
        using Mutex probeEvent = new(initiallyOwned: false, eventName, out bool eventCreatedNew);
        Assert.False(eventCreatedNew);
    }

    [Fact]
    public void Disable_ReleasesBothObjects_ProbeCanCreateThemAfresh()
    {
        FakeProcessProvider provider = new();
        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        using (NamedMutexInstanceGate gate = new(provider, mutexName, eventName))
        {
            Result res = gate.TryEnable();
            Assert.True(res.IsSuccess);
            Assert.True(gate.IsMultiInstanceEnabled);

            gate.Disable();
            Assert.False(gate.IsMultiInstanceEnabled);
        }

        // Probes can now create the objects afresh as brand new
        using Mutex probeMutex = new(initiallyOwned: false, mutexName, out bool mutexCreatedNew);
        Assert.True(mutexCreatedNew);

        using Mutex probeEvent = new(initiallyOwned: false, eventName, out bool eventCreatedNew);
        Assert.True(eventCreatedNew);
    }

    [Fact]
    public void Dispose_TwiceIsSafe_AndReleasesObjects()
    {
        FakeProcessProvider provider = new();
        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        NamedMutexInstanceGate gate = new(provider, mutexName, eventName);
        Result res = gate.TryEnable();
        Assert.True(res.IsSuccess);

        // Disposing twice must be idempotent and not throw
        gate.Dispose();
        gate.Dispose();

        Assert.False(gate.IsMultiInstanceEnabled);

        using Mutex probeMutex = new(initiallyOwned: false, mutexName, out bool mutexCreatedNew);
        Assert.True(mutexCreatedNew);
    }

    [Fact]
    public void TryEnable_PartialCreationFailure_EventAlreadyExists_LeavesNoHeldObject()
    {
        FakeProcessProvider provider = new();
        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        // Pre-create the event mutex so gate's attempt to create the event mutex will fail (createdNew == false)
        using Mutex preExistingEvent = new(initiallyOwned: true, eventName, out bool eventCreatedNew);
        Assert.True(eventCreatedNew);

        using NamedMutexInstanceGate gate = new(provider, mutexName, eventName);
        Result result = gate.TryEnable();

        Assert.False(result.IsSuccess);
        Assert.Equal("CouldNotTakeOver", result.ErrorCode);
        Assert.False(gate.IsMultiInstanceEnabled);

        // Mutex was attempted during TryEnable, but on failure it MUST be released and disposed immediately.
        // A probe must be able to create the mutex afresh.
        using Mutex probeMutex = new(initiallyOwned: false, mutexName, out bool mutexCreatedNew);
        Assert.True(mutexCreatedNew);
    }

    [Fact]
    public void TryEnable_PartialCreationFailure_MutexAlreadyExists_LeavesNoHeldObject()
    {
        FakeProcessProvider provider = new();
        string mutexName = CreateUniqueMutexName();
        string eventName = CreateUniqueEventName();

        // Pre-create the mutex so gate's attempt to create mutex will fail (createdNew == false)
        using Mutex preExistingMutex = new(initiallyOwned: true, mutexName, out bool mutexCreatedNew);
        Assert.True(mutexCreatedNew);

        using NamedMutexInstanceGate gate = new(provider, mutexName, eventName);
        Result result = gate.TryEnable();

        Assert.False(result.IsSuccess);
        Assert.Equal("CouldNotTakeOver", result.ErrorCode);
        Assert.False(gate.IsMultiInstanceEnabled);

        // Event must not be left held; probe must be able to create the event afresh
        using Mutex probeEvent = new(initiallyOwned: false, eventName, out bool eventCreatedNew);
        Assert.True(eventCreatedNew);
    }

    [Fact]
    public void GateWithoutMutexOrEventName_ReturnsNotAvailable()
    {
        FakeProcessProvider provider = new();

        using NamedMutexInstanceGate gate1 = new(provider, mutexName: null, eventName: null);
        Assert.False(gate1.IsAvailable);
        Result res1 = gate1.TryEnable();
        Assert.False(res1.IsSuccess);
        Assert.Equal("MultiInstanceNotAvailable", res1.ErrorCode);

        using NamedMutexInstanceGate gate2 = new(provider, mutexName: "test", eventName: null);
        Assert.False(gate2.IsAvailable);
    }

    [Fact]
    public void ProductionDefaults_IsAvailableIsTrue()
    {
        FakeProcessProvider provider = new();

        using NamedMutexInstanceGate gate = new(provider);
        Assert.True(gate.IsAvailable);
        Assert.Equal(@"Global\ROBLOX_singletonEvent", NamedMutexInstanceGate.DefaultEventName);
        Assert.Equal("ROBLOX_singletonMutex", NamedMutexInstanceGate.DefaultMutexName);
    }

    [Fact]
    public void CreateLenientMutex_AppliesWorldFullControlAndAttachesIfAlreadyCreated()
    {
        string name = CreateUniqueEventName();

        using Mutex first = NamedMutexInstanceGate.CreateLenientMutex(name, initiallyOwned: false, out bool createdNew1)!;
        Assert.True(createdNew1);

        using Mutex second = NamedMutexInstanceGate.CreateLenientMutex(name, initiallyOwned: false, out bool createdNew2)!;
        Assert.False(createdNew2);
    }

    [Fact]
    public void RobloxHandleCloser_ClosesMatchingNamedHandleInCurrentProcess()
    {
        string uniqueEventName = $"RbxManagerTest_HandleClose_{Guid.NewGuid():N}";
        EventWaitHandle targetEvent = new(false, EventResetMode.ManualReset, uniqueEventName);
        try
        {
            HashSet<int> pids = [Environment.ProcessId];
            int closed = RobloxHandleCloser.CloseMatchingHandlesInProcesses(pids, [uniqueEventName]);
            Assert.True(closed >= 1);

            // Because the kernel handle was closed in our process, creating a new EventWaitHandle with the same name creates a brand new one
            using EventWaitHandle probe = new(false, EventResetMode.ManualReset, uniqueEventName, out bool createdNew);
            Assert.True(createdNew);
        }
        finally
        {
            try
            {
                targetEvent.Dispose();
            }
            catch
            {
            }
        }
    }
}
