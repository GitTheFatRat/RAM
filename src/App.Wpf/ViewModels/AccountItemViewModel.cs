namespace RbxManager.App.ViewModels;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RbxManager.App.Resources;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Sessions;
using Wpf.Ui.Controls;

public partial class AccountItemViewModel : ObservableObject, IDisposable
{
    private readonly ISessionTracker? _sessionTracker;
    private int? _activeProcessId;
    private Process? _watchedProcess;
    private CancellationTokenSource? _watcherCts;

    public Account Account { get; private set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusTextOverride;

    public int? ActiveProcessId
    {
        get => _activeProcessId;
        set
        {
            if (_activeProcessId == value)
            {
                return;
            }

            DetachProcessWatcher();
            _activeProcessId = value;
            Account.ActiveProcessId = value;
            OnPropertyChanged(nameof(ActiveProcessId));
            OnPropertyChanged(nameof(IsRunning));

            if (value.HasValue)
            {
                AttachProcessWatcher(value.Value);
            }
        }
    }

    public bool IsRunning => ActiveProcessId.HasValue;

    public AccountId Id => Account.Id;
    public string Alias => Account.Alias;
    public string? Group => Account.Group;
    public string? Note => Account.Note;
    public DateTimeOffset CreatedAt => Account.CreatedAt;
    public string CreatedFormatted => CreatedAt.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

    public string Username => !string.IsNullOrWhiteSpace(Account.Username) ? Account.Username : "-";
    public long? UserId => Account.UserId;
    public AccountStatus Status => Account.Status;

    public string StatusText => !string.IsNullOrWhiteSpace(StatusTextOverride)
        ? StatusTextOverride
        : Account.Status switch
        {
            AccountStatus.Valid => Strings.StatusValid,
            AccountStatus.Expired => Strings.StatusExpired,
            AccountStatus.Error => Strings.StatusError,
            _ => Strings.StatusUnknown
        };

    public SymbolRegular StatusGlyph => Account.Status switch
    {
        AccountStatus.Valid => SymbolRegular.CheckmarkCircle24,
        AccountStatus.Expired => SymbolRegular.Warning24,
        AccountStatus.Error => SymbolRegular.DismissCircle24,
        _ => SymbolRegular.QuestionCircle24
    };

    public InfoBadgeSeverity StatusSeverity => Account.Status switch
    {
        AccountStatus.Valid => InfoBadgeSeverity.Success,
        AccountStatus.Expired => InfoBadgeSeverity.Critical,
        AccountStatus.Error => InfoBadgeSeverity.Caution,
        _ => InfoBadgeSeverity.Informational
    };

    public string LastCheckedFormatted => Account.LastCheckedAt.HasValue
        ? Account.LastCheckedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        : Strings.Never;

    public string LastCheckedRelative => Account.LastCheckedAt.HasValue
        ? RelativeTimeFormatter.Format(Account.LastCheckedAt.Value, DateTimeOffset.UtcNow)
        : Strings.Never;

    public string LastCheckedTooltip => Account.LastCheckedAt.HasValue
        ? Account.LastCheckedAt.Value.ToLocalTime().ToString("F", CultureInfo.CurrentCulture)
        : Strings.Never;

    public AccountItemViewModel(Account account, ISessionTracker? sessionTracker = null)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        _sessionTracker = sessionTracker;
        if (account.ActiveProcessId.HasValue)
        {
            ActiveProcessId = account.ActiveProcessId;
        }
    }

    public void UpdateAccount(Account updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (!ReferenceEquals(Account.Cookie, updated.Cookie))
        {
            Account.Cookie.Dispose();
        }

        updated.ActiveProcessId = _activeProcessId;
        Account = updated;
        OnPropertyChanged(nameof(Account));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(UserId));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StatusSeverity));
        OnPropertyChanged(nameof(LastCheckedFormatted));
        OnPropertyChanged(nameof(LastCheckedRelative));
        OnPropertyChanged(nameof(LastCheckedTooltip));
    }

    [RelayCommand]
    public void KillInstance()
    {
        int? pid = ActiveProcessId;
        if (!pid.HasValue)
        {
            return;
        }

        if (_sessionTracker != null)
        {
            _ = _sessionTracker.CloseSessionAsync(pid.Value, graceful: false);
        }

        try
        {
            using Process process = Process.GetProcessById(pid.Value);
            process.Kill();
        }
        catch
        {
            // Process may have already exited or become inaccessible.
        }
        finally
        {
            ActiveProcessId = null;
        }
    }

    private void AttachProcessWatcher(int pid)
    {
        try
        {
            Process process = Process.GetProcessById(pid);
            process.EnableRaisingEvents = true;
            process.Exited += OnWatchedProcessExited;
            if (process.HasExited)
            {
                process.Exited -= OnWatchedProcessExited;
                process.Dispose();
                _activeProcessId = null;
                Account.ActiveProcessId = null;
                OnPropertyChanged(nameof(ActiveProcessId));
                OnPropertyChanged(nameof(IsRunning));
                return;
            }

            _watchedProcess = process;
            CancellationTokenSource cts = new();
            _watcherCts = cts;
            _ = MonitorWindowLifecycleAsync(pid, cts.Token);
        }
        catch
        {
            // Ignore in unit tests or if the process exited immediately before attachment.
        }
    }

    private async Task MonitorWindowLifecycleAsync(int pid, CancellationToken ct)
    {
        bool sawMainWindow = false;
        int missingWindowPolls = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using Process proc = Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    OnWatchedProcessExited(this, EventArgs.Empty);
                    return;
                }

                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    sawMainWindow = true;
                    missingWindowPolls = 0;
                }
                else if (sawMainWindow && ++missingWindowPolls >= 3)
                {
                    // User closed the Roblox game window, causing RobloxPlayerBeta to drop into headless tray mode.
                    try
                    {
                        proc.Kill();
                    }
                    catch
                    {
                    }

                    OnWatchedProcessExited(this, EventArgs.Empty);
                    return;
                }
            }
            catch (ArgumentException)
            {
                OnWatchedProcessExited(this, EventArgs.Empty);
                return;
            }
            catch
            {
            }
        }
    }

    private void OnWatchedProcessExited(object? sender, EventArgs e)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ActiveProcessId = null);
        }
        else
        {
            ActiveProcessId = null;
        }
    }

    private void DetachProcessWatcher()
    {
        CancellationTokenSource? cts = _watcherCts;
        _watcherCts = null;
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
        }

        Process? existing = _watchedProcess;
        _watchedProcess = null;
        if (existing != null)
        {
            try
            {
                existing.Exited -= OnWatchedProcessExited;
            }
            catch
            {
            }

            existing.Dispose();
        }
    }

    public void Dispose()
    {
        DetachProcessWatcher();
        GC.SuppressFinalize(this);
    }
}
