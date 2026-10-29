namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Launch;
using Microsoft.Extensions.Time.Testing;

public sealed class RobloxLauncherPacingTests
{
    private const string FakeCookie = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-into-your-account-and-to-steal-your-ROBUX-and-clothing._|1234567890ABCDEF";

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly List<Account> _accounts = new();

        public FakeAccountStore(params Account[] accounts)
        {
            _accounts.AddRange(accounts);
        }

        public string VaultPath => "memory://vault";
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
        {
            IReadOnlyList<Account> copy = _accounts.ConvertAll(a => a with { Cookie = a.Cookie.Clone() });
            return Task.FromResult(copy);
        }
        public Task<Result> AddAsync(Account account, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task RemoveAsync(AccountId id, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RobloxUser(1001, "TestPlayer", "TestPlayer")));

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(SecretValue.FromString("ticket-1001")));
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public List<string> StartedUris { get; } = new();

        public bool IsRobloxPlayerInstalled() => true;

        public bool Start(string uri)
        {
            StartedUris.Add(uri);
            return true;
        }
    }

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => [];
        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeInstanceGate : IInstanceGate
    {
        public bool IsAvailable => true;
        public bool IsMultiInstanceEnabled => true;
        public Result TryEnable() => Result.Success();
        public void Disable() { }
    }

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

        public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
        {
            AppSettings updated = mutate(CurrentSettings);
            CurrentSettings = updated;
            SettingsChanged?.Invoke(updated);
            return Task.FromResult(updated);
        }
    }

    [Fact]
    public async Task LaunchAsync_FirstLaunch_StartsImmediatelyWithoutDelay()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "Acc1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new();

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider);

        LaunchRequest request = new(account.Id, 1818, null);

        Task<Result> launchTask = launcher.LaunchAsync(request);
        Assert.True(launchTask.IsCompletedSuccessfully);
        Result result = await launchTask;

        Assert.True(result.IsSuccess);
        Assert.Single(starter.StartedUris);
    }

    [Fact]
    public async Task LaunchAsync_ConsecutiveLaunches_EnforcesPacingInterval()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie);
        Account account1 = new(new AccountId(1), "Acc1", null, null, cookie1, DateTimeOffset.UtcNow);
        Account account2 = new(new AccountId(2), "Acc2", null, null, cookie2, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account1, account2);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new()
        {
            CurrentSettings = new AppSettings(LaunchDelaySeconds: 20)
        };

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider);

        // First launch: immediate
        Result r1 = await launcher.LaunchAsync(new LaunchRequest(account1.Id, 1818, null));
        Assert.True(r1.IsSuccess);
        Assert.Single(starter.StartedUris);

        // Second launch immediately after: should wait for 20 seconds
        Task<Result> secondLaunchTask = launcher.LaunchAsync(new LaunchRequest(account2.Id, 1818, null));
        Assert.False(secondLaunchTask.IsCompleted);

        // Advance time by 10s: still waiting
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        Assert.False(secondLaunchTask.IsCompleted);

        // Advance time by remaining 10s: completes!
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        Result r2 = await secondLaunchTask;
        Assert.True(r2.IsSuccess);
        Assert.Equal(2, starter.StartedUris.Count);
    }

    [Fact]
    public async Task LaunchAsync_CancelledWaitingRequest_DoesNotConsumeInterval()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie3 = SecretValue.FromString(FakeCookie);
        Account a1 = new(new AccountId(1), "Acc1", null, null, cookie1, DateTimeOffset.UtcNow);
        Account a2 = new(new AccountId(2), "Acc2", null, null, cookie2, DateTimeOffset.UtcNow);
        Account a3 = new(new AccountId(3), "Acc3", null, null, cookie3, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(a1, a2, a3);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new()
        {
            CurrentSettings = new AppSettings(LaunchDelaySeconds: 20)
        };

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider);

        // 1. Launch a1 at T = 0
        Result r1 = await launcher.LaunchAsync(new LaunchRequest(a1.Id, 1818, null));
        Assert.True(r1.IsSuccess);

        // 2. Queue a2 with cancellation token, advance to T = 5s, then cancel
        using CancellationTokenSource cts = new();
        Task<Result> a2Task = launcher.LaunchAsync(new LaunchRequest(a2.Id, 1818, null), cts.Token);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a2Task);

        // 3. Queue a3 at T = 5s. Since a2 was cancelled, the interval is still relative to a1 (T = 0),
        // so a3 should complete once time reaches T = 20s (advance 15s more).
        Task<Result> a3Task = launcher.LaunchAsync(new LaunchRequest(a3.Id, 1818, null));
        Assert.False(a3Task.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(10)); // T = 15s
        Assert.False(a3Task.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5)); // T = 20s
        Result r3 = await a3Task;
        Assert.True(r3.IsSuccess);
        Assert.Equal(2, starter.StartedUris.Count); // a1 and a3
    }

    [Fact]
    public async Task LaunchAsync_AllFivePaths_AreEnforcedBySameGate()
    {
        // Path 1: Single launch
        // Path 2: Bulk launch / Group launch
        // Path 3: Join from Servers page
        // Path 4: Auto-rejoin
        // Path 5: Smart server hopping
        //
        // Proving all 5 paths go through the same ILauncher.LaunchAsync pacing gate:
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "UnifiedAcc", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new()
        {
            CurrentSettings = new AppSettings(LaunchDelaySeconds: 20)
        };

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider);

        // 1. Single launch at T = 0
        Result singleResult = await launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, null));
        Assert.True(singleResult.IsSuccess);

        // 2. Bulk launch path (invokes launcher.LaunchAsync)
        Task<Result> bulkItem = launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, null));
        Assert.False(bulkItem.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await bulkItem).IsSuccess);

        // 3. Servers page join path (invokes launcher.LaunchAsync)
        Task<Result> serverJoin = launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, Guid.NewGuid().ToString()));
        Assert.False(serverJoin.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await serverJoin).IsSuccess);

        // 4. Auto-rejoin path (invokes launcher.LaunchAsync)
        Task<Result> autoRejoin = launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, null));
        Assert.False(autoRejoin.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await autoRejoin).IsSuccess);

        // 5. Smart server hop path (invokes launcher.LaunchAsync with new server jobId)
        Task<Result> hopLaunch = launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, Guid.NewGuid().ToString()));
        Assert.False(hopLaunch.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await hopLaunch).IsSuccess);

        Assert.Equal(5, starter.StartedUris.Count);
    }

    private sealed class ThrottlingAuthService : IRobloxAuthService
    {
        public int RemainingRateLimits { get; set; }

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RobloxUser(1001, "TestPlayer", "TestPlayer")));

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
        {
            if (RemainingRateLimits > 0)
            {
                RemainingRateLimits--;
                return Task.FromResult(Result.Failure<SecretValue>("Rate limited", AuthErrorCodes.RateLimited));
            }

            return Task.FromResult(Result.Success(SecretValue.FromString("ticket-1001")));
        }
    }

    [Fact]
    public async Task LaunchAsync_OnRateLimitOrChallenge_EnforcesExponentialBackoffAndJitter()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie = SecretValue.FromString(FakeCookie);
        Account account = new(new AccountId(1), "Acc1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        ThrottlingAuthService auth = new() { RemainingRateLimits = 1 };
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new()
        {
            CurrentSettings = new AppSettings(LaunchDelaySeconds: 20)
        };

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider,
            jitterProvider: () => TimeSpan.FromSeconds(3));

        // 1st launch hits 429/403 rate limit -> schedules 30s backoff
        Result r1 = await launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, null));
        Assert.False(r1.IsSuccess);
        Assert.Equal(AuthErrorCodes.RateLimited, r1.ErrorCode);

        // 2nd launch must wait out the 30s backoff (greater than the 20s + 3s jitter interval)
        Task<Result> secondLaunch = launcher.LaunchAsync(new LaunchRequest(account.Id, 1818, null));
        timeProvider.Advance(TimeSpan.FromSeconds(25));
        Assert.False(secondLaunch.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        Result r2 = await secondLaunch;
        Assert.True(r2.IsSuccess);
        Assert.Single(starter.StartedUris);
    }

    private sealed class ModeratedThenHealthyAuthService : IRobloxAuthService
    {
        private int _calls;

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RobloxUser(1001, "TestPlayer", "TestPlayer")));

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
        {
            if (++_calls == 1)
            {
                AuthFailureDetails details = new("Ticket", 403, [0], "User is moderated", true, [], false);
                return Task.FromResult(Result.Failure<SecretValue>("Account moderated", AuthErrorCodes.Moderated, details));
            }

            return Task.FromResult(Result.Success(SecretValue.FromString("ticket-1002")));
        }
    }

    [Fact]
    public async Task LaunchAsync_OnModerated403_DoesNotTriggerGlobalRateLimitCooldown()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie);
        Account account1 = new(new AccountId(1), "ModAcc", null, null, cookie1, DateTimeOffset.UtcNow);
        Account account2 = new(new AccountId(2), "GoodAcc", null, null, cookie2, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account1, account2);
        ModeratedThenHealthyAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new()
        {
            CurrentSettings = new AppSettings(LaunchDelaySeconds: 20)
        };

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider,
            jitterProvider: () => TimeSpan.Zero);

        Result r1 = await launcher.LaunchAsync(new LaunchRequest(account1.Id, 1818, null));
        Assert.False(r1.IsSuccess);
        Assert.Equal(AuthErrorCodes.Moderated, r1.ErrorCode);

        // Next healthy account waits only the normal 20s pacing interval, NOT a 30s rate-limit backoff
        Task<Result> secondLaunch = launcher.LaunchAsync(new LaunchRequest(account2.Id, 1818, null));
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Result r2 = await secondLaunch;
        Assert.True(r2.IsSuccess);
        Assert.Single(starter.StartedUris);
    }

    private sealed class FakeCacheCleaner : Core.Maintenance.IRobloxCacheCleaner
    {
        public int ClearCookiesCalls { get; private set; }

        public Task<Core.Maintenance.CacheCleanResult> CleanAsync(CancellationToken ct = default) =>
            Task.FromResult(new Core.Maintenance.CacheCleanResult(0, 0, 0));

        public bool ClearSharedCookiesFile()
        {
            ClearCookiesCalls++;
            return true;
        }
    }

    [Fact]
    public async Task LaunchAsync_UsesDeterministicPerAccountTrackerId_AndClearsSharedCookiesFile()
    {
        FakeTimeProvider timeProvider = new();
        using SecretValue cookie1 = SecretValue.FromString(FakeCookie);
        using SecretValue cookie2 = SecretValue.FromString(FakeCookie);
        Account account1 = new(new AccountId(101), "Acc1", null, null, cookie1, DateTimeOffset.UtcNow);
        Account account2 = new(new AccountId(202), "Acc2", null, null, cookie2, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account1, account2);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate gate = new();
        FakeSettingsService settings = new() { CurrentSettings = new AppSettings(LaunchDelaySeconds: 1) };
        FakeCacheCleaner cleaner = new();

        using RobloxLauncher launcher = new(
            store,
            auth,
            starter,
            processProvider,
            gate,
            settingsService: settings,
            timeProvider: timeProvider,
            cacheCleaner: cleaner,
            jitterProvider: () => TimeSpan.Zero);

        string expectedTracker1 = RobloxLauncher.ComputeDeterministicTrackerId(account1.Id);
        string expectedTracker2 = RobloxLauncher.ComputeDeterministicTrackerId(account2.Id);
        Assert.NotEqual(expectedTracker1, expectedTracker2);
        Assert.Equal(expectedTracker1, RobloxLauncher.ComputeDeterministicTrackerId(account1.Id));

        await launcher.LaunchAsync(new LaunchRequest(account1.Id, 1818, null));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await launcher.LaunchAsync(new LaunchRequest(account1.Id, 1818, null));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await launcher.LaunchAsync(new LaunchRequest(account2.Id, 1818, null));

        Assert.Equal(3, starter.StartedUris.Count);
        Assert.Contains($"browsertrackerid:{expectedTracker1}", starter.StartedUris[0], StringComparison.Ordinal);
        Assert.Contains($"browsertrackerid:{expectedTracker1}", starter.StartedUris[1], StringComparison.Ordinal);
        Assert.Contains($"browsertrackerid:{expectedTracker2}", starter.StartedUris[2], StringComparison.Ordinal);
        Assert.Equal(3, cleaner.ClearCookiesCalls);
    }
}
