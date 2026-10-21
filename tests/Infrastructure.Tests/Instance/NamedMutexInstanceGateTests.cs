namespace RbxManager.Infrastructure.Tests.Instance;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using RbxManager.Core.Common;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Instance;

[SupportedOSPlatform("windows")]
public sealed class NamedMutexInstanceGateTests
{
    private const string PrivateTestMutexName = "Local\\RbxManager_UnitTest_PrivateMutex_Test";

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

        using NamedMutexInstanceGate gate = new(provider, PrivateTestMutexName);
        Result result = gate.TryEnable();

        Assert.False(result.IsSuccess);
        Assert.Equal("RobloxRunning", result.ErrorCode);
        Assert.False(gate.IsMultiInstanceEnabled);
    }

    [Fact]
    public void TryEnable_NoProcessRunning_AcquiresTestMutex()
    {
        FakeProcessProvider provider = new()
        {
            Processes = []
        };

        using NamedMutexInstanceGate gate = new(provider, PrivateTestMutexName);
        Result result = gate.TryEnable();

        Assert.True(result.IsSuccess);
        Assert.True(gate.IsMultiInstanceEnabled);

        gate.Disable();
        Assert.False(gate.IsMultiInstanceEnabled);
    }

    [Fact]
    public void Disable_ReleasesMutex_AllowingReacquire()
    {
        FakeProcessProvider provider = new();

        using (NamedMutexInstanceGate gate1 = new(provider, PrivateTestMutexName))
        {
            Result res1 = gate1.TryEnable();
            Assert.True(res1.IsSuccess);

            gate1.Disable();
            Assert.False(gate1.IsMultiInstanceEnabled);
        }

        using (NamedMutexInstanceGate gate2 = new(provider, PrivateTestMutexName))
        {
            Result res2 = gate2.TryEnable();
            Assert.True(res2.IsSuccess);
            Assert.True(gate2.IsMultiInstanceEnabled);
        }
    }

    [Fact]
    public void GateWithoutMutexName_ReturnsNotAvailable()
    {
        FakeProcessProvider provider = new();

        using NamedMutexInstanceGate gate = new(provider, mutexName: null);

        Assert.False(gate.IsAvailable);
        Assert.False(gate.IsMultiInstanceEnabled);

        Result result = gate.TryEnable();
        Assert.False(result.IsSuccess);
        Assert.Equal("MultiInstanceNotAvailable", result.ErrorCode);
    }
}
