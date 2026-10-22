namespace RbxManager.Infrastructure.Tests.Sessions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Sessions;

public sealed class SessionTrackerTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = [];
        public List<int> KilledPids { get; } = [];

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful)
        {
            KilledPids.Add(pid);
            Processes.RemoveAll(p => p.Pid == pid);
            return true;
        }
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public Dictionary<long, Account> Accounts { get; } = [];

        public string VaultPath => "memory://fake_vault.dat";

        public FakeAccountStore(params Account[] accounts)
        {
            foreach (Account a in accounts)
            {
                Accounts[a.Id.Value] = a;
            }
        }

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Account>>(Accounts.Values.ToList());

        public Task<Result> AddAsync(Account account, CancellationToken ct = default)
        {
            Accounts[account.Id.Value] = account;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default)
        {
            Accounts[account.Id.Value] = account;
            return Task.FromResult(Result.Success());
        }

        public Task RemoveAsync(AccountId id, CancellationToken ct = default)
        {
            Accounts.Remove(id.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Result<RobloxUser> ValidateResult { get; set; } = Result.Success(new RobloxUser(123, "Test", "Test"));

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(ValidateResult);

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(SecretValue.FromString("ticket")));
    }

    private sealed class FakeLauncher : ILauncher
    {
        public List<LaunchRequest> Launches { get; } = [];
        public Result LaunchResult { get; set; } = Result.Success();
        public Action<LaunchRequest>? OnLaunch { get; set; }

        public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            Launches.Add(request);
            OnLaunch?.Invoke(request);
            return Task.FromResult(LaunchResult);
        }
    }

    [Fact]
    public async Task TrackLaunchAsync_OneNewPid_BindsSessionAndRaisesSessionStarted()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(100, DateTimeOffset.UtcNow, 50000)]
        };
        FakeAccountStore store = new();
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        using SessionTracker tracker = new(provider, store, auth, launcher);
        AccountId accountId = new(1);
        LaunchRequest req = new(accountId, 1818, null);

        RunningSession? startedSession = null;
        tracker.SessionStarted += s => startedSession = s;

        RunningSession? result = await tracker.TrackLaunchAsync(accountId, req, () =>
        {
            provider.Processes.Add(new ProcessSnapshot(200, DateTimeOffset.UtcNow, 60000));
            return Task.FromResult(Result.Success());
        });

        Assert.NotNull(result);
        Assert.Equal(200, result.ProcessId);
        Assert.Equal(accountId, result.AccountId);
        Assert.Same(result, startedSession);
        Assert.Single(tracker.ActiveSessions);
        Assert.Equal(200, tracker.ActiveSessions[0].ProcessId);
    }

    [Fact]
    public async Task TrackLaunchAsync_TwoNewPids_LeavesUnbound()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(100, DateTimeOffset.UtcNow, 50000)]
        };
        FakeAccountStore store = new();
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        using SessionTracker tracker = new(provider, store, auth, launcher);
        AccountId accountId = new(1);
        LaunchRequest req = new(accountId, 1818, null);

        bool startedRaised = false;
        tracker.SessionStarted += _ => startedRaised = true;

        RunningSession? result = await tracker.TrackLaunchAsync(accountId, req, () =>
        {
            provider.Processes.Add(new ProcessSnapshot(200, DateTimeOffset.UtcNow, 60000));
            provider.Processes.Add(new ProcessSnapshot(300, DateTimeOffset.UtcNow, 60000));
            return Task.FromResult(Result.Success());
        });

        Assert.Null(result);
        Assert.False(startedRaised);
        Assert.Empty(tracker.ActiveSessions);
    }

    [Fact]
    public async Task PollProcesses_ProcessExit_RaisesSessionEndedWithIntentionalFalse()
    {
        FakeProcessProvider provider = new();
        FakeAccountStore store = new();
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        using SessionTracker tracker = new(provider, store, auth, launcher);
        AccountId accountId = new(1);
        RunningSession session = new(accountId, new LaunchRequest(accountId, 1818, null), 200, DateTimeOffset.UtcNow);

        tracker.RegisterSession(session);
        Assert.Single(tracker.ActiveSessions);

        RunningSession? endedSession = null;
        bool? wasIntentional = null;
        tracker.SessionEnded += (s, intentional) =>
        {
            endedSession = s;
            wasIntentional = intentional;
        };

        // Provider has no processes -> PID 200 has exited
        await tracker.PollProcessesAsync();

        Assert.NotNull(endedSession);
        Assert.Equal(200, endedSession.ProcessId);
        Assert.False(wasIntentional);
        Assert.Empty(tracker.ActiveSessions);
    }

    [Fact]
    public async Task CloseSessionAsync_SetsIntentionalFlagTrueOnExit()
    {
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(200, DateTimeOffset.UtcNow, 50000)]
        };
        FakeAccountStore store = new();
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        using SessionTracker tracker = new(provider, store, auth, launcher);
        AccountId accountId = new(1);
        RunningSession session = new(accountId, new LaunchRequest(accountId, 1818, null), 200, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        RunningSession? endedSession = null;
        bool? wasIntentional = null;
        tracker.SessionEnded += (s, intentional) =>
        {
            endedSession = s;
            wasIntentional = intentional;
        };

        bool closed = await tracker.CloseSessionAsync(200);
        Assert.True(closed);
        Assert.Contains(200, provider.KilledPids);

        await tracker.PollProcessesAsync();

        Assert.NotNull(endedSession);
        Assert.True(wasIntentional);
        Assert.Empty(tracker.ActiveSessions);
    }

    [Fact]
    public async Task AutoRejoin_UnexpectedExit_TriggersLauncher()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(300, DateTimeOffset.UtcNow, 50000));

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest req = new(account.Id, 1818, null);
        RunningSession session = new(account.Id, req, 200, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Single(launcher.Launches);
        Assert.Equal(account.Id, launcher.Launches[0].AccountId);
        Assert.Equal(1818, launcher.Launches[0].PlaceId);
    }

    [Fact]
    public async Task AutoRejoin_ExpiredCookie_StopsRejoinAndUpdatesAccount()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow, Status: AccountStatus.Valid);

        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new()
        {
            ValidateResult = Result.Failure<RobloxUser>("Cookie expired", AuthErrorCodes.Expired)
        };
        FakeLauncher launcher = new();

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest req = new(account.Id, 1818, null);
        RunningSession session = new(account.Id, req, 200, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Empty(launcher.Launches);
        Account updatedAccount = store.Accounts[account.Id.Value];
        Assert.Equal(AccountStatus.Expired, updatedAccount.Status);
    }

    [Fact]
    public async Task AutoRejoin_CircuitBreakerTripped_SetsGaveUp()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();

        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(400, DateTimeOffset.UtcNow, 50000));

        // Breaker with maxAttempts = 1
        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero, maxAttempts: 1));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest req = new(account.Id, 1818, null);

        // Attempt 1: succeeds
        RunningSession s1 = new(account.Id, req, 201, DateTimeOffset.UtcNow);
        tracker.RegisterSession(s1);
        await tracker.PollProcessesAsync();
        Assert.False(tracker.IsRejoinGaveUp(account.Id));

        // Attempt 2: breaker trips
        RunningSession s2 = new(account.Id, req, 202, DateTimeOffset.UtcNow);
        tracker.RegisterSession(s2);
        await tracker.PollProcessesAsync();

        Assert.True(tracker.IsRejoinGaveUp(account.Id));
    }

    private sealed class FakeServerBrowserService : IServerBrowserService
    {
        public Result<ServerPage> ServerResult { get; set; } = Core.Common.Result.Success(new ServerPage([], null));
        public bool ShouldThrow { get; set; }
        public int CallCount { get; private set; }

        public Task<Result<ServerPage>> GetServersAsync(
            long placeId,
            ServerSort sort = ServerSort.Descending,
            string? cursor = null,
            CancellationToken ct = default)
        {
            CallCount++;
            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated network timeout");
            }
            return Task.FromResult(ServerResult);
        }
    }

    private sealed class FakeSettingsService : Core.Settings.ISettingsService
    {
        public Core.Settings.AppSettings CurrentSettings { get; set; } = Core.Settings.AppSettings.Default;
        public event Action<Core.Settings.AppSettings>? SettingsChanged;
        public Task<Core.Settings.AppSettings> LoadSettingsAsync(CancellationToken ct = default) => Task.FromResult(CurrentSettings);
        public Task SaveSettingsAsync(Core.Settings.AppSettings settings, CancellationToken ct = default)
        {
            CurrentSettings = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AutoRejoin_SmartHoppingEnabled_FetchesNewServerAndLaunchesWithNewJobId()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(401, DateTimeOffset.UtcNow, 50000));

        FakeServerBrowserService serverBrowser = new()
        {
            ServerResult = Core.Common.Result.Success(new ServerPage(
            [
                new ServerInfo("full-server-1", 20, 20, null, null),
                new ServerInfo("candidate-server-2", 12, 20, null, null)
            ], null))
        };
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { EnableSmartServerHopping = true }
        };

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero, maxAttempts: 3));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest originalReq = new(account.Id, 1818, "original-job-id");
        RunningSession session = new(account.Id, originalReq, 301, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Equal(1, serverBrowser.CallCount);
        Assert.Single(launcher.Launches);
        Assert.Equal("candidate-server-2", launcher.Launches[0].JobId);
        Assert.Equal(1818, launcher.Launches[0].PlaceId);
    }

    [Fact]
    public async Task AutoRejoin_SmartHoppingThrows_FallsBackToOriginalRequest()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(402, DateTimeOffset.UtcNow, 50000));

        FakeServerBrowserService serverBrowser = new()
        {
            ShouldThrow = true
        };
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { EnableSmartServerHopping = true }
        };

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero, maxAttempts: 3));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest originalReq = new(account.Id, 1818, "original-job-id");
        RunningSession session = new(account.Id, originalReq, 302, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Equal(1, serverBrowser.CallCount);
        Assert.Single(launcher.Launches);
        Assert.Equal("original-job-id", launcher.Launches[0].JobId);
    }

    [Fact]
    public async Task AutoRejoin_SmartHoppingReturnsEmpty_FallsBackToOriginalRequest()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(403, DateTimeOffset.UtcNow, 50000));

        FakeServerBrowserService serverBrowser = new()
        {
            ServerResult = Core.Common.Result.Success(new ServerPage([], null))
        };
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { EnableSmartServerHopping = true }
        };

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero, maxAttempts: 3));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest originalReq = new(account.Id, 1818, "original-job-id");
        RunningSession session = new(account.Id, originalReq, 303, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Equal(1, serverBrowser.CallCount);
        Assert.Single(launcher.Launches);
        Assert.Equal("original-job-id", launcher.Launches[0].JobId);
    }

    [Fact]
    public async Task AutoRejoin_SmartHoppingDisabled_DoesNotQueryServerBrowser()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        launcher.OnLaunch = _ => provider.Processes.Add(new ProcessSnapshot(404, DateTimeOffset.UtcNow, 50000));

        FakeServerBrowserService serverBrowser = new()
        {
            ServerResult = Core.Common.Result.Success(new ServerPage([new ServerInfo("hop-job", 1, 10, null, null)], null))
        };
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { EnableSmartServerHopping = false }
        };

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            policyFactory: () => new RejoinPolicy(initialDelay: TimeSpan.Zero, maxAttempts: 3));

        tracker.SetAutoRejoin(account.Id, true);

        LaunchRequest originalReq = new(account.Id, 1818, "original-job-id");
        RunningSession session = new(account.Id, originalReq, 304, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        await tracker.PollProcessesAsync();

        Assert.Equal(0, serverBrowser.CallCount);
        Assert.Single(launcher.Launches);
        Assert.Equal("original-job-id", launcher.Launches[0].JobId);
    }

    private sealed class FakeLogWatcher : IRobloxLogWatcher
    {
        public event Action<int, string>? GhostDisconnectDetected;
        public List<int> WatchedPids { get; } = [];
        public List<int> StoppedPids { get; } = [];

        public void StartWatching(int pid, DateTimeOffset startTime, string? explicitLogPath = null) => WatchedPids.Add(pid);
        public void StopWatching(int pid) => StoppedPids.Add(pid);

        public void RaiseDisconnect(int pid, string keyword) => GhostDisconnectDetected?.Invoke(pid, keyword);
        public void Dispose() { }
    }

    [Fact]
    public void GhostDisconnectDetected_WhenKillAndRejoin_AndAgeOver60s_KillsRobloxProcess()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        FakeServerBrowserService serverBrowser = new();
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { GhostDisconnectMode = Core.Processes.GhostDisconnectMode.KillAndRejoin }
        };
        FakeLogWatcher logWatcher = new();

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            logWatcher);

        LaunchRequest req = new(account.Id, 1818, null);
        RunningSession session = new(account.Id, req, 555, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(70));
        tracker.RegisterSession(session);

        Assert.Contains(555, logWatcher.WatchedPids);

        // Signal ghost disconnect
        logWatcher.RaiseDisconnect(555, "Connection lost");

        Assert.Contains(555, provider.KilledPids);
    }

    [Fact]
    public void GhostDisconnectDetected_WhenNotifyOnly_DoesNotKillProcess()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        FakeServerBrowserService serverBrowser = new();
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { GhostDisconnectMode = Core.Processes.GhostDisconnectMode.NotifyOnly }
        };
        FakeLogWatcher logWatcher = new();

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            logWatcher);

        LaunchRequest req = new(account.Id, 1818, null);
        RunningSession session = new(account.Id, req, 555, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(70));
        tracker.RegisterSession(session);

        bool eventRaised = false;
        tracker.GhostDisconnectOccurred += (s, r) => eventRaised = true;

        logWatcher.RaiseDisconnect(555, "Connection lost");

        Assert.True(eventRaised);
        Assert.DoesNotContain(555, provider.KilledPids);
    }

    [Fact]
    public void GhostDisconnectDetected_WhenDisabled_DoesNotKillProcess()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);
        FakeProcessProvider provider = new();
        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeLauncher launcher = new();
        FakeServerBrowserService serverBrowser = new();
        FakeSettingsService settingsService = new()
        {
            CurrentSettings = Core.Settings.AppSettings.Default with { GhostDisconnectMode = Core.Processes.GhostDisconnectMode.Off }
        };
        FakeLogWatcher logWatcher = new();

        using SessionTracker tracker = new(
            provider,
            store,
            auth,
            launcher,
            serverBrowser,
            settingsService,
            logWatcher);

        LaunchRequest req = new(account.Id, 1818, null);
        RunningSession session = new(account.Id, req, 556, DateTimeOffset.UtcNow);
        tracker.RegisterSession(session);

        // Signal ghost disconnect
        logWatcher.RaiseDisconnect(556, "Connection lost");

        Assert.DoesNotContain(556, provider.KilledPids);
    }
}
