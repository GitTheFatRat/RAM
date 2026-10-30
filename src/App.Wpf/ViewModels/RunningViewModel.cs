namespace RbxManager.App.ViewModels;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Rotation;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

public partial class RunningViewModel : ObservableObject, IDisposable
{
    private readonly ISessionTracker _sessionTracker;
    private readonly IAccountStore _accountStore;
    private readonly IProcessProvider _processProvider;
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly IRotationManager _rotationManager;
    private readonly ISettingsService _settingsService;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public ObservableCollection<RunningSessionItemViewModel> Sessions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmptyStateVisible))]
    private bool _hasSessions;

    public bool IsEmptyStateVisible => !HasSessions;

    public ObservableCollection<RotationGroupOption> RotationGroups { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoRotationGroups))]
    private bool _hasRotationGroups;

    public bool HasNoRotationGroups => !HasRotationGroups;

    [ObservableProperty]
    private int _rotationHours;

    [ObservableProperty]
    private int _rotationMinutes;

    [ObservableProperty]
    private string _rotationPlaceIdText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRotationIdle))]
    private bool _isRotationRunning;

    public bool IsRotationIdle => !IsRotationRunning;

    [ObservableProperty]
    private string _rotationStatusText = string.Empty;

    public RunningViewModel(
        ISessionTracker sessionTracker,
        IAccountStore accountStore,
        IProcessProvider processProvider,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        IRotationManager rotationManager,
        ISettingsService settingsService)
    {
        _sessionTracker = sessionTracker;
        _accountStore = accountStore;
        _processProvider = processProvider;
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _rotationManager = rotationManager;
        _settingsService = settingsService;

        _sessionTracker.SessionStarted += OnSessionStarted;
        _sessionTracker.SessionEnded += OnSessionEnded;
        _rotationManager.StatusChanged += OnRotationStatusChanged;
        ApplyRotationStatus(_rotationManager.Status);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (s, e) => RefreshLiveMetrics();
        _timer.Start();

        _ = RefreshSessionsAsync();
        _ = LoadRotationOptionsAsync();
    }

    private void OnSessionStarted(RunningSession session)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await AddOrUpdateSessionItemAsync(session);
            }
            catch
            {
            }
        });
    }

    private void OnSessionEnded(RunningSession session, bool intentional)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RunningSessionItemViewModel? item = Sessions.FirstOrDefault(s => s.Pid == session.ProcessId);
            if (item != null)
            {
                Sessions.Remove(item);
                HasSessions = Sessions.Count > 0;
            }
        });
    }

    [RelayCommand]
    public async Task RefreshSessionsAsync()
    {
        IReadOnlyList<RunningSession> activeSessions = _sessionTracker.ActiveSessions;
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        try
        {
            Dictionary<long, Account> accountsById = accounts.ToDictionary(a => a.Id.Value);

            IReadOnlyList<ProcessSnapshot> processes = _processProvider.GetRobloxProcesses();
            Dictionary<int, ProcessSnapshot> processesById = processes.ToDictionary(p => p.Pid);

            Sessions.Clear();
            foreach (RunningSession session in activeSessions)
            {
                accountsById.TryGetValue(session.AccountId.Value, out Account? account);
                processesById.TryGetValue(session.ProcessId, out ProcessSnapshot? proc);

                string alias = account?.Alias ?? $"Account #{session.AccountId.Value}";
                double memoryMb = proc != null ? Math.Round(proc.WorkingSetBytes / (1024.0 * 1024.0), 1) : 0;
                bool autoRejoin = _sessionTracker.GetAutoRejoin(session.AccountId);
                bool gaveUp = _sessionTracker.IsRejoinGaveUp(session.AccountId);

                Sessions.Add(new RunningSessionItemViewModel(session, alias, memoryMb, autoRejoin, gaveUp, (s, enabled) =>
                {
                    _sessionTracker.SetAutoRejoin(s.AccountId, enabled);
                }));
            }

            HasSessions = Sessions.Count > 0;
        }
        finally
        {
            foreach (Account acc in accounts)
            {
                acc.Cookie.Dispose();
            }
        }
    }

    [RelayCommand]
    public async Task CloseSessionAsync(RunningSessionItemViewModel? item)
    {
        if (item == null)
        {
            return;
        }

        bool closed = await _sessionTracker.CloseSessionAsync(item.Pid, graceful: true);
        if (!closed)
        {
            _snackbarService.Show(Strings.ErrorTitle, Strings.ErrorTitle, ControlAppearance.Danger);
        }
    }

    [RelayCommand]
    public async Task CloseAllSessionsAsync()
    {
        if (Sessions.Count == 0)
        {
            return;
        }

        ContentDialogResult confirmResult = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = Strings.CloseAllConfirmTitle,
                Content = Strings.CloseAllConfirmMessage,
                PrimaryButtonText = Strings.CloseAll,
                CloseButtonText = Strings.Cancel
            });

        if (confirmResult == ContentDialogResult.Primary)
        {
            await _sessionTracker.CloseAllSessionsAsync(graceful: true);
        }
    }

    [RelayCommand]
    public async Task StartRotationAsync()
    {
        List<string> groups = RotationGroups.Where(g => g.IsSelected).Select(g => g.Name).ToList();
        long.TryParse(RotationPlaceIdText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long placeId);
        TimeSpan duration = TimeSpan.FromHours(Math.Max(0, RotationHours)) + TimeSpan.FromMinutes(Math.Max(0, RotationMinutes));

        Result started = _rotationManager.Start(new RotationPlan(groups, duration, placeId));
        if (!started.IsSuccess)
        {
            string message = started.ErrorCode switch
            {
                "NoGroups" => Strings.RotationSelectGroup,
                "DurationTooShort" => Strings.RotationDurationTooShort,
                "InvalidPlaceId" => Strings.PlaceIdRequired,
                _ => started.Error ?? Strings.ErrorTitle
            };
            _snackbarService.Show(Strings.ErrorTitle, message, ControlAppearance.Danger);
            return;
        }

        // Merged inside the settings lock so a concurrent toggle/resolution save is not overwritten by a stale snapshot.
        await _settingsService.UpdateSettingsAsync(current => current with
        {
            RotationGroups = groups,
            RotationDurationMinutes = (int)duration.TotalMinutes,
            LastPlaceId = placeId.ToString(CultureInfo.InvariantCulture)
        });
    }

    [RelayCommand]
    public Task StopRotationAsync() => _rotationManager.StopAsync();

    public async Task LoadRotationOptionsAsync()
    {
        AppSettings settings = _settingsService.CurrentSettings ?? AppSettings.Default;
        HashSet<string> saved = RotationGroups.Count > 0
            ? new(RotationGroups.Where(g => g.IsSelected).Select(g => g.Name), StringComparer.OrdinalIgnoreCase)
            : new(settings.RotationGroups ?? [], StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync() ?? [];
        try
        {
            RotationGroups.Clear();
            foreach (IGrouping<string, Account> group in accounts
                         .Where(a => !string.IsNullOrWhiteSpace(a.Group))
                         .GroupBy(a => a.Group!.Trim(), StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                RotationGroups.Add(new RotationGroupOption(group.Key, group.Count(), saved.Contains(group.Key)));
            }
        }
        finally
        {
            foreach (Account acc in accounts)
            {
                acc.Cookie.Dispose();
            }
        }

        HasRotationGroups = RotationGroups.Count > 0;
        RotationHours = settings.RotationDurationMinutes / 60;
        RotationMinutes = settings.RotationDurationMinutes % 60;
        RotationPlaceIdText = settings.LastPlaceId ?? string.Empty;
    }

    private void OnRotationStatusChanged(RotationStatus status)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyRotationStatus(status));
            return;
        }

        ApplyRotationStatus(status);
    }

    private void ApplyRotationStatus(RotationStatus? status)
    {
        status ??= RotationStatus.Idle;
        IsRotationRunning = status.IsRunning;
        if (status.Phase == RotationPhase.WaitingForRecovery)
        {
            RotationStatusText = AccountsViewModel.RecoveryWaitingStatusText;
            return;
        }

        string format = status.Phase switch
        {
            RotationPhase.Launching => Strings.RotationStatusLaunching,
            RotationPhase.Farming => Strings.RotationStatusFarming,
            RotationPhase.Stopping => Strings.RotationStatusStopping,
            RotationPhase.Cleaning => Strings.RotationStatusCleaning,
            _ => Strings.RotationStatusIdle
        };
        RotationStatusText = string.Format(
            CultureInfo.CurrentCulture,
            CompositeFormat.Parse(format),
            status.CurrentGroup,
            status.NextSwitchAt?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
    }

    private async Task AddOrUpdateSessionItemAsync(RunningSession session)
    {
        IReadOnlyList<Account> accounts = await _accountStore.ListAsync();
        string alias;
        try
        {
            Account? account = accounts.FirstOrDefault(a => a.Id == session.AccountId);
            alias = account?.Alias ?? $"Account #{session.AccountId.Value}";
        }
        finally
        {
            foreach (Account acc in accounts)
            {
                acc.Cookie.Dispose();
            }
        }

        RunningSessionItemViewModel? existing = Sessions.FirstOrDefault(s => s.Pid == session.ProcessId);
        if (existing == null)
        {
            bool autoRejoin = _sessionTracker.GetAutoRejoin(session.AccountId);
            bool gaveUp = _sessionTracker.IsRejoinGaveUp(session.AccountId);

            Sessions.Add(new RunningSessionItemViewModel(session, alias, 0, autoRejoin, gaveUp, (s, enabled) =>
            {
                _sessionTracker.SetAutoRejoin(s.AccountId, enabled);
            }));
        }

        HasSessions = Sessions.Count > 0;
    }

    private void RefreshLiveMetrics()
    {
        IReadOnlyList<ProcessSnapshot> processes = _processProvider.GetRobloxProcesses();
        Dictionary<int, ProcessSnapshot> processesById = processes.ToDictionary(p => p.Pid);

        foreach (RunningSessionItemViewModel item in Sessions)
        {
            item.RefreshMetrics();
            if (processesById.TryGetValue(item.Pid, out ProcessSnapshot? proc))
            {
                item.MemoryMb = Math.Round(proc.WorkingSetBytes / (1024.0 * 1024.0), 1);
            }
            item.IsRejoinGaveUp = _sessionTracker.IsRejoinGaveUp(item.AccountId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _sessionTracker.SessionStarted -= OnSessionStarted;
        _sessionTracker.SessionEnded -= OnSessionEnded;
        _rotationManager.StatusChanged -= OnRotationStatusChanged;
        GC.SuppressFinalize(this);
    }
}

public partial class RunningSessionItemViewModel : ObservableObject
{
    private readonly Action<RunningSession, bool> _onAutoRejoinChanged;

    public RunningSession Session { get; }
    public AccountId AccountId => Session.AccountId;
    public int Pid => Session.ProcessId;
    public long PlaceId => Session.Request.PlaceId;
    public DateTimeOffset StartedAt => Session.StartedAt;

    public string Alias { get; }

    public string StartedFormatted => StartedAt.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    [ObservableProperty]
    private string _uptimeFormatted = string.Empty;

    [ObservableProperty]
    private double _memoryMb;

    private bool _autoRejoin;
    public bool AutoRejoin
    {
        get => _autoRejoin;
        set
        {
            if (SetProperty(ref _autoRejoin, value))
            {
                _onAutoRejoinChanged(Session, value);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GaveUpVisibility))]
    private bool _isRejoinGaveUp;

    public Visibility GaveUpVisibility => IsRejoinGaveUp ? Visibility.Visible : Visibility.Collapsed;

    public RunningSessionItemViewModel(
        RunningSession session,
        string alias,
        double memoryMb,
        bool autoRejoin,
        bool isRejoinGaveUp,
        Action<RunningSession, bool> onAutoRejoinChanged)
    {
        Session = session;
        Alias = alias;
        MemoryMb = memoryMb;
        _autoRejoin = autoRejoin;
        _isRejoinGaveUp = isRejoinGaveUp;
        _onAutoRejoinChanged = onAutoRejoinChanged;
        RefreshMetrics();
    }

    public void RefreshMetrics()
    {
        TimeSpan uptime = DateTimeOffset.UtcNow - StartedAt;
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        UptimeFormatted = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"
            : $"{uptime.Minutes:D2}:{uptime.Seconds:D2}";
    }
}

public partial class RotationGroupOption : ObservableObject
{
    public RotationGroupOption(string name, int accountCount, bool isSelected)
    {
        Name = name;
        AccountCount = accountCount;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public int AccountCount { get; }

    public string DisplayName => string.Create(CultureInfo.CurrentCulture, $"{Name} ({AccountCount})");

    [ObservableProperty]
    private bool _isSelected;
}
