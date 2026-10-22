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
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Launch;

public sealed class RobloxLauncherInstanceGateTests
{
    private sealed class FakeProcessProvider : IProcessProvider
    {
        public List<ProcessSnapshot> Processes { get; set; } = [];

        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => Processes;

        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public bool Installed { get; set; } = true;
        public string? StartedUri { get; private set; }
        public Exception? ExceptionToThrowOnStart { get; set; }

        public bool IsRobloxPlayerInstalled() => Installed;

        public bool Start(string uri)
        {
            if (ExceptionToThrowOnStart != null)
            {
                throw ExceptionToThrowOnStart;
            }

            StartedUri = uri;
            return true;
        }
    }

    private sealed class FakeInstanceGate : IInstanceGate
    {
        public bool IsMultiInstanceEnabled { get; set; }
        public bool IsAvailable => true;
        public Result TryEnable() => Result.Success();
        public void Disable() => IsMultiInstanceEnabled = false;
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly List<Account> _accounts = [];

        public string VaultPath => "memory://fake_vault.dat";

        public FakeAccountStore(params Account[] accounts)
        {
            _accounts.AddRange(accounts);
        }

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Account>>(_accounts);

        public Task<Result> AddAsync(Account account, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task RemoveAsync(AccountId id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RobloxUser(123, "User", "User")));

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(SecretValue.FromString("test-ticket")));
    }

    [Fact]
    public async Task LaunchAsync_GateDisabledAndProcessRunning_BlocksLaunch()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 50000)]
        };
        FakeInstanceGate gate = new() { IsMultiInstanceEnabled = false };

        using RobloxLauncher launcher = new(store, auth, starter, provider, gate);
        LaunchRequest request = new(account.Id, 1818, null);

        Result result = await launcher.LaunchAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("RobloxAlreadyRunning", result.ErrorCode);
        Assert.Null(starter.StartedUri);
    }

    [Fact]
    public async Task LaunchAsync_GateEnabledAndProcessRunning_AllowsLaunch()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider provider = new()
        {
            Processes = [new ProcessSnapshot(1001, DateTimeOffset.UtcNow, 50000)]
        };
        FakeInstanceGate gate = new() { IsMultiInstanceEnabled = true };

        using RobloxLauncher launcher = new(store, auth, starter, provider, gate);
        LaunchRequest request = new(account.Id, 1818, null);

        Result result = await launcher.LaunchAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(starter.StartedUri);
    }

    [Fact]
    public async Task LaunchAsync_GateDisabledAndNoProcessRunning_AllowsLaunch()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider provider = new()
        {
            Processes = []
        };
        FakeInstanceGate gate = new() { IsMultiInstanceEnabled = false };

        using RobloxLauncher launcher = new(store, auth, starter, provider, gate);
        LaunchRequest request = new(account.Id, 1818, null);

        Result result = await launcher.LaunchAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(starter.StartedUri);
    }

    [Fact]
    public async Task LaunchAsync_WhenStarterThrowsWin32Exception1155_MapsToRobloxNotInstalledOrRegistered()
    {
        using SecretValue cookie = SecretValue.FromString("TEST_COOKIE_123");
        Account account = new(new AccountId(1), "Alias1", null, null, cookie, DateTimeOffset.UtcNow);

        FakeAccountStore store = new(account);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new()
        {
            ExceptionToThrowOnStart = new System.ComponentModel.Win32Exception(1155)
        };
        FakeProcessProvider provider = new()
        {
            Processes = []
        };
        FakeInstanceGate gate = new() { IsMultiInstanceEnabled = false };

        using RobloxLauncher launcher = new(store, auth, starter, provider, gate);
        LaunchRequest request = new(account.Id, 1818, null);

        Result result = await launcher.LaunchAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("RobloxNotInstalledOrRegistered", result.ErrorCode);
    }
}
