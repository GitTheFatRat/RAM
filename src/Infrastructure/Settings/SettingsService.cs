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
            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return UpdateSettingsAsync(_ => settings, ct);
    }

    public async Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        AppSettings clamped;
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Mutating the latest snapshot inside the lock is what prevents overlapping saves from undoing each other.
            clamped = mutate(_currentSettings).Clamp();
            await WriteToDiskAsync(clamped, ct).ConfigureAwait(false);
            _currentSettings = clamped;
        }
        finally
        {
            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        SettingsChanged?.Invoke(clamped);
        return clamped;
    }

    private async Task WriteToDiskAsync(AppSettings settings, CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = _filePath + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings, RbxManagerJsonContext.Indented.AppSettings);

        await File.WriteAllBytesAsync(tempPath, bytes, ct).ConfigureAwait(false);

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }
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
