namespace RbxManager.Infrastructure.Tests.Logging;

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Logging;
using RbxManager.Infrastructure.Processes;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class UatLoggerTests : IDisposable
{
    private static readonly int[] TestPids = [4242];
    private readonly string _tempDir;
    private readonly string _traceFile;

    public UatLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RbxManager_UatLoggerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _traceFile = Path.Combine(_tempDir, "RbxManager_UAT_Trace.txt");
    }

    [Fact]
    public void LogTrace_WhenEnableUatTelemetryIsFalse_DoesNotCreateTraceFile()
    {
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        settingsService.CurrentSettings.Returns(new AppSettings(EnableUatTelemetry: false));

        UatLogger logger = new(settingsService, _traceFile);
        logger.LogTrace("SessionTracker: Session bound -> Account=TestUser, PID=1234, PlaceId=2753915549");

        Assert.False(File.Exists(_traceFile));
    }

    [Fact]
    public void LogTrace_WhenEnableUatTelemetryIsTrue_AppendsTimestampedMaskedTraceToFile()
    {
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        settingsService.CurrentSettings.Returns(new AppSettings(EnableUatTelemetry: true));

        UatLogger logger = new(settingsService, _traceFile);
        string launchUri = "roblox-player:1+launchmode:play+gameinfo:SUPER_SECRET_AUTH_TICKET_999+launchtime:1700000000000+placelauncherurl:https%3A%2F%2Fassetgame.roblox.com%2Fgame%2FPlaceLauncher.ashx%3Frequest%3DRequestGame%26placeId%3D2753915549+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:";

        logger.LogTrace($"WindowsProcessStarter.Start: FileName=\"{launchUri}\", UseShellExecute=True");

        Assert.True(File.Exists(_traceFile));
        string content = File.ReadAllText(_traceFile);
        Assert.Contains("WindowsProcessStarter.Start:", content);
        Assert.Contains("gameinfo:***+launchtime:1700000000000", content);
        Assert.Contains("placeId%3D2753915549", content);
        Assert.DoesNotContain("SUPER_SECRET_AUTH_TICKET_999", content);
    }

    [Fact]
    public void MaskSecrets_ReplacesRobloSecurityCookieAndTicketsWithAsterisks()
    {
        string raw = "Cookie: .ROBLOSECURITY=_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_ABCDEF1234567890ABCDEF1234567890 and gameinfo:TICKET_ABC_123+launchtime:123";

        string masked = UatLogger.MaskSecrets(raw);

        Assert.DoesNotContain("WARNING:-DO-NOT-SHARE-THIS", masked);
        Assert.DoesNotContain("ABCDEF1234567890", masked);
        Assert.DoesNotContain("TICKET_ABC_123", masked);
        Assert.Contains("gameinfo:***+launchtime:123", masked);
    }

    [Fact]
    public void Constructor_WhenNoCustomPathProvided_DefaultsToDesktopTraceFile()
    {
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        settingsService.CurrentSettings.Returns(new AppSettings());

        UatLogger logger = new(settingsService);

        string expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RbxManager_UAT_Trace.txt");
        Assert.Equal(expectedPath, logger.TraceFilePath);
    }

    [Fact]
    public async Task WindowsMemoryOptimizer_WritesSetProcessWorkingSetSizeToUatLogger()
    {
        ISettingsService settingsService = Substitute.For<ISettingsService>();
        settingsService.CurrentSettings.Returns(new AppSettings(EnableUatTelemetry: true));
        UatLogger uatLogger = new(settingsService, _traceFile);

        INativeWindowMethods windowMethods = Substitute.For<INativeWindowMethods>();
        windowMethods.GetForegroundWindow().Returns(IntPtr.Zero);

        WindowsMemoryOptimizer optimizer = new(
            processProvider: null,
            nativeMemoryMethods: null,
            nativeWindowMethods: windowMethods,
            customTrimProcess: _ => true,
            logger: null,
            uatLogger: uatLogger);

        await optimizer.TrimMemoryAsync(TestPids, CancellationToken.None);

        Assert.True(File.Exists(_traceFile));
        string content = File.ReadAllText(_traceFile);
        Assert.Contains("Calling SetProcessWorkingSetSize(-1, -1) on PID 4242", content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
