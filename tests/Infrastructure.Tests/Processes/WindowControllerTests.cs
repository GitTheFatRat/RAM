namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Processes;

[SupportedOSPlatform("windows")]
public sealed class WindowControllerTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = new();

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful) => true;
    }

    [Fact]
    public void ThrottleBackgroundProcesses_WhenNoRobloxProcesses_CompletesWithoutError()
    {
        FakeProcessProvider processProvider = new();
        WindowController controller = new(processProvider);

        // Should complete cleanly without throwing
        controller.ThrottleBackgroundProcesses();
    }

    [Fact]
    public void RestoreProcesses_WhenNoThrottledWindows_CompletesWithoutError()
    {
        FakeProcessProvider processProvider = new();
        WindowController controller = new(processProvider);

        controller.RestoreProcesses();
    }

    [Fact]
    public void ThrottleAndRestore_WithFakeProcesses_ExecutesCleanly()
    {
        FakeProcessProvider processProvider = new()
        {
            Processes = [new ProcessSnapshot(12345, DateTimeOffset.UtcNow, 1024L * 1024L)]
        };
        WindowController controller = new(processProvider);

        controller.ThrottleBackgroundProcesses();
        controller.RestoreProcesses();
    }
}
