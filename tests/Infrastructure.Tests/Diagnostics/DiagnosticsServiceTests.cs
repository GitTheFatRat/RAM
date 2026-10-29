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
    private const string FakeCookie = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_FAKE0123456789ABCDEFSECRETTAIL";
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

        public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
        {
            AppSettings updated = mutate(CurrentSettings);
            CurrentSettings = updated;
            SettingsChanged?.Invoke(updated);
            return Task.FromResult(updated);
        }
    }

    private sealed class FakeAuthService : IRobloxAuthService
    {
        public Task<Result<RobloxUser>> ValidateCookieAsync(SecretValue cookie, CancellationToken ct = default)
        {
            AuthFailureDetails details = new(
                Stage: "Validate",
                HttpStatus: 401,
                RobloxErrorCodes: [401],
                RobloxMessage: $"Error with cookie {FakeCookie}",
                CsrfTokenObtained: false,
                ResponseHeaderNames: ["content-length"],
                TicketHeaderPresent: false);

            return Task.FromResult(Result.Failure<RobloxUser>(
                $"Error with cookie {FakeCookie}",
                "Unauthorized",
                details));
        }

        public Task<Result<SecretValue>> GetAuthTicketAsync(SecretValue cookie, CancellationToken ct = default)
        {
            AuthFailureDetails details = new(
                Stage: "Ticket",
                HttpStatus: 403,
                RobloxErrorCodes: [403],
                RobloxMessage: $"Failed acquiring ticket with payload {FakeCookie}",
                CsrfTokenObtained: true,
                ResponseHeaderNames: ["x-csrf-token"],
                TicketHeaderPresent: false);

            return Task.FromResult(Result.Failure<SecretValue>(
                $"Failed acquiring ticket with payload {FakeCookie}",
                "TicketFailed",
                details));
        }
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"errors\":[{{\"code\":401,\"message\":\"Body with {FakeCookie}\"}}]}}",
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

        // Assert sensitive secrets are never leaked (neither prefix nor tail survives)
        Assert.DoesNotContain(FakeCookie, report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WARNING:-DO-NOT-SHARE-THIS", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FAKE0123456789ABCDEFSECRETTAIL", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FakeTicket, report, StringComparison.OrdinalIgnoreCase);

        // Assert username path masking
        Assert.DoesNotContain(@"C:\Users\Test\", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\<user>\", report, StringComparison.OrdinalIgnoreCase);

        // Assert failure details are printed
        Assert.Contains("Stage: Validate", report, StringComparison.Ordinal);
        Assert.Contains("HttpStatus: 401", report, StringComparison.Ordinal);
        Assert.Contains("RobloxErrorCodes: [401]", report, StringComparison.Ordinal);
        Assert.Contains("Stage: Ticket", report, StringComparison.Ordinal);
        Assert.Contains("HttpStatus: 403", report, StringComparison.Ordinal);
        Assert.Contains("CsrfTokenObtained: yes", report, StringComparison.Ordinal);
        Assert.Contains("TicketHeaderPresent: no", report, StringComparison.Ordinal);
        Assert.Contains("ResponseHeaderNames: [x-csrf-token]", report, StringComparison.Ordinal);

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
