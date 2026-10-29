namespace RbxManager.Infrastructure.Tests.Integrations;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Integrations;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Integrations;
using Xunit;

public sealed class PotassiumServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _localAppData;
    private readonly string _roamingAppData;
    private readonly string _documentsDir;

    public PotassiumServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RbxManager_PotassiumTests_" + Guid.NewGuid().ToString("N"));
        _localAppData = Path.Combine(_tempRoot, "LocalAppData");
        _roamingAppData = Path.Combine(_tempRoot, "RoamingAppData");
        _documentsDir = Path.Combine(_tempRoot, "Documents");

        Directory.CreateDirectory(_localAppData);
        Directory.CreateDirectory(_roamingAppData);
        Directory.CreateDirectory(_documentsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
        }
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

    [Fact]
    public async Task ScanAsync_DiscoversRandomizedFolderDataAndMcpToken_AndCachesExecutablePath()
    {
        // Arrange: simulate Potassium's randomized Documents folder (e.g. OpXOyuApWKTlFzrV)
        string randomInstallDir = Path.Combine(_documentsDir, "OpXOyuApWKTlFzrV");
        string binDir = Path.Combine(randomInstallDir, "bin");
        Directory.CreateDirectory(binDir);

        string exePath = Path.Combine(randomInstallDir, "Potassium.exe");
        string dllPath = Path.Combine(binDir, "Potassium.dll");
        await File.WriteAllBytesAsync(exePath, [0x4D, 0x5A]);
        await File.WriteAllBytesAsync(dllPath, [0x4D, 0x5A]);

        // Simulate %LOCALAPPDATA%\Potassium\{autoexec,scripts,workspace,settings.json}
        string dataDir = Path.Combine(_localAppData, "Potassium");
        string autoExecDir = Path.Combine(dataDir, "autoexec");
        string scriptsDir = Path.Combine(dataDir, "scripts");
        string workspaceDir = Path.Combine(dataDir, "workspace");
        Directory.CreateDirectory(autoExecDir);
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(workspaceDir);

        await File.WriteAllTextAsync(Path.Combine(dataDir, "settings.json"), "{\"autoAttach\":true}");
        await File.WriteAllTextAsync(Path.Combine(autoExecDir, "kaitun.luau"), "print('kaitun')");
        await File.WriteAllTextAsync(Path.Combine(autoExecDir, "fps_boost.lua"), "print('fps')");
        await File.WriteAllTextAsync(Path.Combine(autoExecDir, "readme.md"), "ignored non-script");

        // Simulate %APPDATA%\com.pot.potassium-tauri\mcp-token
        string tauriDir = Path.Combine(_roamingAppData, "com.pot.potassium-tauri");
        Directory.CreateDirectory(tauriDir);
        string mcpTokenPath = Path.Combine(tauriDir, "mcp-token");
        await File.WriteAllTextAsync(mcpTokenPath, "test-token");

        FakeSettingsService settingsService = new();
        PotassiumService service = new(
            settingsService: settingsService,
            localAppDataRoot: _localAppData,
            roamingAppDataRoot: _roamingAppData,
            searchRoots: [_documentsDir]);

        // Act
        PotassiumScanResult result = await service.ScanAsync();

        // Assert
        Assert.True(result.IsInstalled);
        Assert.Equal(exePath, result.ExecutablePath);
        Assert.Equal(dllPath, result.CoreDllPath);
        Assert.Equal(dataDir, result.DataDirectoryPath);
        Assert.Equal(autoExecDir, result.AutoExecDirectoryPath);
        Assert.Equal(scriptsDir, result.ScriptsDirectoryPath);
        Assert.Equal(workspaceDir, result.WorkspaceDirectoryPath);
        Assert.Equal(Path.Combine(dataDir, "settings.json"), result.SettingsFilePath);
        Assert.Equal(tauriDir, result.RoamingConfigDirectoryPath);
        Assert.Equal(mcpTokenPath, result.McpTokenPath);
        Assert.Equal(2, result.AutoExecScriptCount);

        // Verify executable path was cached into AppSettings
        Assert.Equal(exePath, settingsService.CurrentSettings.PotassiumExecutablePath);
    }

    [Fact]
    public async Task ScanAsync_WhenNothingExists_ReturnsNotInstalled()
    {
        FakeSettingsService settingsService = new();
        PotassiumService service = new(
            settingsService: settingsService,
            localAppDataRoot: Path.Combine(_tempRoot, "EmptyLocal"),
            roamingAppDataRoot: Path.Combine(_tempRoot, "EmptyRoaming"),
            searchRoots: [Path.Combine(_tempRoot, "EmptySearch")]);

        PotassiumScanResult result = await service.ScanAsync();

        if (!result.IsRunning)
        {
            Assert.False(result.IsInstalled);
            Assert.Null(result.ExecutablePath);
            Assert.Null(result.CoreDllPath);
            Assert.Null(result.DataDirectoryPath);
            Assert.Equal(0, result.AutoExecScriptCount);
        }
    }
}
