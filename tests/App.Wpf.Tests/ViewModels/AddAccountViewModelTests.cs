namespace RbxManager.App.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RbxManager.App.Resources;
using RbxManager.App.Services;
using RbxManager.App.ViewModels;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Network;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Xunit;

public sealed class AddAccountViewModelTests
{
    private sealed class FakeAccountStore : IAccountStore
    {
        public List<Account> StoredAccounts { get; } = new();
        public string VaultPath => "memory://vault";

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
        {
            IReadOnlyList<Account> copy = StoredAccounts.ConvertAll(a => a with { Cookie = a.Cookie.Clone() });
            return Task.FromResult(copy);
        }

        public Task<Result> AddAsync(Account account, CancellationToken ct = default)
        {
            StoredAccounts.Add(account with { Cookie = account.Cookie.Clone() });
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateAsync(Account account, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task RemoveAsync(AccountId id, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void OpenIncognitoBrowserCommand_InvokesBrowserLauncher()
    {
        FakeAccountStore store = new();
        IRobloxAuthService auth = Substitute.For<IRobloxAuthService>();
        bool launched = false;

        AddAccountViewModel vm = new(store, auth, browserLauncher: () =>
        {
            launched = true;
            return true;
        });

        vm.OpenIncognitoBrowserCommand.Execute(null);
        Assert.True(launched);
        Assert.False(vm.HasErrorMessage);
    }

    [Fact]
    public void IncognitoBrowserLauncher_IncludesDpiBypassFlagsInBrowserArguments()
    {
        string expectedIncognito = "--incognito --disable-quic --disable-features=EncryptedClientHello,Kyber,PostQuantumCECPQ2 \"https://www.roblox.com/NewLogin\"";
        string expectedInPrivate = "--inprivate --disable-quic --disable-features=EncryptedClientHello,Kyber,PostQuantumCECPQ2 \"https://www.roblox.com/NewLogin\"";

        Assert.Equal(expectedIncognito, IncognitoBrowserLauncher.BuildIncognitoArguments());
        Assert.Equal(expectedInPrivate, IncognitoBrowserLauncher.BuildInPrivateArguments());

        foreach (var (cmdName, args) in IncognitoBrowserLauncher.GetPathBrowserCommands())
        {
            string expected = cmdName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
                ? expectedInPrivate
                : expectedIncognito;
            Assert.Equal(expected, args);
        }

        foreach (var (exePath, args) in IncognitoBrowserLauncher.GetCandidateBrowserPaths())
        {
            string expected = exePath.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase)
                ? expectedInPrivate
                : expectedIncognito;
            Assert.Equal(expected, args);
        }
    }

    [Fact]
    public async Task ImportAccountsAsync_ValidatesAndStoresMultipleAccounts()
    {
        FakeAccountStore store = new();
        IRobloxAuthService auth = Substitute.For<IRobloxAuthService>();
        ISnackbarService snackbar = Substitute.For<ISnackbarService>();

        int callIndex = 0;
        auth.ValidateCookieAsync(Arg.Any<SecretValue>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                int current = Interlocked.Increment(ref callIndex);
                return current switch
                {
                    1 => Task.FromResult(Result.Success(new RobloxUser(101, "AlphaUser", "Alpha"))),
                    2 => Task.FromResult(Result.Failure<RobloxUser>("Invalid cookie", "Unauthorized")),
                    _ => Task.FromResult(Result.Success(new RobloxUser(103, "GammaUser", "Gamma")))
                };
            });

        AddAccountViewModel vm = new(store, auth, snackbar, browserLauncher: () => true)
        {
            RawCookiesText = """
                _|WARNING:-DO-NOT-SHARE-THIS._|FAKE_COOKIE_1
                User2:Pass2:_|WARNING:-DO-NOT-SHARE-THIS._|FAKE_COOKIE_2
                User3:Pass3:_|WARNING:-DO-NOT-SHARE-THIS._|FAKE_COOKIE_3
                """
        };

        bool? closedWithResult = null;
        vm.RequestClose += r => closedWithResult = r;

        await vm.ImportAccountsCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.ImportedCount);
        Assert.Equal(2, store.StoredAccounts.Count);
        Assert.Equal("AlphaUser", store.StoredAccounts[0].Alias);
        Assert.Equal("GammaUser", store.StoredAccounts[1].Alias);
        Assert.True(closedWithResult);
    }

    [Fact]
    public async Task InstallAndStartGoodbyeDpiCommand_WhenSuccessful_UpdatesStateAndDisablesButton()
    {
        FakeAccountStore store = new();
        IRobloxAuthService auth = Substitute.For<IRobloxAuthService>();
        IDpiToolService dpiService = Substitute.For<IDpiToolService>();
        dpiService.IsRunning().Returns(false);
        dpiService.EnsureInstalledAndStartAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success()));

        AddAccountViewModel vm = new(store, auth, dpiToolService: dpiService);

        Assert.False(vm.IsGoodbyeDpiRunning);
        Assert.True(vm.CanStartGoodbyeDpi);
        Assert.Equal(Strings.InstallAndStartGoodbyeDpi, vm.GoodbyeDpiButtonText);

        await vm.InstallAndStartGoodbyeDpiCommand.ExecuteAsync(null);

        Assert.True(vm.IsGoodbyeDpiRunning);
        Assert.False(vm.CanStartGoodbyeDpi);
        Assert.Equal(Strings.GoodbyeDpiActive, vm.GoodbyeDpiButtonText);
    }

    [Fact]
    public async Task InstallAndStartGoodbyeDpiCommand_WhenUacDenied_ShowsLocalizedError()
    {
        FakeAccountStore store = new();
        IRobloxAuthService auth = Substitute.For<IRobloxAuthService>();
        ISnackbarService snackbar = Substitute.For<ISnackbarService>();
        IDpiToolService dpiService = Substitute.For<IDpiToolService>();
        dpiService.IsRunning().Returns(false);
        dpiService.EnsureInstalledAndStartAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure("UAC denied", DpiErrorCodes.UacDenied)));

        AddAccountViewModel vm = new(store, auth, snackbar, dpiToolService: dpiService);

        await vm.InstallAndStartGoodbyeDpiCommand.ExecuteAsync(null);

        Assert.False(vm.IsGoodbyeDpiRunning);
        Assert.True(vm.CanStartGoodbyeDpi);
        Assert.Equal(Strings.GoodbyeDpiUacDenied, vm.ErrorMessage);
    }
}
