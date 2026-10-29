namespace RbxManager.Infrastructure.Integrations;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Common;
using RbxManager.Core.Integrations;
using RbxManager.Core.Settings;

public sealed partial class PotassiumService : IPotassiumService
{
    public const string ProcessName = "Potassium";
    public const string ExecutableFileName = "Potassium.exe";
    public const string CoreDllRelativePath = @"bin\Potassium.dll";
    public const string TauriIdentifier = "com.pot.potassium-tauri";

    private readonly ISettingsService? _settingsService;
    private readonly ILogger<PotassiumService> _logger;
    private readonly string _localAppDataRoot;
    private readonly string _roamingAppDataRoot;
    private readonly IReadOnlyList<string> _searchRoots;
    private readonly bool _useSystemShortcuts;

    public PotassiumScanResult? LastScanResult { get; private set; }

    public PotassiumService(
        ISettingsService? settingsService = null,
        ILogger<PotassiumService>? logger = null,
        string? localAppDataRoot = null,
        string? roamingAppDataRoot = null,
        IReadOnlyList<string>? searchRoots = null)
    {
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<PotassiumService>.Instance;
        _localAppDataRoot = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _roamingAppDataRoot = roamingAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _searchRoots = searchRoots ?? GetDefaultSearchRoots();
        _useSystemShortcuts = searchRoots == null;
    }

    public async Task<PotassiumScanResult> ScanAsync(CancellationToken ct = default)
    {
        string? cachedPath = _settingsService?.CurrentSettings.PotassiumExecutablePath;
        PotassiumScanResult result = await Task.Run(() => ScanCore(cachedPath, ct), ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.ExecutablePath) &&
            _settingsService != null &&
            !string.Equals(_settingsService.CurrentSettings.PotassiumExecutablePath, result.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            AppSettings updated = _settingsService.CurrentSettings with { PotassiumExecutablePath = result.ExecutablePath };
            await _settingsService.SaveSettingsAsync(updated, ct).ConfigureAwait(false);
        }

        LastScanResult = result;
        LogScanCompleted(_logger, result.IsInstalled, result.IsRunning, result.AutoExecScriptCount);
        return result;
    }

