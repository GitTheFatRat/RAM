namespace RbxManager.Infrastructure.Tests.Storage;

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
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
using RbxManager.Infrastructure.Storage;
using RbxManager.Infrastructure.Tests.TestHelpers;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class CookieMemoryHygieneTests
{
    private const string RealisticCookie = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_FAKE0123456789ABCDEFSECRETTAIL";

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly IReadOnlyList<Account> _accounts;

        public string VaultPath => "fake_path";

        public FakeAccountStore(IReadOnlyList<Account> accounts)
        {
            _accounts = accounts;
        }

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(_accounts);

        public Task<Result> AddAsync(Account account, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task RemoveAsync(AccountId id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public bool IsRobloxPlayerInstalled() => true;
        public bool Start(string uri) => true;
    }

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => [];
        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RobloxUser(12345, "TestUser", "TestUser")));

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(SecretValue.FromString("FAKE_TICKET_12345")));
    }

    [Fact]
    public async Task RobloxLauncher_LaunchAsync_DisposesAllLoadedCookiesFromStore()
    {
        SecretValue cookie1 = SecretValue.FromString(RealisticCookie);
        SecretValue cookie2 = SecretValue.FromString(RealisticCookie);

        Account acc1 = new(new AccountId(1), "User1", null, null, cookie1, DateTimeOffset.UtcNow);
        Account acc2 = new(new AccountId(2), "User2", null, null, cookie2, DateTimeOffset.UtcNow);

        FakeAccountStore store = new([acc1, acc2]);
        FakeAuthService auth = new();
        FakeProcessStarter starter = new();
        FakeProcessProvider processes = new();
        DefaultInstanceGate gate = new();

        using RobloxLauncher launcher = new(store, auth, starter, processes, gate);

        Result result = await launcher.LaunchAsync(new LaunchRequest(new AccountId(1), 1818, null));

        Assert.True(result.IsSuccess);
        Assert.Throws<ObjectDisposedException>(() => cookie1.Reveal());
        Assert.Throws<ObjectDisposedException>(() => cookie2.Reveal());
    }

    [Fact]
    public async Task EncryptedFileVault_AddAsync_PreservesCallerCookie_DisposesLoadedCookies()
    {
        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath("hygiene_vault.dat");

        using EncryptedFileVault vault = new(vaultPath);

        // Add first account
        using SecretValue firstCookie = SecretValue.FromString(RealisticCookie);
        Account firstAccount = new(new AccountId(1), "User1", null, null, firstCookie, DateTimeOffset.UtcNow);
        Result add1Result = await vault.AddAsync(firstAccount);
        Assert.True(add1Result.IsSuccess);
        Assert.Equal(RealisticCookie, firstCookie.Reveal());

        // Add second account: caller owns secondCookie
        using SecretValue secondCookie = SecretValue.FromString(RealisticCookie);
        Account secondAccount = new(new AccountId(2), "User2", null, null, secondCookie, DateTimeOffset.UtcNow);
        Result add2Result = await vault.AddAsync(secondAccount);
        Assert.True(add2Result.IsSuccess);

        // Caller cookie must stay usable
        Assert.Equal(RealisticCookie, secondCookie.Reveal());
    }
}
