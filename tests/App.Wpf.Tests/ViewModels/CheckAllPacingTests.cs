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
            _utcNow += delta;
            foreach (FakeTimer timer in _timers.ToArray())
            {
                timer.CheckTrigger(_utcNow);
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
        public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
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
}
