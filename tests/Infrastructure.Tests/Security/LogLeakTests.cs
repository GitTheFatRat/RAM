namespace RbxManager.Infrastructure.Tests.Security;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Auth;
using RbxManager.Infrastructure.Launch;
using RbxManager.Infrastructure.Storage;
using RbxManager.Infrastructure.Tests.TestHelpers;

[SupportedOSPlatform("windows")]
public sealed class LogLeakTests
{
    private const string FakeCookie = "FAKE_COOKIE_FOR_TESTS_123";
    private const string FakeTicket = "FAKE_TICKET_FOR_TESTS_456";
    private const string FakePassword = "FAKE_PASSWORD_FOR_TESTS_1234!";

    private sealed class InMemoryLogSink
    {
        public ConcurrentBag<string> Messages { get; } = new();
    }

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly InMemoryLogSink _sink;

        public InMemoryLoggerProvider(InMemoryLogSink sink)
        {
            _sink = sink;
        }

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_sink, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogSink _sink;
        private readonly string _category;

        public InMemoryLogger(InMemoryLogSink sink, string category)
        {
            _sink = sink;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            _sink.Messages.Add($"[{logLevel}] {_category}: {message}");
            if (exception != null)
            {
                _sink.Messages.Add($"[EXCEPTION] {_category}: {exception}");
            }
        }
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public string? CapturedUri { get; private set; }

        public bool IsRobloxPlayerInstalled() => true;

