namespace RbxManager.Infrastructure.Tests.Network;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        _tempDir = Path.Combine(Path.GetTempPath(), "RbxManager_WarpToolTests_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void DefaultDownloadUri_PointsToOfficialCloudflareWarpRelease()
    {
        Assert.Equal(
            "https://1111-releases.cloudflareclient.com/win/latest",
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
    public async Task EnsureInstalledAndStartAsync_WhenNotInstalled_DownloadsMsiAndLaunchesMsiexecWithRunAs()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        byte[] fakeMsiBytes = [0xD0, 0xCF, 0x11, 0xE0];
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeMsiBytes)
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
        Assert.Equal("msiexec.exe", capturedPsi.FileName);
        Assert.Contains(DpiToolService.InstallerFileName, capturedPsi.Arguments, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_tempDir, DpiToolService.InstallerFileName)));
    }

    [Fact]
    public async Task EnsureInstalledAndStartAsync_WhenWarpCliInstalled_ExecutesWarpCliConnect()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        string warpCliPath = Path.Combine(_tempDir, DpiToolService.WarpCliFileName);
        await File.WriteAllTextAsync(warpCliPath, "fake-warp-cli");

        List<ProcessStartInfo> capturedCalls = [];
        using DpiToolService service = new(
            processProvider: processProvider,
            toolsDirectory: _tempDir,
            processRunner: psi =>
            {
                capturedCalls.Add(psi);
                return true;
            });

        Result result = await service.EnsureInstalledAndStartAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains(capturedCalls, psi =>
            psi.FileName.EndsWith(DpiToolService.WarpCliFileName, StringComparison.OrdinalIgnoreCase) &&
            psi.Arguments == "--accept-tos connect");
    }

    [Fact]
    public async Task EnsureInstalledAndStartAsync_WhenUacCanceled_ReturnsUacDeniedError()
    {
        IProcessProvider processProvider = Substitute.For<IProcessProvider>();
        processProvider.IsProcessRunning(DpiToolService.ProcessName).Returns(false);

        byte[] fakeMsiBytes = [0xD0, 0xCF, 0x11, 0xE0];
        using HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(fakeMsiBytes)
        }));

        using DpiToolService service = new(
            httpClient: httpClient,
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
