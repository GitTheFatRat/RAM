namespace RbxManager.Infrastructure.Logging;

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Settings;

public sealed partial class UatLogger : IUatLogger
{
    [GeneratedRegex(@"(_\|WARNING:-DO-NOT-SHARE-THIS\S*)|(\.ROBLOSECURITY=\S*)|(\|_[A-Za-z0-9]{16,})|((?<=gameinfo:)[^+&""'\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretAndTicketMaskRegex();

    private readonly ISettingsService _settingsService;
    private readonly object _fileLock = new();

    public string TraceFilePath { get; }

    public UatLogger(ISettingsService settingsService, string? customTraceFilePath = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        TraceFilePath = !string.IsNullOrWhiteSpace(customTraceFilePath)
            ? customTraceFilePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RbxManager_UAT_Trace.txt");
    }

    public void LogTrace(string message)
    {
        if (!_settingsService.CurrentSettings.EnableUatTelemetry || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string masked = MaskSecrets(message);
        string line = string.Create(CultureInfo.InvariantCulture, $"[{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] {masked}{Environment.NewLine}");

        lock (_fileLock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(TraceFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(TraceFilePath, line, Encoding.UTF8);
            }
            catch
            {
                // Best effort UAT trace output; never crash host process
            }
        }
    }

    public static string MaskSecrets(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        return SecretAndTicketMaskRegex().Replace(input, "***");
    }
}
