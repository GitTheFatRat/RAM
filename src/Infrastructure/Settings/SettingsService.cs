namespace RbxManager.Infrastructure.Settings;

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Serialization;

public sealed partial class SettingsService : ISettingsService, IDisposable
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RbxManager",
        "settings.json");

    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private AppSettings _currentSettings;
    private bool _disposed;

    public AppSettings CurrentSettings => _currentSettings;

    public event Action<AppSettings>? SettingsChanged;

    public SettingsService(string? filePath = null, ILogger<SettingsService>? logger = null)
    {
        _filePath = filePath ?? DefaultPath;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
        _currentSettings = AppSettings.Default;
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                _currentSettings = AppSettings.Default;
                return _currentSettings;
            }

            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(_filePath, ct).ConfigureAwait(false);
                AppSettings? loaded = JsonSerializer.Deserialize(bytes, RbxManagerJsonContext.Default.AppSettings);
                _currentSettings = (loaded ?? AppSettings.Default).Clamp();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                LogSettingsLoadWarning(_filePath, ex.Message);
                _currentSettings = AppSettings.Default;
            }

            return _currentSettings;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        AppSettings clamped = settings.Clamp();

        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = _filePath + ".tmp";
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(clamped, RbxManagerJsonContext.Indented.AppSettings);

            await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }

            _currentSettings = clamped;
        }
        finally
        {
            _semaphore.Release();
        }

        SettingsChanged?.Invoke(clamped);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _semaphore.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    [LoggerMessage(EventId = 701, Level = LogLevel.Warning, Message = "Failed to load settings from {Path}. Using default settings. Error: {Error}")]
    private partial void LogSettingsLoadWarning(string path, string error);
}
