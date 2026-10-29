namespace RbxManager.Core.Integrations;

using System.Collections.Generic;

public sealed record PotassiumScanResult(
    bool IsInstalled,
    bool IsRunning,
    IReadOnlyList<int> RunningProcessIds,
    string? ExecutablePath,
    string? CoreDllPath,
    string? DataDirectoryPath,
    string? AutoExecDirectoryPath,
    string? ScriptsDirectoryPath,
    string? WorkspaceDirectoryPath,
    string? SettingsFilePath,
    string? RoamingConfigDirectoryPath,
    string? McpTokenPath,
    int AutoExecScriptCount);