        public bool Start(string uri)
        {
            CapturedUri = uri;
            return true;
        }
    }

    private sealed class FakeProcessProvider : RbxManager.Core.Processes.IProcessProvider
    {
        public IReadOnlyList<ProcessSnapshot> GetRobloxProcesses() => [];
        public bool Kill(int pid, bool graceful) => true;
    }

    private sealed class SimpleMockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public SimpleMockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task AllOperations_NeverLeakCookieOrTicketIntoLogs()
    {
        InMemoryLogSink logSink = new();
        using InMemoryLoggerProvider provider = new(logSink);
        ILoggerFactory factory = new LoggerFactory([provider]);

        ILogger<EncryptedFileVault> vaultLogger = factory.CreateLogger<EncryptedFileVault>();
        ILogger<RobloxAuthService> authLogger = factory.CreateLogger<RobloxAuthService>();
        ILogger<RobloxLauncher> launcherLogger = factory.CreateLogger<RobloxLauncher>();

        using TempDirectory tempDir = new();
        string vaultPath = tempDir.CreateFilePath();

        // 1. Vault operations (Add, Update, Remove)
        using (EncryptedFileVault vault = new(vaultPath, vaultLogger))
        {
            using SecretValue cookie = SecretValue.FromString(FakeCookie);
            Account account = new(new AccountId(1), "SecurityAccount", null, null, cookie, DateTimeOffset.UtcNow);

            Result addResult = await vault.AddAsync(account);
            Assert.True(addResult.IsSuccess);

            Result updateResult = await vault.UpdateAsync(account);
            Assert.True(updateResult.IsSuccess);

            await vault.ListAsync();
        }

        // 2. Auth service operations (Success and Failure)
        using (HttpClient successHttp = new(new SimpleMockHttpHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("authenticated", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"id": 1, "name": "SecUser", "displayName": "SecUser"}""", Encoding.UTF8, "application/json")
                };
            }

            HttpResponseMessage ticketResp = new(HttpStatusCode.OK);
            ticketResp.Headers.Add("rbx-authentication-ticket", FakeTicket);
            return ticketResp;
        })))
        {
            RobloxAuthService authService = new(successHttp, authLogger);
            using SecretValue cookie = SecretValue.FromString(FakeCookie);

            Result<RobloxUser> validateSuccess = await authService.ValidateCookieAsync(cookie);
            Assert.True(validateSuccess.IsSuccess);

            Result<SecretValue> ticketSuccess = await authService.GetAuthTicketAsync(cookie);
            Assert.True(ticketSuccess.IsSuccess);
            ticketSuccess.Value?.Dispose();
        }

        using (HttpClient failHttp = new(new SimpleMockHttpHandler(req => new HttpResponseMessage(HttpStatusCode.Unauthorized))))
        {
            RobloxAuthService authService = new(failHttp, authLogger);
            using SecretValue cookie = SecretValue.FromString(FakeCookie);

            await authService.ValidateCookieAsync(cookie);
            await authService.GetAuthTicketAsync(cookie);
        }

        // 3. Launcher operations (LaunchAsync)
        FakeProcessStarter processStarter = new();
        using (HttpClient launchHttp = new(new SimpleMockHttpHandler(req =>
        {
            HttpResponseMessage resp = new(HttpStatusCode.OK);
            resp.Headers.Add("rbx-authentication-ticket", FakeTicket);
            return resp;
        })))
        using (EncryptedFileVault vault = new(vaultPath))
        {
            RobloxAuthService authService = new(launchHttp, authLogger);
            using RobloxLauncher launcher = new(vault, authService, processStarter, new FakeProcessProvider(), new RbxManager.Core.Instance.DefaultInstanceGate(), launcherLogger);

            LaunchRequest launchReq = new(new AccountId(1), 1818, null);
            Result launchResult = await launcher.LaunchAsync(launchReq);
            Assert.True(launchResult.IsSuccess);

            Assert.NotNull(processStarter.CapturedUri);
            Assert.Contains(FakeTicket, processStarter.CapturedUri, StringComparison.Ordinal);
        }

        // 4. ServerBrowser operations (unauthenticated queries)
        ILogger<RbxManager.Infrastructure.Servers.ServerBrowserService> serverLogger =
            factory.CreateLogger<RbxManager.Infrastructure.Servers.ServerBrowserService>();
        using (HttpClient serverHttp = new(new SimpleMockHttpHandler(req =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            })))
        {
            RbxManager.Infrastructure.Servers.ServerBrowserService serverService = new(serverHttp, serverLogger);
            await serverService.GetServersAsync(1818);
        }

        // 5. SessionTracker operations (rejoin logging)
        ILogger<RbxManager.Infrastructure.Sessions.SessionTracker> trackerLogger =
            factory.CreateLogger<RbxManager.Infrastructure.Sessions.SessionTracker>();
        using (HttpClient failHttp = new(new SimpleMockHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))))
        using (EncryptedFileVault trackerVault = new(vaultPath))
        {
            RobloxAuthService authService = new(failHttp, authLogger);
            using RobloxLauncher launcher = new(trackerVault, authService, processStarter, new FakeProcessProvider(), new RbxManager.Core.Instance.DefaultInstanceGate(), launcherLogger);
            using RbxManager.Infrastructure.Sessions.SessionTracker tracker = new(
                new FakeProcessProvider(),
                trackerVault,
                authService,
                launcher,
                trackerLogger,
                policyFactory: () => new RbxManager.Core.Sessions.RejoinPolicy(initialDelay: TimeSpan.Zero));

            AccountId accId = new(1);
            tracker.SetAutoRejoin(accId, true);
            RbxManager.Core.Sessions.RunningSession session = new(accId, new LaunchRequest(accId, 1818, null), 999, DateTimeOffset.UtcNow);
            tracker.RegisterSession(session);
            await tracker.PollProcessesAsync();
        }

        // 6. EncryptedBackupService operations (export & import)
        ILogger<RbxManager.Infrastructure.Backup.EncryptedBackupService> backupLogger =
            factory.CreateLogger<RbxManager.Infrastructure.Backup.EncryptedBackupService>();
        string backupFile = tempDir.CreateFilePath("backup.rbxbackup");
        using (EncryptedFileVault backupVault = new(vaultPath))
        {
            RbxManager.Infrastructure.Backup.EncryptedBackupService backupService = new(backupVault, backupLogger);
            await backupService.ExportAsync(backupFile, FakePassword);
        }

        using (EncryptedFileVault destVault = new(tempDir.CreateFilePath("destVault.dat")))
        {
            RbxManager.Infrastructure.Backup.EncryptedBackupService importService = new(destVault, backupLogger);
            await importService.ImportAsync(backupFile, FakePassword);
        }

        // 7. Assert zero log leakage
        Assert.NotEmpty(logSink.Messages);

        foreach (string logMessage in logSink.Messages)
        {
            Assert.DoesNotContain(FakeCookie, logMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeTicket, logMessage, StringComparison.Ordinal);
            Assert.DoesNotContain(FakePassword, logMessage, StringComparison.Ordinal);
        }
    }
}
