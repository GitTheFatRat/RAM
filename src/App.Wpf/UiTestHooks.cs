#if DEBUG
namespace RbxManager.App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Servers;
using RbxManager.Core.Storage;

internal static class UiTestHooks
{
    public static void ConfigureFakes(IServiceCollection services, string dataDir)
    {
        services.RemoveAll<IRobloxAuthService>();
        services.AddSingleton<IRobloxAuthService, FakeRobloxAuthService>();

        services.RemoveAll<IServerBrowserService>();
        services.AddSingleton<IServerBrowserService, FakeServerBrowserService>();

        services.RemoveAll<ILauncher>();
        services.AddSingleton<ILauncher, FakeLauncher>();

        services.RemoveAll<IProcessProvider>();
        services.AddSingleton<IProcessProvider, FakeProcessProvider>();

        services.RemoveAll<IDiagnosticsService>();
        services.AddSingleton<IDiagnosticsService, FakeDiagnosticsService>();

        services.RemoveAll<IInstanceGate>();
        services.AddSingleton<IInstanceGate, FakeInstanceGate>();
    }

    public static async Task EnsureSeededAsync(IServiceProvider services, string dataDir)
    {
        if (Environment.GetEnvironmentVariable("RBXMANAGER_EMPTY_VAULT") == "1")
        {
            return;
        }

        string vaultPath = Path.Combine(dataDir, "vault.dat");
        if (File.Exists(vaultPath))
        {
            return;
        }

        IAccountStore store = services.GetRequiredService<IAccountStore>();

        using SecretValue cookie1 = SecretValue.FromString("FAKE_UI_COOKIE_MAIN_01");
        Account acc1 = new(
            Id: new AccountId(1),
            Alias: "Main_Account_LongAlias_TestingBoundary",
            Group: "Main",
            Note: "Primary test account with long notes for UI boundary verification.",
            Cookie: cookie1,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-30),
            Username: "ProRobloxianPlayer99999",
            UserId: 11111111,
            Status: AccountStatus.Valid,
            LastCheckedAt: DateTimeOffset.UtcNow.AddHours(-1));

        using SecretValue cookie2 = SecretValue.FromString("FAKE_UI_COOKIE_ALT_02");
        Account acc2 = new(
            Id: new AccountId(2),
            Alias: "AltFarmer_01",
            Group: "Farming",
            Note: "Secondary farming alt.",
            Cookie: cookie2,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-14),
            Username: "AltFarmerRobot01",
            UserId: 22222222,
            Status: AccountStatus.Valid,
            LastCheckedAt: DateTimeOffset.UtcNow.AddHours(-2));

        using SecretValue cookie3 = SecretValue.FromString("FAKE_UI_COOKIE_EXPIRED_03");
        Account acc3 = new(
            Id: new AccountId(3),
            Alias: "Expired_Token_Account",
            Group: "Alt",
            Note: "Account with expired cookie for error UI testing.",
            Cookie: cookie3,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-60),
            Username: "ExpiredSessionUser",
            UserId: 33333333,
            Status: AccountStatus.Expired,
            LastCheckedAt: DateTimeOffset.UtcNow.AddDays(-1));

        using SecretValue cookie4 = SecretValue.FromString("FAKE_UI_COOKIE_UNCHECKED_04");
        Account acc4 = new(
            Id: new AccountId(4),
            Alias: "Unchecked_NoGroup",
            Group: null,
            Note: null,
            Cookie: cookie4,
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-5),
            Username: "SoloExplorer",
            UserId: 44444444,
            Status: AccountStatus.Unknown,
            LastCheckedAt: null);

        await store.AddAsync(acc1);
        await store.AddAsync(acc2);
        await store.AddAsync(acc3);
        await store.AddAsync(acc4);
    }
}

internal sealed class FakeRobloxAuthService : IRobloxAuthService
{
    public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default)
    {
        if (!cookie.HasContent)
        {
            return Task.FromResult(Result.Failure<RobloxUser>("Cookie cannot be empty."));
        }
        return Task.FromResult(Result.Success(new RobloxUser(9999001, "UiTestUser", "UI Test Agent")));
    }

    public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, ProxyConfig? proxy = null, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Success(SecretValue.FromString("fake-test-ticket-9999001")));
    }
}

internal sealed class FakeServerBrowserService : IServerBrowserService
{
    public Task<Result<ServerPage>> GetServersAsync(long placeId, ServerSort sort = ServerSort.Descending, string? cursor = null, CancellationToken ct = default)
    {
        if (placeId == 666666)
        {
            return Task.FromResult(Result.Failure<ServerPage>("HTTP 500: Server error fetching place instances."));
        }
        if (placeId == 999999)
        {
            return Task.FromResult(Result.Success(new ServerPage(Array.Empty<ServerInfo>(), null)));
        }
        List<ServerInfo> servers = new()
        {
            new ServerInfo("srv-fake-alpha-01", 14, 20, 60.0, 32),
            new ServerInfo("srv-fake-beta-02", 19, 20, 58.2, 45),
            new ServerInfo("srv-fake-gamma-03", 5, 20, 59.9, 110)
        };
        return Task.FromResult(Result.Success(new ServerPage(servers, null)));
    }
}

internal sealed class FakeLauncher : ILauncher
{
    public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Success());
    }
}

internal sealed class FakeProcessProvider : IProcessProvider
{
    public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses()
    {
        return new List<ProcessSnapshot>
        {
            new ProcessSnapshot(12340, DateTimeOffset.UtcNow.AddMinutes(-45), 256 * 1024 * 1024),
            new ProcessSnapshot(12344, DateTimeOffset.UtcNow.AddMinutes(-15), 384 * 1024 * 1024)
        };
    }

    public bool Kill(int pid, bool graceful) => true;
    public bool IsProcessRunning(string processName) => true;
}

internal sealed class FakeDiagnosticsService : IDiagnosticsService
{
    public Task<string> RunDiagnosticsAsync(Account? targetAccount = null, CancellationToken ct = default)
    {
        string report = """
            === RBXMANAGER DIAGNOSTICS REPORT (OFFLINE UI TEST) ===
            Generated: Offline Test Mode
            Roblox Client: Installed (Fake Registry: C:\Roblox\Versions\version-fake\RobloxPlayerBeta.exe)
            Multi-Instance Gate: OK (Simulated Active)
            Roblox Processes: 2 running (PIDs: 12340, 12344)
            Authentication Check: OK (Valid UI test cookie)
            Network Connectivity: Simulated Offline Sandbox OK
            =======================================================
            """;
        return Task.FromResult(report);
    }
}

internal sealed class FakeInstanceGate : IInstanceGate
{
    public bool IsMultiInstanceEnabled { get; set; } = true;
    public bool IsAvailable => true;
    public Result TryEnable() => Result.Success();
    public void Disable() { IsMultiInstanceEnabled = false; }
}
#endif
