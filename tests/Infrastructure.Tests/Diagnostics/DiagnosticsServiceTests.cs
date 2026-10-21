namespace RbxManager.Infrastructure.Tests.Diagnostics;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Instance;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Diagnostics;
using Xunit;

public sealed class DiagnosticsServiceTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";
    private const string FakeTicket = "FAKE_TICKET_FOR_TESTS_456";
    private const string SensitiveHeaderValue = "SUPER_SECRET_TOKEN_VALUE_HEADER";

    private sealed class FakeRegistryProvider : IRegistryProvider
    {
        public bool ProtocolHandlerExists(string protocol) => true;

        public string? GetProtocolHandlerCommand(string protocol) =>
            @"C:\Users\Test\AppData\Local\Bloxstrap\Bloxstrap.exe %1";

        public bool StandardRobloxInstallFolderExists() => true;

        public string? GetWebView2Version() => "120.0.2210.121";

        public bool IsProcessElevated() => false;
    }

    private sealed class FakeProcessProvider : IProcessProvider
    {
        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() =>
            [new ProcessSnapshot(9999, DateTimeOffset.UtcNow, 40000000)];

        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class FakeInstanceGate : IInstanceGate
    {
        public bool IsMultiInstanceEnabled => false;
        public bool IsAvailable => true;
        public Result TryEnable() => Result.Success();
        public void Disable() { }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = AppSettings.Default;
        public event Action<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default) =>
            Task.FromResult(CurrentSettings);

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
        {
            CurrentSettings = settings;
            SettingsChanged?.Invoke(settings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Failure<RobloxUser>(
                $"Error with {FakeCookie} and {FakeTicket}",
                "Unauthorized"));
        }

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Failure<SecretValue>(
                $"Failed acquiring ticket with payload {FakeCookie} and {FakeTicket}",
                "TicketFailed"));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"errors\":[{{\"code\":401,\"message\":\"Body with {FakeCookie} and {FakeTicket}\"}}]}}",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.Add("X-Roblox-Secret-Header", SensitiveHeaderValue);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task RunDiagnosticsAsync_ProducesRedactedReport_WithoutSecretsOrHeaderValues()
    {
        FakeRegistryProvider registry = new();
        FakeProcessProvider processProvider = new();
        FakeInstanceGate instanceGate = new();
        FakeSettingsService settingsService = new();
        FakeAuthService authService = new();
        using FakeHttpMessageHandler handler = new();
        using HttpClient httpClient = new(handler);

        DiagnosticsService service = new(
            registry,
            processProvider,
            instanceGate,
            settingsService,
            authService,
            httpClient);

        using SecretValue accountCookie = SecretValue.FromString(FakeCookie);
        Account testAccount = new(
            Id: new AccountId(1),
            Alias: "TestAccount",
            Group: null,
            Note: null,
            Cookie: accountCookie,
            CreatedAt: DateTimeOffset.UtcNow);

        string report = await service.RunDiagnosticsAsync(testAccount);

        // Assert starts with required header line
        Assert.StartsWith("Safe to share: redacted. No cookies or tickets.", report, StringComparison.Ordinal);

        // Assert sensitive secrets are never leaked
        Assert.DoesNotContain(FakeCookie, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeTicket, report, StringComparison.OrdinalIgnoreCase);

        // Assert header values never appear (header names only)
        Assert.DoesNotContain(SensitiveHeaderValue, report, StringComparison.Ordinal);
        Assert.Contains("X-Roblox-Secret-Header", report, StringComparison.Ordinal);

        // Assert bootstrapper detection
        Assert.Contains("Bloxstrap", report, StringComparison.Ordinal);

        // Assert report sections present
        Assert.Contains("[Environment]", report, StringComparison.Ordinal);
        Assert.Contains("[Roblox Client]", report, StringComparison.Ordinal);
        Assert.Contains("[Network]", report, StringComparison.Ordinal);
        Assert.Contains("[Templates]", report, StringComparison.Ordinal);
        Assert.Contains("[Account Check: TestAccount]", report, StringComparison.Ordinal);
        Assert.Contains("LaunchUriDryRun: roblox-player:", report, StringComparison.Ordinal);
    }
}
