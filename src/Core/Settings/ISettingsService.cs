namespace RbxManager.Core.Settings;

using System;
using System.Threading;
using System.Threading.Tasks;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    event Action<AppSettings>? SettingsChanged;

    Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default);
}