    private PotassiumScanResult ScanCore(string? cachedPath, CancellationToken ct)
    {
        (IReadOnlyList<int> runningPids, string? runningExePath) = InspectRunningProcesses();

        string? executablePath = runningExePath;
        if (string.IsNullOrWhiteSpace(executablePath) && !string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
        {
            executablePath = cachedPath;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = TryResolveFromShortcuts() ?? ScanSearchRoots(ct);
        }

        string? coreDllPath = ResolveCoreDllPath(executablePath);

        string dataCandidate = Path.Combine(_localAppDataRoot, "Potassium");
        string? dataDir = Directory.Exists(dataCandidate) ? dataCandidate : null;

        string? autoExecDir = dataDir != null && Directory.Exists(Path.Combine(dataDir, "autoexec"))
            ? Path.Combine(dataDir, "autoexec")
            : null;
        string? scriptsDir = dataDir != null && Directory.Exists(Path.Combine(dataDir, "scripts"))
            ? Path.Combine(dataDir, "scripts")
            : null;
        string? workspaceDir = dataDir != null && Directory.Exists(Path.Combine(dataDir, "workspace"))
            ? Path.Combine(dataDir, "workspace")
            : null;
        string? settingsFile = dataDir != null && File.Exists(Path.Combine(dataDir, "settings.json"))
            ? Path.Combine(dataDir, "settings.json")
            : null;

        string roamingCandidate = Path.Combine(_roamingAppDataRoot, TauriIdentifier);
        string? roamingDir = Directory.Exists(roamingCandidate) ? roamingCandidate : null;
        string? mcpTokenPath = roamingDir != null && File.Exists(Path.Combine(roamingDir, "mcp-token"))
            ? Path.Combine(roamingDir, "mcp-token")
            : null;

        int autoExecCount = CountAutoExecScripts(autoExecDir);
        bool isInstalled = executablePath != null || dataDir != null || roamingDir != null;

        return new PotassiumScanResult(
            IsInstalled: isInstalled,
            IsRunning: runningPids.Count > 0,
            RunningProcessIds: runningPids,
            ExecutablePath: executablePath,
            CoreDllPath: coreDllPath,
            DataDirectoryPath: dataDir,
            AutoExecDirectoryPath: autoExecDir,
            ScriptsDirectoryPath: scriptsDir,
            WorkspaceDirectoryPath: workspaceDir,
            SettingsFilePath: settingsFile,
            RoamingConfigDirectoryPath: roamingDir,
            McpTokenPath: mcpTokenPath,
            AutoExecScriptCount: autoExecCount);
    }

    public Result Launch()
    {
        string? exePath = LastScanResult?.ExecutablePath ?? _settingsService?.CurrentSettings.PotassiumExecutablePath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return Result.Failure("Potassium executable was not found. Run a scan first.", "PotassiumNotFound");
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                UseShellExecute = true
            };
            using Process? started = Process.Start(psi);
            return started != null
                ? Result.Success()
                : Result.Failure("Failed to start Potassium.", "PotassiumLaunchFailed");
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to launch Potassium: {ex.Message}", "PotassiumLaunchFailed");
        }
    }

    private static (IReadOnlyList<int> Pids, string? ExecutablePath) InspectRunningProcesses()
    {
        List<int> pids = [];
        string? exePath = null;

        try
        {
            Process[] processes = Process.GetProcessesByName(ProcessName);
            foreach (Process proc in processes)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        pids.Add(proc.Id);
                        if (exePath == null)
                        {
                            try
                            {
                                string? modulePath = proc.MainModule?.FileName;
                                if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
                                {
                                    exePath = NormalizePotassiumExecutablePath(modulePath);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
        }

        return (pids, exePath);
    }

    private string? TryResolveFromShortcuts()
    {
        if (!_useSystemShortcuts)
        {
            return null;
        }

        string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        string[] shortcutCandidates =
        [
            Path.Combine(desktopDir, "Potassium.lnk"),
            Path.Combine(startMenuDir, "Potassium.lnk")
        ];

        foreach (string lnkPath in shortcutCandidates)
        {
            string? resolved = TryExtractPathFromLnkFile(lnkPath);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
            {
                return NormalizePotassiumExecutablePath(resolved);
            }
        }

        return null;
    }

    internal static string? TryExtractPathFromLnkFile(string lnkFilePath)
    {
        try
        {
            if (!File.Exists(lnkFilePath))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(lnkFilePath);
            foreach (Encoding encoding in new[] { Encoding.Unicode, Encoding.ASCII })
            {
                string decoded = encoding.GetString(bytes);
                int index = 0;
                while ((index = decoded.IndexOf(":\\", index, StringComparison.Ordinal)) > 0)
                {
                    char drive = decoded[index - 1];
                    if (char.IsLetter(drive))
                    {
                        int start = index - 1;
                        int end = start;
                        while (end < decoded.Length &&
                               decoded[end] >= 32 &&
                               decoded[end] < 127 &&
                               decoded[end] is not ('"' or '<' or '>' or '|'))
                        {
                            end++;
                        }

                        string candidate = decoded[start..end].Trim();
                        if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }

                    index += 2;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private string? ScanSearchRoots(CancellationToken ct)
    {
        foreach (string root in _searchRoots)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            string directCandidate = Path.Combine(root, ExecutableFileName);
            if (File.Exists(directCandidate))
            {
                return NormalizePotassiumExecutablePath(directCandidate);
            }

            try
            {
                foreach (string subDir in Directory.EnumerateDirectories(root))
                {
                    ct.ThrowIfCancellationRequested();
                    string level1Exe = Path.Combine(subDir, ExecutableFileName);
                    if (File.Exists(level1Exe))
                    {
                        return NormalizePotassiumExecutablePath(level1Exe);
                    }

                    string level2BinExe = Path.Combine(subDir, "bin", ExecutableFileName);
                    if (File.Exists(level2BinExe))
                    {
                        return NormalizePotassiumExecutablePath(level2BinExe);
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string NormalizePotassiumExecutablePath(string foundExePath)
    {
        string? dir = Path.GetDirectoryName(foundExePath);
        if (dir != null && string.Equals(Path.GetFileName(dir), "bin", StringComparison.OrdinalIgnoreCase))
        {
            string? parentDir = Path.GetDirectoryName(dir);
            if (parentDir != null)
            {
                string rootLauncher = Path.Combine(parentDir, ExecutableFileName);
                if (File.Exists(rootLauncher))
                {
                    return rootLauncher;
                }
            }
        }

        return foundExePath;
    }

    private static string? ResolveCoreDllPath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        string nestedDll = Path.Combine(dir, CoreDllRelativePath);
        if (File.Exists(nestedDll))
        {
            return nestedDll;
        }

        string siblingDll = Path.Combine(dir, "Potassium.dll");
        return File.Exists(siblingDll) ? siblingDll : null;
    }

    private static int CountAutoExecScripts(string? autoExecDir)
    {
        if (string.IsNullOrWhiteSpace(autoExecDir) || !Directory.Exists(autoExecDir))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(autoExecDir, "*.*", SearchOption.TopDirectoryOnly)
                .Count(f =>
                {
                    string ext = Path.GetExtension(f);
                    return string.Equals(ext, ".lua", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(ext, ".luau", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
                });
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyList<string> GetDefaultSearchRoots()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(userProfile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        ];
    }

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "Potassium scan completed: Installed={Installed}, Running={Running}, AutoExecScripts={AutoExecCount}.")]
    private static partial void LogScanCompleted(ILogger logger, bool installed, bool running, int autoExecCount);
}
