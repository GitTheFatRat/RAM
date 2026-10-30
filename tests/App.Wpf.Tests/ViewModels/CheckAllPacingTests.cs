namespace RbxManager.App.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RbxManager.App.Tests.Helpers;
using RbxManager.App.ViewModels;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;
using Wpf.Ui;

public sealed class CheckAllPacingTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly List<FakeTimer> _timers = new();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            TimeSpan remaining = delta;
            while (remaining > TimeSpan.Zero)
            {
                TimeSpan step = remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining;
                _utcNow += step;
                remaining -= step;
                foreach (FakeTimer timer in _timers.ToArray())
                {
                    timer.CheckTrigger(_utcNow);
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            FakeTimer timer = new(this, callback, state, dueTime, period, _utcNow);
            _timers.Add(timer);
            return timer;
        }

        internal void RemoveTimer(FakeTimer timer) => _timers.Remove(timer);

        internal sealed class FakeTimer : ITimer
        {
            private readonly FakeTimeProvider _provider;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private DateTimeOffset _nextTrigger;
            private TimeSpan _period;
            private bool _disposed;

            public FakeTimer(FakeTimeProvider provider, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
            {
                _provider = provider;
                _callback = callback;
                _state = state;
                _period = period;
                _nextTrigger = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + dueTime;
            }

            public void CheckTrigger(DateTimeOffset now)
            {
                if (!_disposed && now >= _nextTrigger)
                {
                    _callback(_state);
                    if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
                    {
                        _nextTrigger += _period;
                    }
                    else
                    {
                        _nextTrigger = DateTimeOffset.MaxValue;
                    }
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                _period = period;
                _nextTrigger = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : _provider.GetUtcNow() + dueTime;
                return true;
            }

            public void Dispose()
            {
                _disposed = true;
                _provider.RemoveTimer(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

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
        public List<DateTimeOffset> ValidationTimes { get; } = new();
        private readonly TimeProvider _timeProvider;

        public FakeAuthService(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
        {
            ValidationTimes.Add(_timeProvider.GetUtcNow());
            return Task.FromResult(Result.Success(new RobloxUser(1234, "ValidUser", "ValidUser")));
        }

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(SecretValue.FromString("ticket")));
    }

    private sealed class FakeLauncher : ILauncher
    {
        public List<(AccountId AccountId, DateTimeOffset LaunchTime)> LaunchedAccounts { get; } = new();
        private readonly TimeProvider? _timeProvider;

        public FakeLauncher(TimeProvider? timeProvider = null)
        {
            _timeProvider = timeProvider;
        }

        public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            LaunchedAccounts.Add((request.AccountId, _timeProvider?.GetUtcNow() ?? DateTimeOffset.UtcNow));
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public void ExecuteSequentialLaunchQueueAsync_RunsSequentiallyWith60To120SecondRandomizedDelay()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            using SecretValue c3 = SecretValue.FromString("fake-cookie-3");

            Account a1 = new(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow);
            Account a2 = new(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow);
            Account a3 = new(new AccountId(3), "A3", null, null, c3, DateTimeOffset.UtcNow);

            FakeAccountStore store = new(a1, a2, a3);
            FakeAuthService auth = new(timeProvider);
            FakeLauncher launcher = new(timeProvider);
            IContentDialogService dialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            Queue<int> delays = new();
            delays.Enqueue(65);
            delays.Enqueue(115);
            List<(int Min, int MaxExclusive)> requestedRanges = new();
            RbxManager.Core.Instance.IInstanceGate instanceGate = Substitute.For<RbxManager.Core.Instance.IInstanceGate>();
            instanceGate.IsMultiInstanceEnabled.Returns(false);
            instanceGate.TryEnable().Returns(Result.Success());

            AccountsViewModel vm = new(
                store,
                dialogService,
                snackbarService,
                auth,
                launcher,
                timeProvider: timeProvider,
                randomDelayGenerator: (min, maxExclusive) =>
                {
                    requestedRanges.Add((min, maxExclusive));
                    return delays.Dequeue();
                },
                instanceGate: instanceGate);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            vm.FilteredAccounts[0].IsSelected = true;
            vm.FilteredAccounts[1].IsSelected = true;
            vm.FilteredAccounts[2].IsSelected = true;
            vm.SelectedAccount = vm.FilteredAccounts[2]; // Last clicked row

            // Reloading accounts must preserve IsSelected for all 3 checked accounts
            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            List<AccountItemViewModel> selectedQueue = vm.GetSelectedAccountsInQueueOrder();
            Assert.Equal(3, selectedQueue.Count);
            Assert.True(vm.CanLaunchAccount);
            Assert.True(vm.IsNotBusy);

            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(selectedQueue, 1818);

            // Multi-instance gate must be enabled before launching the queue
            instanceGate.Received(1).TryEnable();
            Assert.StartsWith("Queued 3 accounts for launch...", vm.BulkLaunchQueueText, StringComparison.Ordinal);

            // Account 1 launched immediately at T = 0; buttons disabled during sequence
            Assert.Single(launcher.LaunchedAccounts);
            Assert.Equal(new AccountId(1), launcher.LaunchedAccounts[0].AccountId);
            Assert.True(vm.IsBulkLaunching);
            Assert.False(vm.CanLaunchAccount);
            Assert.False(vm.IsNotBusy);
            Assert.Single(requestedRanges);
            Assert.Equal((60, 121), requestedRanges[0]);

            // Advance 64s: still waiting for the 65s randomized delay, and countdown ticked down to 1s
            Assert.Contains("65", vm.BulkLaunchCountdownText, StringComparison.Ordinal);
            timeProvider.Advance(TimeSpan.FromSeconds(64));
            Assert.Single(launcher.LaunchedAccounts);
            Assert.Contains("1", vm.BulkLaunchCountdownText, StringComparison.Ordinal);

            // Advance 1s (total 65s): Account 2 launched, second delay (115s) scheduled
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(2, launcher.LaunchedAccounts.Count);
            Assert.Equal(new AccountId(2), launcher.LaunchedAccounts[1].AccountId);
            Assert.Equal(2, requestedRanges.Count);
            Assert.Equal((60, 121), requestedRanges[1]);

            // Advance 115s: Account 3 launched; no delay after the last account
            timeProvider.Advance(TimeSpan.FromSeconds(115));
            Assert.Equal(3, launcher.LaunchedAccounts.Count);
            Assert.Equal(new AccountId(3), launcher.LaunchedAccounts[2].AccountId);
            Assert.Equal(2, requestedRanges.Count);

            launchTask.GetAwaiter().GetResult();
            Assert.False(vm.IsBulkLaunching);
            Assert.True(vm.CanLaunchAccount);
            Assert.True(vm.IsNotBusy);
        });
    }

    private sealed class GatedLauncher : ILauncher
    {
        public List<AccountId> LaunchedAccounts { get; } = new();
        // Inline continuations keep the queue on the StaRunner thread; async continuations would Dispatcher.Invoke
        // back onto that thread while it is blocked in GetResult() and deadlock the shared STA runner.
        public TaskCompletionSource<Result> Gate { get; } = new();

        public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            LaunchedAccounts.Add(request.AccountId);
            return Gate.Task;
        }
    }

    // Cancellation resumes the queue on a pool thread, which posts UI resets to the STA dispatcher; drain them.
    private static void FlushDispatcher()
    {
        System.Windows.Threading.DispatcherFrame frame = new();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static AccountsViewModel CreateQueueVm(TimeProvider timeProvider, ILauncher launcher, params Account[] accounts)
    {
        return new AccountsViewModel(
            new FakeAccountStore(accounts),
            Substitute.For<IContentDialogService>(),
            Substitute.For<ISnackbarService>(),
            new FakeAuthService(timeProvider),
            launcher,
            timeProvider: timeProvider,
            randomDelayGenerator: (_, _) => 90);
    }

    [Fact]
    public void CancelBulkLaunch_DuringRandomizedDelay_StopsQueueWithoutLaunchingRemaining()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            FakeLauncher launcher = new(timeProvider);
            AccountsViewModel vm = CreateQueueVm(
                timeProvider,
                launcher,
                new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow));

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);
            Assert.Single(launcher.LaunchedAccounts);
            Assert.True(vm.IsBulkLaunching);

            vm.CancelBulkLaunch();
            launchTask.GetAwaiter().GetResult();
            timeProvider.Advance(TimeSpan.FromSeconds(200));
            FlushDispatcher();

            Assert.Single(launcher.LaunchedAccounts);
            Assert.False(vm.IsBulkLaunching);
            Assert.Equal(string.Empty, vm.BulkLaunchCountdownText);
        });
    }

    [Fact]
    public void Dispose_WhileLaunchInFlight_CancelsQueueWithoutThrowing()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            GatedLauncher launcher = new();
            AccountsViewModel vm = CreateQueueVm(
                timeProvider,
                launcher,
                new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow));

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);
            Assert.Single(launcher.LaunchedAccounts);

            // Regression: Dispose used to null the CTS while the loop was awaiting LaunchAsync → NullReferenceException.
            vm.Dispose();
            launcher.Gate.SetResult(Result.Success());
            launchTask.GetAwaiter().GetResult();

            Assert.Single(launcher.LaunchedAccounts);
            Assert.False(vm.IsBulkLaunching);
        });
    }

    [Fact]
    public void CheckAll_RunsSequentiallyWithTwoSecondGap()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            using SecretValue c3 = SecretValue.FromString("fake-cookie-3");

            Account a1 = new(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow);
            Account a2 = new(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow);
            Account a3 = new(new AccountId(3), "A3", null, null, c3, DateTimeOffset.UtcNow);

            FakeAccountStore store = new(a1, a2, a3);
            FakeAuthService auth = new(timeProvider);
            FakeLauncher launcher = new();
            IContentDialogService dialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            AccountsViewModel vm = new(
                store,
                dialogService,
                snackbarService,
                auth,
                launcher,
                timeProvider: timeProvider);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();

            Task checkTask = vm.CheckAllAsync();

            // Account 1 validated at T = 0
            Assert.Single(auth.ValidationTimes);
            Assert.True(vm.IsCheckingAll);
            Assert.Equal("1 / 3", vm.CheckAllProgressText);

            // Advance 1s: still waiting on the 2s delay
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            Assert.Single(auth.ValidationTimes);

            // Advance another 1s (total 2s): Account 2 validated
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(2, auth.ValidationTimes.Count);
            Assert.Equal("2 / 3", vm.CheckAllProgressText);

            // Advance 2s: Account 3 validated
            timeProvider.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(3, auth.ValidationTimes.Count);

            checkTask.GetAwaiter().GetResult();
            Assert.False(vm.IsCheckingAll);
        });
    }

    [Fact]
    public void CancelCheckAll_CancelsRemainingAccountsPromptly()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            using SecretValue c3 = SecretValue.FromString("fake-cookie-3");

            Account a1 = new(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow);
            Account a2 = new(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow);
            Account a3 = new(new AccountId(3), "A3", null, null, c3, DateTimeOffset.UtcNow);

            FakeAccountStore store = new(a1, a2, a3);
            FakeAuthService auth = new(timeProvider);
            FakeLauncher launcher = new();
            IContentDialogService dialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            AccountsViewModel vm = new(
                store,
                dialogService,
                snackbarService,
                auth,
                launcher,
                timeProvider: timeProvider);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();

            Task checkTask = vm.CheckAllAsync();
            Assert.Single(auth.ValidationTimes);

            // Cancel during the 2-second delay
            vm.CancelCheckAll();

            checkTask.GetAwaiter().GetResult();

            // No further accounts validated
            Assert.Single(auth.ValidationTimes);
            Assert.False(vm.IsCheckingAll);
        });
    }

    [Fact]
    public void SettingsViewModel_MultiInstanceToggle_EnablesMutexGateAndPersistsSetting()
    {
        StaRunner.Run(() =>
        {
            RbxManager.Core.Settings.ISettingsService settingsService = Substitute.For<RbxManager.Core.Settings.ISettingsService>();
            RbxManager.Core.Settings.AppSettings currentSettings = RbxManager.Core.Settings.AppSettings.Default with
            {
                MultiInstanceAcknowledged = true
            };
            settingsService.CurrentSettings.Returns(_ => currentSettings);
            settingsService.UpdateSettingsAsync(Arg.Any<Func<RbxManager.Core.Settings.AppSettings, RbxManager.Core.Settings.AppSettings>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    currentSettings = call.Arg<Func<RbxManager.Core.Settings.AppSettings, RbxManager.Core.Settings.AppSettings>>()(currentSettings);
                    return Task.FromResult(currentSettings);
                });

            RbxManager.Core.Instance.IInstanceGate instanceGate = Substitute.For<RbxManager.Core.Instance.IInstanceGate>();
            instanceGate.IsAvailable.Returns(true);
            instanceGate.TryEnable().Returns(Result.Success());

            IContentDialogService dialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            SettingsViewModel vm = new(settingsService, instanceGate, dialogService, snackbarService);
            Assert.True(vm.IsMultiInstanceAvailable);

            vm.MultiInstanceToggledCommand.ExecuteAsync(true).GetAwaiter().GetResult();

            Assert.True(vm.MultiInstanceEnabled);
            Assert.True(currentSettings.IsMultiInstanceEnabled);
            instanceGate.Received(1).TryEnable();

            vm.MultiInstanceToggledCommand.ExecuteAsync(false).GetAwaiter().GetResult();

            Assert.False(vm.MultiInstanceEnabled);
            Assert.False(currentSettings.IsMultiInstanceEnabled);
            instanceGate.Received(1).Disable();
        });
    }

    [Fact]
    public void AccountItemViewModel_ActiveProcessId_UpdatesIsRunningAndKillResetsIt()
    {
        using SecretValue cookie = SecretValue.FromString("fake-cookie-1");
        Account account = new(new AccountId(1), "A1", null, null, cookie, DateTimeOffset.UtcNow);
        using AccountItemViewModel item = new(account);

        Assert.False(item.IsRunning);
        Assert.Null(item.ActiveProcessId);

        item.ActiveProcessId = 99999999; // Non-existent PID: KillInstance should safely catch and reset
        if (item.IsRunning)
        {
            item.KillInstanceCommand.Execute(null);
        }

        Assert.False(item.IsRunning);
        Assert.Null(item.ActiveProcessId);
        Assert.Null(account.ActiveProcessId);
    }

    [Fact]
    public void SettingsViewModel_CustomResolution_LoadsAndPersistsSettings()
    {
        StaRunner.Run(() =>
        {
            RbxManager.Core.Settings.ISettingsService settingsService = Substitute.For<RbxManager.Core.Settings.ISettingsService>();
            RbxManager.Core.Settings.AppSettings currentSettings = RbxManager.Core.Settings.AppSettings.Default;
            settingsService.CurrentSettings.Returns(_ => currentSettings);
            settingsService.UpdateSettingsAsync(Arg.Any<Func<RbxManager.Core.Settings.AppSettings, RbxManager.Core.Settings.AppSettings>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    currentSettings = call.Arg<Func<RbxManager.Core.Settings.AppSettings, RbxManager.Core.Settings.AppSettings>>()(currentSettings).Clamp();
                    return Task.FromResult(currentSettings);
                });

            RbxManager.Core.Instance.IInstanceGate instanceGate = Substitute.For<RbxManager.Core.Instance.IInstanceGate>();
            IContentDialogService dialogService = Substitute.For<IContentDialogService>();
            ISnackbarService snackbarService = Substitute.For<ISnackbarService>();

            SettingsViewModel vm = new(settingsService, instanceGate, dialogService, snackbarService);
            Assert.False(vm.EnableCustomResolution);
            Assert.Equal(800, vm.ResolutionWidth);
            Assert.Equal(600, vm.ResolutionHeight);

            vm.EnableCustomResolution = true;
            vm.ResolutionWidth = 200;
            vm.ResolutionHeight = 400;

            // Dimension edits are debounced: nothing is written until the quiet period ends or a flush happens.
            Assert.True(currentSettings.EnableCustomResolution);
            Assert.Equal(800, currentSettings.ResolutionWidth);
            Assert.Equal(600, currentSettings.ResolutionHeight);

            vm.FlushPendingAsync().GetAwaiter().GetResult();

            Assert.True(currentSettings.EnableCustomResolution);
            Assert.Equal(200, currentSettings.ResolutionWidth);
            Assert.Equal(400, currentSettings.ResolutionHeight);

            // Simulate navigating away and returning to Settings tab (new ViewModel instance):
            SettingsViewModel vmAfterNavigation = new(settingsService, instanceGate, dialogService, snackbarService);
            Assert.True(vmAfterNavigation.EnableCustomResolution);
            Assert.Equal(200, vmAfterNavigation.ResolutionWidth);
            Assert.Equal(400, vmAfterNavigation.ResolutionHeight);
        });
    }

    // M44 R1: after the user presses Cancel, a non-cancellation error from the in-flight account (vault IO, recovery)
    // must still be isolated — it must not escape the async command as an unhandled "fatal error".
    [Fact]
    public void ExecuteSequentialLaunchQueueAsync_WhenAccountThrowsIoAfterUserCancel_EndsCleanly()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            ILauncher launcher = Substitute.For<ILauncher>();
            AccountsViewModel? vm = null;
            List<AccountId> launchedIds = [];
            launcher.LaunchAsync(Arg.Any<LaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    launchedIds.Add(call.Arg<LaunchRequest>().AccountId);
                    vm!.CancelBulkLaunch();
                    return Task.FromException<Result>(new System.IO.IOException("vault.dat is locked by another process"));
                });

            vm = new AccountsViewModel(
                new FakeAccountStore(
                    new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                    new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow)),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                new FakeAuthService(timeProvider),
                launcher,
                timeProvider: timeProvider,
                randomDelayGenerator: (_, _) => 5);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);
            for (int i = 0; i < 10 && !launchTask.IsCompleted; i++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            Exception? escaped = Record.Exception(() => launchTask.GetAwaiter().GetResult());
            Assert.Null(escaped);
            Assert.Equal([new AccountId(1)], launchedIds);
            Assert.False(vm.IsBulkLaunching);
        });
    }

    // M44 Flow 3: an HTTP timeout surfaces as TaskCanceledException while the user has NOT cancelled the queue.
    [Fact]
    public void ExecuteSequentialLaunchQueueAsync_WhenAccountThrowsNonUserTaskCanceled_ContinuesWithNextAccount()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            using SecretValue c3 = SecretValue.FromString("fake-cookie-3");
            ILauncher launcher = Substitute.For<ILauncher>();
            List<AccountId> launchedIds = [];
            launcher.LaunchAsync(Arg.Any<LaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    AccountId accountId = call.Arg<LaunchRequest>().AccountId;
                    launchedIds.Add(accountId);
                    return accountId.Value == 2
                        ? Task.FromException<Result>(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"))
                        : Task.FromResult(Result.Success());
                });

            AccountsViewModel vm = new(
                new FakeAccountStore(
                    new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                    new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow),
                    new Account(new AccountId(3), "A3", null, null, c3, DateTimeOffset.UtcNow)),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                new FakeAuthService(timeProvider),
                launcher,
                timeProvider: timeProvider,
                randomDelayGenerator: (_, _) => 5);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);

            for (int i = 0; i < 30 && !launchTask.IsCompleted; i++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            launchTask.GetAwaiter().GetResult();
            Assert.Equal([new AccountId(1), new AccountId(2), new AccountId(3)], launchedIds);
        });
    }

    // M44 Flow 3: wait after HTTP 429 must be Math.Max(normalGap, backoff) — here gap 90s > backoff 30s.
    [Fact]
    public void ExecuteSequentialLaunchQueueAsync_WhenPacingExceedsBackoff_WaitsTheLongerPacingGap()
    {
        StaRunner.Run(() =>
        {
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            ILauncher launcher = Substitute.For<ILauncher>();
            List<AccountId> launchedIds = [];
            launcher.LaunchAsync(Arg.Any<LaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    AccountId accountId = call.Arg<LaunchRequest>().AccountId;
                    launchedIds.Add(accountId);
                    return accountId.Value == 1
                        ? Task.FromResult(Result.Failure("Too many requests", AuthErrorCodes.RateLimited))
                        : Task.FromResult(Result.Success());
                });

            AccountsViewModel vm = new(
                new FakeAccountStore(
                    new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                    new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow)),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                new FakeAuthService(timeProvider),
                launcher,
                timeProvider: timeProvider,
                randomDelayGenerator: (_, _) => 90);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);

            Assert.Contains("90", vm.BulkLaunchCountdownText, StringComparison.Ordinal);
            for (int i = 0; i < 89; i++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            Assert.Equal([new AccountId(1)], launchedIds);

            timeProvider.Advance(TimeSpan.FromSeconds(1));
            launchTask.GetAwaiter().GetResult();
            Assert.Equal([new AccountId(1), new AccountId(2)], launchedIds);
        });
    }

    [Fact]
    public void ExecuteSequentialLaunchQueueAsync_WhenRateLimitedOrModerated_SurvivesAndResumesAfterBackoff()
    {
        StaRunner.Run(() =>
        {
            RbxManager.App.Services.RobloxWindowSizer.ClearOverrides();
            FakeTimeProvider timeProvider = new();
            using SecretValue c1 = SecretValue.FromString("fake-cookie-1");
            using SecretValue c2 = SecretValue.FromString("fake-cookie-2");
            using SecretValue c3 = SecretValue.FromString("fake-cookie-3");

            RbxManager.Core.Settings.ISettingsService settingsService = Substitute.For<RbxManager.Core.Settings.ISettingsService>();
            RbxManager.Core.Settings.AppSettings globalSettings = RbxManager.Core.Settings.AppSettings.Default with
            {
                EnableCustomResolution = true,
                ResolutionWidth = 200,
                ResolutionHeight = 200
            };
            settingsService.CurrentSettings.Returns(_ => globalSettings);

            ILauncher launcher = Substitute.For<ILauncher>();
            List<AccountId> launchedIds = [];
            int account1Attempts = 0;
            launcher.LaunchAsync(Arg.Any<LaunchRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    AccountId accountId = call.Arg<LaunchRequest>().AccountId;
                    launchedIds.Add(accountId);
                    if (accountId.Value == 1)
                    {
                        return ++account1Attempts == 1
                            ? Task.FromResult(Result.Failure("User is moderated", AuthErrorCodes.Moderated))
                            : Task.FromResult(Result.Success());
                    }

                    return accountId.Value switch
                    {
                        2 => Task.FromResult(Result.Failure("Too many requests", AuthErrorCodes.RateLimited)),
                        _ => Task.FromResult(Result.Success())
                    };
                });

            AccountsViewModel vm = new(
                new FakeAccountStore(
                    new Account(new AccountId(1), "A1", null, null, c1, DateTimeOffset.UtcNow),
                    new Account(new AccountId(2), "A2", null, null, c2, DateTimeOffset.UtcNow),
                    new Account(new AccountId(3), "A3", null, null, c3, DateTimeOffset.UtcNow)),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                new FakeAuthService(timeProvider),
                launcher,
                settingsService: settingsService,
                timeProvider: timeProvider,
                randomDelayGenerator: (_, _) => 5);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            AccountItemViewModel item1Before = vm.FilteredAccounts[0];

            Task launchTask = vm.ExecuteSequentialLaunchQueueAsync(vm.FilteredAccounts, 1818);

            // Account 1 fails initial validation with Moderated -> DO NOT skip; triggers recovery launch,
            // sets status text to "Đang chờ xử lý lỗi...", and overrides RobloxWindowSizer to 800x600 without changing global AppSettings.
            Assert.Equal([new AccountId(1), new AccountId(1)], launchedIds);
            Assert.Same(item1Before, vm.FilteredAccounts[0]);
            Assert.Equal("Đang chờ xử lý lỗi...", item1Before.StatusText);

            var (enabled1, width1, height1, _) = RbxManager.App.Services.RobloxWindowSizer.ResolveTargetResolution(
                settingsService,
                processId: 424242,
                accountId: new AccountId(1));
            Assert.True(enabled1);
            Assert.Equal(800, width1);
            Assert.Equal(600, height1);

            // Subsequent normal account must receive global 200x200 resolution, not a leaked 800x600 pending override
            var (enabled3, width3, height3, _) = RbxManager.App.Services.RobloxWindowSizer.ResolveTargetResolution(
                settingsService,
                processId: 434343,
                accountId: new AccountId(3));
            Assert.True(enabled3);
            Assert.Equal(200, width3);
            Assert.Equal(200, height3);

            // Global AppSettings remain untouched at 200x200
            Assert.Equal(200, settingsService.CurrentSettings.ResolutionWidth);
            Assert.Equal(200, settingsService.CurrentSettings.ResolutionHeight);

            for (int i = 0; i < 5; i++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            // Account 2 fails with RateLimited -> triggers 30s backoff countdown instead of aborting the queue.
            Assert.Equal([new AccountId(1), new AccountId(1), new AccountId(2)], launchedIds);
            Assert.True(vm.IsBulkLaunching);
            Assert.Contains("30", vm.BulkLaunchCountdownText, StringComparison.Ordinal);

            for (int i = 0; i < 29; i++)
            {
                timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            // Still waiting on the last second of the 30s rate-limit backoff.
            Assert.Equal(3, launchedIds.Count);
            Assert.Contains("1", vm.BulkLaunchCountdownText, StringComparison.Ordinal);

            timeProvider.Advance(TimeSpan.FromSeconds(1));

            // Account 3 launches and succeeds after the 30s rate-limit backoff completes.
            launchTask.GetAwaiter().GetResult();
            Assert.Equal([new AccountId(1), new AccountId(1), new AccountId(2), new AccountId(3)], launchedIds);
            Assert.False(vm.IsBulkLaunching);
            Assert.Equal(string.Empty, vm.BulkLaunchCountdownText);
        });
    }

    [Fact]
    public void AccountItemViewModel_KillInstanceCommand_NotifiesSessionTrackerToPreventZombieAutoRejoin()
    {
        StaRunner.Run(() =>
        {
            using SecretValue cookie = SecretValue.FromString("fake-cookie");
            RbxManager.Core.Sessions.ISessionTracker sessionTracker = Substitute.For<RbxManager.Core.Sessions.ISessionTracker>();
            AccountsViewModel vm = new(
                new FakeAccountStore(new Account(new AccountId(1), "A1", null, null, cookie, DateTimeOffset.UtcNow)),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                new FakeAuthService(new FakeTimeProvider()),
                Substitute.For<ILauncher>(),
                sessionTracker: sessionTracker);

            vm.LoadAccountsAsync().GetAwaiter().GetResult();
            AccountItemViewModel item = Assert.Single(vm.FilteredAccounts);
            item.ActiveProcessId = 999999;

            item.KillInstanceCommand.Execute(null);

            sessionTracker.Received(1).CloseSessionAsync(999999, graceful: false);
            Assert.Null(item.ActiveProcessId);
        });
    }
}
