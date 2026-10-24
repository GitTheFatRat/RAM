namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using System.IO;
using System.Text.Json.Nodes;
using RbxManager.Infrastructure.Launch;

public sealed class RobloxFastFlagInjectorTests : IDisposable
{
    private readonly string _versionsRoot = Path.Combine(Path.GetTempPath(), "rbx-fflag-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_versionsRoot))
        {
            Directory.Delete(_versionsRoot, recursive: true);
        }
    }

    [Fact]
    public void InjectResolutionFastFlags_PreservesExistingFlags_AndWritesMinSize()
    {
        string exePath = CreateFakeVersion("version-abc");
        string settingsPath = Path.Combine(Path.GetDirectoryName(exePath)!, "ClientSettings", "ClientAppSettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, """{ "FIntTargetRefreshRate": 60, "DFIntWindowSizeMinX": 800 }""");

        bool injected = RobloxFastFlagInjector.InjectResolutionFastFlags(exePath, 200, 400);

        Assert.True(injected);
        JsonObject flags = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal(60, flags["FIntTargetRefreshRate"]!.GetValue<int>());
        Assert.Equal(200, flags[RobloxFastFlagInjector.MinWidthFlag]!.GetValue<int>());
        Assert.Equal(400, flags[RobloxFastFlagInjector.MinHeightFlag]!.GetValue<int>());
    }

    [Fact]
    public void InjectResolutionFastFlags_ReplacesCorruptJson()
    {
        string exePath = CreateFakeVersion("version-corrupt");
        string settingsPath = Path.Combine(Path.GetDirectoryName(exePath)!, "ClientSettings", "ClientAppSettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ not json");

        Assert.True(RobloxFastFlagInjector.InjectResolutionFastFlags(exePath, 320, 240));
        Assert.Equal(320, JsonNode.Parse(File.ReadAllText(settingsPath))![RobloxFastFlagInjector.MinWidthFlag]!.GetValue<int>());
    }

    [Theory]
    [InlineData(0, 400)]
    [InlineData(200, -1)]
    public void InjectResolutionFastFlags_RejectsInvalidSize(int width, int height) =>
        Assert.False(RobloxFastFlagInjector.InjectResolutionFastFlags(CreateFakeVersion("version-x"), width, height));

    [Fact]
    public void FindInstalledPlayerExecutables_OnlyReturnsFoldersWithPlayer()
    {
        string exePath = CreateFakeVersion("version-player");
        Directory.CreateDirectory(Path.Combine(_versionsRoot, "version-studio-only"));

        var found = RobloxFastFlagInjector.FindInstalledPlayerExecutables([_versionsRoot, Path.Combine(_versionsRoot, "missing")]);

        Assert.Equal([exePath], found);
    }

    private string CreateFakeVersion(string versionName)
    {
        string versionDir = Path.Combine(_versionsRoot, versionName);
        Directory.CreateDirectory(versionDir);
        string exePath = Path.Combine(versionDir, "RobloxPlayerBeta.exe");
        File.WriteAllText(exePath, string.Empty);
        return exePath;
    }
}
