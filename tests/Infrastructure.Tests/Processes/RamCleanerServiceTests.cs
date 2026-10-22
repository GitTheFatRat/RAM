namespace RbxManager.Infrastructure.Tests.Processes;

using System;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Processes;
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

        public Task TrimMemoryAsync(CancellationToken ct = default)
        {
            CallCount++;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated trim error");
            }
            return Task.CompletedTask;
        }
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

        RamCleanerService service = new(settingsService, optimizer, DelayFunc);

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

        RamCleanerService service = new(settingsService, optimizer, DelayFunc);

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
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true }
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
            CurrentSettings = AppSettings.Default with { EnableRamCleaner = true }
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

        RamCleanerService service = new(settingsService, optimizer, DelayFunc);

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        Assert.True(optimizer.CallCount >= 1);
    }
}
