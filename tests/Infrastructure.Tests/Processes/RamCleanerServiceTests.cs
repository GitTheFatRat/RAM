namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Processes;
using Xunit;

public sealed class RamCleanerServiceTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = AppSettings.Default;

        public event Action<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default) => Task.FromResult(CurrentSettings);

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
        {
            CurrentSettings = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMemoryOptimizer : IMemoryOptimizer
    {
        public int CallCount { get; private set; }
        public bool ShouldThrow { get; set; }
        public int LastTrimmedCount { get; set; }
        public int LastAccessDeniedCount { get; set; }
        public int LastFailedCount { get; set; }
        public IReadOnlyCollection<int>? LastTargetProcessIds { get; private set; }

        public Task TrimMemoryAsync(CancellationToken ct = default) => TrimMemoryAsync(null, ct);

        public Task TrimMemoryAsync(IReadOnlyCollection<int>? targetProcessIds, CancellationToken ct = default)
        {
            CallCount++;
            LastTargetProcessIds = targetProcessIds;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated trim error");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionTracker : ISessionTracker
    {
        public List<RunningSession> Sessions { get; set; } = new();
        public IReadOnlyList<RunningSession> ActiveSessions => Sessions;

        #pragma warning disable CS0067
        public event Action<RunningSession>? SessionStarted;
        public event Action<RunningSession, bool>? SessionEnded;
        public event Action<RunningSession, string>? GhostDisconnectOccurred;
        #pragma warning restore CS0067

        public bool GetAutoRejoin(AccountId accountId) => false;
        public void SetAutoRejoin(AccountId accountId, bool enabled) { }
        public bool IsRejoinGaveUp(AccountId accountId) => false;
        public Task<bool> CloseSessionAsync(int pid, bool graceful = true) => Task.FromResult(true);
        public Task CloseAllSessionsAsync(bool graceful = true) => Task.CompletedTask;
        public Task<RunningSession?> TrackLaunchAsync(
            AccountId accountId,
            LaunchRequest request,
            Func<Task<RbxManager.Core.Common.Result>> launchAction,
            CancellationToken ct = default) => Task.FromResult<RunningSession?>(null);
    }

    [Fact]
    public async Task RamCleanerService_WhenDisabled_DoesNotCallOptimizer()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = false }
        };
        FakeMemoryOptimizer optimizer = new();

        using CancellationTokenSource cts = new();
        int delayCalls = 0;

        Task DelayFunc(TimeSpan span, CancellationToken token)
        {
            delayCalls++;
            if (delayCalls >= 3)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        RamCleanerService service = new(settingsService, optimizer, sessionTracker: null, delayFunc: DelayFunc);

        await service.StartAsync(cts.Token);
        // Wait for ExecuteAsync background loop to conclude after cancel
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, optimizer.CallCount);
        Assert.True(delayCalls >= 3);
    }

    [Fact]
    public async Task RamCleanerService_WhenEnabled_CallsOptimizerWithCorrectInterval()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with
            {
                EnableRamCleaner = true,
                RamCleanerAcknowledged = true,
                RamCleanerIntervalMinutes = 20
            }
        };
        FakeMemoryOptimizer optimizer = new();

        using CancellationTokenSource cts = new();
        TimeSpan? observedDelay = null;
        int delayCalls = 0;

        Task DelayFunc(TimeSpan span, CancellationToken token)
        {
            delayCalls++;
            observedDelay = span;
            if (delayCalls >= 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        RamCleanerService service = new(settingsService, optimizer, sessionTracker: null, delayFunc: DelayFunc);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(20), observedDelay);
        Assert.True(optimizer.CallCount >= 1);
    }

    [Fact]
    public async Task RamCleanerService_WhenTokenCancelledImmediately_ExitsCleanly()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true, RamCleanerAcknowledged = true }
        };
        FakeMemoryOptimizer optimizer = new();

        using CancellationTokenSource cts = new();
        cts.Cancel();

        RamCleanerService service = new(settingsService, optimizer);

        // Should return cleanly without throwing
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, optimizer.CallCount);
    }

    [Fact]
    public async Task RamCleanerService_WhenOptimizerThrows_DoesNotCrashLoop()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true, RamCleanerAcknowledged = true }
        };
        FakeMemoryOptimizer optimizer = new()
        {
            ShouldThrow = true
        };

        using CancellationTokenSource cts = new();
        int iterations = 0;

        Task DelayFunc(TimeSpan span, CancellationToken token)
        {
            iterations++;
            if (iterations >= 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        RamCleanerService service = new(settingsService, optimizer, sessionTracker: null, delayFunc: DelayFunc);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.True(optimizer.CallCount >= 1);
    }

    [Fact]
    public async Task RamCleanerService_ThreeConsecutiveAccessDeniedCycles_AutoDisablesFeature()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true, RamCleanerAcknowledged = true }
        };
        FakeMemoryOptimizer optimizer = new()
        {
            LastAccessDeniedCount = 1,
            LastTrimmedCount = 0
        };

        using CancellationTokenSource cts = new();
        int iterations = 0;

        Task DelayFunc(TimeSpan span, CancellationToken token)
        {
            iterations++;
            if (iterations >= 4)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        RamCleanerService service = new(settingsService, optimizer, sessionTracker: null, delayFunc: DelayFunc);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.False(settingsService.CurrentSettings.EnableRamCleaner);
    }

    [Fact]
    public async Task RamCleanerService_WhenSessionTrackerProvided_PassesTrackedPidsToOptimizer()
    {
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true, RamCleanerAcknowledged = true }
        };
        FakeMemoryOptimizer optimizer = new();
        FakeSessionTracker sessionTracker = new()
        {
            Sessions =
            [
                new RunningSession(new AccountId(1), new LaunchRequest(new AccountId(1), 1818, null), 1234, DateTimeOffset.UtcNow),
                new RunningSession(new AccountId(2), new LaunchRequest(new AccountId(2), 1818, null), 5678, DateTimeOffset.UtcNow)
            ]
        };

        using CancellationTokenSource cts = new();
        int iterations = 0;

        Task DelayFunc(TimeSpan span, CancellationToken token)
        {
            iterations++;
            if (iterations >= 2)
            {
                cts.Cancel();
            }
            return Task.CompletedTask;
        }

        RamCleanerService service = new(settingsService, optimizer, sessionTracker, DelayFunc);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(optimizer.LastTargetProcessIds);
        Assert.Equal([1234, 5678], optimizer.LastTargetProcessIds);
    }
}
