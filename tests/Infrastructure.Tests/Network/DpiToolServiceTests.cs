namespace RbxManager.Infrastructure.Tests.Network;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using RbxManager.Core.Common;
using RbxManager.Core.Network;
using RbxManager.Core.Processes;
using RbxManager.Infrastructure.Network;
using Xunit;

[SupportedOSPlatform("windows")]
public sealed class DpiToolServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DpiToolServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RbxManager_DpiToolTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private static byte[] CreateFakeGoodbyeDpiZip()
    {
        using MemoryStream ms = new();
        using (ZipArchive archive = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("goodbyedpi-0.2.2/1_russia_blacklist.cmd");
            using StreamWriter writer = new(entry.Open());
            writer.WriteLine("@echo off");
        }
        return ms.ToArray();
    }

    [Fact]
    public void DefaultDownloadUri_PointsToPinnedV022Release()
    {
        Assert.Equal(
            "https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.2/goodbyedpi-0.2.2.zip",
            DpiToolService.DefaultDownloadUri.ToString());
    }

    [Fact]
    public void IsRunning_DelegatesToProcessProvider()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(true);

        using DpiToolService service = new(processProvider: processProvider, toolsDirectory: _tempDir);
        Assert.True(service.IsRunning());
    }

    [Fact]
    public async Task EnsureInstalledAndStartAsync_WhenNotInstalled_DownloadsExtractsAndLaunchesWithRunAs()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        byte[] zipBytes = CreateFakeGoodbyeDpiZip();
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes)
        }));

        ProcessStartInfo? capturedPsi = null;
        using DpiToolService service = new(
            httpClient: httpClient,
            processProvider: processProvider,
            toolsDirectory: _tempDir,
            processRunner: psi =>
            {
                capturedPsi = psi;
                return true;
            });

        Result result = await service.EnsureInstalledAndStartAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedPsi);
        Assert.True(capturedPsi!.UseShellExecute);
        Assert.Equal("runas", capturedPsi.Verb);
        Assert.EndsWith("1_russia_blacklist.cmd", capturedPsi.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(capturedPsi.FileName));
    }

    [Fact]
    public async Task EnsureInstalledAndStartAsync_WhenUacCanceled_ReturnsUacDeniedError()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        string scriptPath = Path.Combine(_tempDir, DpiToolService.DefaultScriptFileName);
        await File.WriteAllTextAsync(scriptPath, "@echo off");

        using DpiToolService service = new(
            processProvider: processProvider,
            toolsDirectory: _tempDir,
            processRunner: _ => throw new Win32Exception(1223));

        Result result = await service.EnsureInstalledAndStartAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(DpiErrorCodes.UacDenied, result.ErrorCode);
    }

    [Fact]
    public async Task EnsureInstalledAndStartAsync_WhenDownloadBlocked_ReturnsDownloadFailedError()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        using DpiToolService service = new(
            httpClient: httpClient,
            processProvider: processProvider,
            toolsDirectory: _tempDir);

        Result result = await service.EnsureInstalledAndStartAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(DpiErrorCodes.DownloadFailed, result.ErrorCode);
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
