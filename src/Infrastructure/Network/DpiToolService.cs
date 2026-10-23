namespace RbxManager.Infrastructure.Network;

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    public const string ProcessName = "goodbyedpi";
    public const string DefaultScriptFileName = "1_russia_blacklist.cmd";
    public const string ArchiveFileName = "goodbyedpi.zip";
    public static readonly Uri DefaultDownloadUri = new("https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.2/goodbyedpi-0.2.2.zip");

    private const int ErrorCancelledWin32Code = 1223;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IProcessProvider? _processProvider;
    private readonly string _toolsDirectory;
    private readonly Func<ProcessStartInfo, bool> _processRunner;
    private readonly ILogger<DpiToolService> _logger;
    private readonly IUatLogger? _uatLogger;

    public string ToolsDirectory => _toolsDirectory;

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
                Timeout = TimeSpan.FromSeconds(60)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RbxManager/1.0");
            _ownsHttpClient = true;
        }

        _processProvider = processProvider;
        _toolsDirectory = !string.IsNullOrWhiteSpace(toolsDirectory)
            ? toolsDirectory
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RbxManager",
                "Tools",
                "GoodbyeDPI");
        _processRunner = processRunner ?? StartElevatedProcess;
        _logger = logger ?? NullLogger<DpiToolService>.Instance;
        _uatLogger = uatLogger;
    }

    public bool IsRunning()
    {
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

        string? scriptPath = FindLaunchScript();
        if (scriptPath == null)
        {
            Result downloadResult = await DownloadAndExtractAsync(ct).ConfigureAwait(false);
            if (!downloadResult.IsSuccess)
            {
                return downloadResult;
            }

            scriptPath = FindLaunchScript();
            if (scriptPath == null)
            {
                return Result.Failure("GoodbyeDPI launch script was not found after extraction.", DpiErrorCodes.LaunchFailed);
            }
        }

        ProcessStartInfo psi = new()
        {
            FileName = scriptPath,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? _toolsDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            _uatLogger?.LogTrace($"DpiToolService: Launching \"{psi.FileName}\" with UseShellExecute={psi.UseShellExecute}, Verb=\"{psi.Verb}\"");
            bool started = _processRunner(psi);
            if (!started)
            {
                return Result.Failure("Failed to start GoodbyeDPI process.", DpiErrorCodes.LaunchFailed);
            }

            LogStarted(_logger, scriptPath);
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
            return Result.Failure($"Failed to launch GoodbyeDPI: {ex.Message}", DpiErrorCodes.LaunchFailed);
        }
    }

    private async Task<Result> DownloadAndExtractAsync(CancellationToken ct)
    {
        string zipPath = Path.Combine(_toolsDirectory, ArchiveFileName);
        try
        {
            Directory.CreateDirectory(_toolsDirectory);
            _uatLogger?.LogTrace($"DpiToolService: Downloading GoodbyeDPI archive from {DefaultDownloadUri} to \"{zipPath}\"");

            using HttpResponseMessage response = await _httpClient.GetAsync(
                DefaultDownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogDownloadHttpFailure(_logger, (int)response.StatusCode);
                return Result.Failure($"GitHub download failed with HTTP {(int)response.StatusCode}.", DpiErrorCodes.DownloadFailed);
            }

            await using (FileStream fileStream = new(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(zipPath, _toolsDirectory, overwriteFiles: true);
            LogExtracted(_logger, _toolsDirectory);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDownloadException(_logger, ex);
            return Result.Failure($"Failed to download or extract GoodbyeDPI: {ex.Message}", DpiErrorCodes.DownloadFailed);
        }
        finally
        {
            try
            {
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }
            }
            catch
            {
                // Ignore temp file cleanup failure
            }
        }
    }

    private string? FindLaunchScript()
    {
        if (!Directory.Exists(_toolsDirectory))
        {
            return null;
        }

        string directPath = Path.Combine(_toolsDirectory, DefaultScriptFileName);
        if (File.Exists(directPath))
        {
            return directPath;
        }

        string? matchingScript = Directory
            .EnumerateFiles(_toolsDirectory, DefaultScriptFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (matchingScript != null)
        {
            return matchingScript;
        }

        return Directory
            .EnumerateFiles(_toolsDirectory, "*.cmd", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static bool StartElevatedProcess(ProcessStartInfo psi)
    {
        using Process? process = Process.Start(psi);
        return process != null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Extracted GoodbyeDPI archive to {Directory}.")]
    private static partial void LogExtracted(ILogger logger, string directory);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Started GoodbyeDPI script {ScriptPath}.")]
    private static partial void LogStarted(ILogger logger, string scriptPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "User denied UAC elevation prompt for GoodbyeDPI.")]
    private static partial void LogUacDenied(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "GoodbyeDPI download returned HTTP {StatusCode}.")]
    private static partial void LogDownloadHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Failed to download or extract GoodbyeDPI.")]
    private static partial void LogDownloadException(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Failed to launch GoodbyeDPI.")]
    private static partial void LogLaunchFailed(ILogger logger, Exception ex);
}
