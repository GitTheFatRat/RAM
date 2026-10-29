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

    /// <summary>
    /// Atomically applies <paramref name="mutate"/> to the latest settings and persists the result.
    /// Prefer this over <c>SaveSettingsAsync(CurrentSettings with {...})</c>, which can lose concurrent updates.
    /// </summary>
    Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> mutate, CancellationToken ct = default);
}
