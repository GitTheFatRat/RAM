namespace RbxManager.Infrastructure.Network;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Common;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Network;
using RbxManager.Core.Processes;

[SupportedOSPlatform("windows")]
public sealed partial class DpiToolService : IDpiToolService, IDisposable
{
    public const string ProcessName = "Cloudflare WARP";
    public const string WarpCliFileName = "warp-cli.exe";
    public const string WarpGuiFileName = "Cloudflare WARP.exe";
    public const string InstallerFileName = "Cloudflare_WARP_Release-x64.msi";
    public static readonly Uri DefaultDownloadUri = new("https://1111-releases.cloudflareclient.com/win/latest");

    private const int ErrorCancelledWin32Code = 1223;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IProcessProvider? _processProvider;
    private readonly string _toolsDirectory;
    private readonly bool _useSystemInstallLookup;
    private readonly Func<ProcessStartInfo, bool> _processRunner;
    private readonly ILogger<DpiToolService> _logger;
    private readonly IUatLogger? _uatLogger;

    public string ToolsDirectory => _toolsDirectory;

    public static string SystemWarpDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cloudflare", "Cloudflare WARP");

    public DpiToolService(
        HttpClient? httpClient = null,
        IProcessProvider? processProvider = null,
        string? toolsDirectory = null,
        Func<ProcessStartInfo, bool>? processRunner = null,
        ILogger<DpiToolService>? logger = null,
        IUatLogger? uatLogger = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            SocketsHttpHandler handler = new()
            {
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RbxManager/1.0");
            _ownsHttpClient = true;
        }

        _processProvider = processProvider;
        _useSystemInstallLookup = string.IsNullOrWhiteSpace(toolsDirectory);
        _toolsDirectory = !_useSystemInstallLookup
            ? toolsDirectory!
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RbxManager",
                "Tools",
                "CloudflareWARP");
        _processRunner = processRunner ?? RunProcessAndWait;
        _logger = logger ?? NullLogger<DpiToolService>.Instance;
        _uatLogger = uatLogger;
    }

    public bool IsRunning()
    {
        if (_useSystemInstallLookup)
        {
            string? warpCli = FindWarpCliPath();
            if (warpCli != null)
            {
                return IsWarpTunnelConnected(warpCli);
            }
        }

        if (_processProvider != null)
        {
            return _processProvider.IsProcessRunning(ProcessName);
        }

        try
        {
            Process[] processes = Process.GetProcessesByName(ProcessName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    public async Task<Result> EnsureInstalledAndStartAsync(CancellationToken ct = default)
    {
        if (IsRunning())
        {
            return Result.Success();
        }

        string? warpCliPath = FindWarpCliPath();
        if (warpCliPath == null)
        {
            Result downloadResult = await DownloadInstallerAsync(ct).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
            {
                return downloadResult;
            }

            string msiPath = Path.Combine(_toolsDirectory, InstallerFileName);
            ProcessStartInfo installPsi = new()
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\" /passive /norestart",
                WorkingDirectory = _toolsDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                _uatLogger?.LogTrace($"DpiToolService: Installing Cloudflare WARP via \"{installPsi.FileName} {installPsi.Arguments}\"");
                if (!_processRunner(installPsi))
                {
                    return Result.Failure("Failed to install Cloudflare WARP.", DpiErrorCodes.LaunchFailed);
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledWin32Code)
            {
                LogUacDenied(_logger);
                return Result.Failure("Administrator privileges (UAC) were denied.", DpiErrorCodes.UacDenied);
            }
            catch (Exception ex)
            {
                LogLaunchFailed(_logger, ex);
                return Result.Failure($"Failed to install Cloudflare WARP: {ex.Message}", DpiErrorCodes.LaunchFailed);
            }

            warpCliPath = FindWarpCliPath();
            if (warpCliPath == null)
            {
                LogStarted(_logger, msiPath);
                return Result.Success();
            }
        }

        return ConnectWarpCli(warpCliPath);
    }

    private Result ConnectWarpCli(string warpCliPath)
    {
        string workingDir = Path.GetDirectoryName(warpCliPath) ?? _toolsDirectory;
        try
        {
            string guiPath = Path.Combine(workingDir, WarpGuiFileName);
            if (File.Exists(guiPath))
            {
                TryStartGuiNonBlocking(guiPath, workingDir);
            }

            RunCliOptional(warpCliPath, workingDir, "--accept-tos registration new");
            RunCliOptional(warpCliPath, workingDir, "--accept-tos mode warp");
            RunCliOptional(warpCliPath, workingDir, "--accept-tos mode warp+doh");

            ProcessStartInfo connectPsi = new()
            {
                FileName = warpCliPath,
                Arguments = "--accept-tos connect",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _uatLogger?.LogTrace($"DpiToolService: Connecting Cloudflare WARP via \"{connectPsi.FileName} {connectPsi.Arguments}\"");
            if (!_processRunner(connectPsi))
            {
                return Result.Failure("Failed to connect Cloudflare WARP.", DpiErrorCodes.LaunchFailed);
            }

            LogStarted(_logger, warpCliPath);
            return Result.Success();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelledWin32Code)
        {
            LogUacDenied(_logger);
            return Result.Failure("Administrator privileges (UAC) were denied.", DpiErrorCodes.UacDenied);
        }
        catch (Exception ex)
        {
            LogLaunchFailed(_logger, ex);
            return Result.Failure($"Failed to connect Cloudflare WARP: {ex.Message}", DpiErrorCodes.LaunchFailed);
        }
    }

    private void RunCliOptional(string warpCliPath, string workingDir, string arguments)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = warpCliPath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _processRunner(psi);
        }
        catch
        {
            // Ignore optional registration/mode pre-steps if already registered
        }
    }

    private static void TryStartGuiNonBlocking(string guiPath, string workingDir)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = guiPath,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            };
            using Process? proc = Process.Start(psi);
        }
        catch
        {
            // Ignore tray launch errors
        }
    }

    private async Task<Result> DownloadInstallerAsync(CancellationToken ct)
    {
        string msiPath = Path.Combine(_toolsDirectory, InstallerFileName);
        try
        {
            Directory.CreateDirectory(_toolsDirectory);
            _uatLogger?.LogTrace($"DpiToolService: Downloading Cloudflare WARP installer from {DefaultDownloadUri} to \"{msiPath}\"");

            using HttpResponseMessage response = await _httpClient.GetAsync(
                DefaultDownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogDownloadHttpFailure(_logger, (int)response.StatusCode);
                return Result.Failure($"Cloudflare WARP download failed with HTTP {(int)response.StatusCode}.", DpiErrorCodes.DownloadFailed);
            }

            await using (FileStream fileStream = new(msiPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            LogDownloaded(_logger, msiPath);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDownloadException(_logger, ex);
            return Result.Failure($"Failed to download Cloudflare WARP installer: {ex.Message}", DpiErrorCodes.DownloadFailed);
        }
    }

    private string? FindWarpCliPath()
    {
        if (Directory.Exists(_toolsDirectory))
        {
            string directPath = Path.Combine(_toolsDirectory, WarpCliFileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            string? nested = Directory
                .EnumerateFiles(_toolsDirectory, WarpCliFileName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (nested != null)
            {
                return nested;
            }
        }

        if (_useSystemInstallLookup)
        {
            string systemWarpCli = Path.Combine(SystemWarpDirectory, WarpCliFileName);
            if (File.Exists(systemWarpCli))
            {
                return systemWarpCli;
            }
        }

        return null;
    }

    private static bool IsWarpTunnelConnected(string warpCliPath)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = warpCliPath,
                Arguments = "--accept-tos status",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output.Contains("Connected", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("Disconnected", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool RunProcessAndWait(ProcessStartInfo psi)
    {
        using Process? process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        process.WaitForExit(60000);
        return process.HasExited ? process.ExitCode == 0 : true;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Downloaded Cloudflare WARP installer to {InstallerPath}.")]
    private static partial void LogDownloaded(ILogger logger, string installerPath);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Started Cloudflare WARP via {TargetPath}.")]
    private static partial void LogStarted(ILogger logger, string targetPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "User denied UAC elevation prompt for Cloudflare WARP.")]
    private static partial void LogUacDenied(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Cloudflare WARP download returned HTTP {StatusCode}.")]
    private static partial void LogDownloadHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to download Cloudflare WARP installer.")]
    private static partial void LogDownloadException(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to launch or connect Cloudflare WARP.")]
    private static partial void LogLaunchFailed(ILogger logger, Exception ex);
}
