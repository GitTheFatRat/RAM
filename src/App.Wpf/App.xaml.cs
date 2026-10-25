namespace RbxManager.App;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using RbxManager.Core.Instance;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure;
using RbxManager.Infrastructure.Instance;
using RbxManager.Infrastructure.Logging;
using RbxManager.Infrastructure.Settings;
using RbxManager.Infrastructure.Storage;
using Serilog;
using Wpf.Ui;

public partial class App : Application
{
    private const string AppMutexName = @"Local\RbxManager_Application_SingleInstance_Mutex";

    private static Mutex? _appSingleInstanceMutex;
    private static Mutex? _globalRobloxMutex;
    private static Mutex? _globalRobloxSingletonMutex;
    private readonly IHost _host;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public App()
    {
        ConfigureCulture();
        ConfigureLogging();
        ConfigureGlobalExceptionHandling();

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();
    }

    private static void ConfigureCulture()
    {
        CultureInfo culture = new("vi-VN");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private static void ConfigureLogging()
    {
        string logPath = "logs/app-.log";
#if DEBUG
        string? dataDir = Environment.GetEnvironmentVariable("RBXMANAGER_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            logPath = Path.Combine(dataDir, "logs", "app-.log");
        }
#endif

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.With<RobloxSecretRedactionEnricher>()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }

    private static void ConfigureGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Exception? ex = e.ExceptionObject as Exception;
            WriteCrashLog("AppDomain.UnhandledException", ex);
            Log.Error(ex, "AppDomain unhandled exception: {Message}. IsTerminating: {IsTerminating}.",
                ex?.Message, e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
            Log.Error(e.Exception, "Unobserved task exception: {Message}.",
                e.Exception?.Message);
            e.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("Application.DispatcherUnhandledException", e.Exception);
        Log.Error(e.Exception, "Dispatcher unhandled exception: {Message}.", e.Exception.Message);
        if (e.Exception.InnerException != null)
        {
            Log.Error(e.Exception.InnerException, "Inner exception: {Message}", e.Exception.InnerException.Message);
        }
        e.Handled = true;

        try
        {
            MessageBox.Show(
                $"A fatal error occurred. Check crash_log.txt for details.\n\n{e.Exception.Message}",
                "RbxManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore failure displaying message box during critical UI failure
        }
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            string crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            StringBuilder sb = new();
            sb.AppendLine("==================================================");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Timestamp: {DateTimeOffset.Now:O}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Source: {source}");
            if (exception != null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Exception Type: {exception.GetType().FullName}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Message: {exception.Message}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"StackTrace:\n{exception.StackTrace}");

                Exception? inner = exception.InnerException;
                int depth = 1;
                while (inner != null)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"--- InnerException [{depth}] ({inner.GetType().FullName}) ---");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Message: {inner.Message}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"StackTrace:\n{inner.StackTrace}");
                    inner = inner.InnerException;
                    depth++;
                }
            }
            else
            {
                sb.AppendLine("Exception: <null>");
            }
            sb.AppendLine();

            File.AppendAllText(crashLogPath, sb.ToString());
        }
        catch
        {
            // Best-effort crash file logging
        }
    }

    private static void FocusExistingWindow()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            Process[] processes = Process.GetProcessesByName(current.ProcessName);
            try
            {
                Process? other = processes.FirstOrDefault(p => p.Id != current.Id);
                if (other != null && other.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
            finally
            {
                foreach (Process p in processes)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
        }
    }

    private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<Serilog.ILogger>(Log.Logger);

        // Core & Infrastructure
        services.AddInfrastructureServices();

#if DEBUG
        string? dataDir = Environment.GetEnvironmentVariable("RBXMANAGER_DATA_DIR");
        string? uiTest = Environment.GetEnvironmentVariable("RBXMANAGER_UITEST");

        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            string vaultPath = Path.Combine(dataDir, "vault.dat");
            string settingsPath = Path.Combine(dataDir, "settings.json");

            services.RemoveAll<Core.Settings.ISettingsService>();
            services.AddSingleton<Core.Settings.ISettingsService>(sp => new SettingsService(settingsPath, sp.GetService<ILogger<SettingsService>>()));

            services.RemoveAll<Core.Storage.IAccountStore>();
            services.AddSingleton<Core.Storage.IAccountStore>(sp => new EncryptedFileVault(
                vaultPath,
                sp.GetRequiredService<IVaultKeyProvider>(),
                sp.GetService<ILogger<EncryptedFileVault>>()));
        }

        if (uiTest == "1" && !string.IsNullOrWhiteSpace(dataDir))
        {
            UiTestHooks.ConfigureFakes(services, dataDir);
        }
#endif

        // UI Services
        services.AddSingleton<Core.Diagnostics.IRegistryProvider, Services.WindowsRegistryProvider>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();

        // Views & ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        // Singletons so an in-flight sequential launch queue survives page navigation (a transient VM would be
        // orphaned mid-delay) and is cancelled/disposed exactly once on shutdown.
        services.AddSingleton<AccountsViewModel>();
        services.AddSingleton<AccountsPage>();
        services.AddSingleton<ServersViewModel>();
        services.AddSingleton<ServersPage>();
        services.AddTransient<RunningViewModel>();
        services.AddTransient<RunningPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<ViewModels.DiagnosticsViewModel>();
        services.AddTransient<Views.Pages.DiagnosticsPage>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        string? uiTestEnv = Environment.GetEnvironmentVariable("RBXMANAGER_UITEST");
        string? dataDirEnv = Environment.GetEnvironmentVariable("RBXMANAGER_DATA_DIR");
        if (uiTestEnv == "1")
        {
            // Force software rendering in UI test mode so GDI PrintWindow (PW_RENDERFULLCONTENT) can capture the surface
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            if (string.IsNullOrWhiteSpace(dataDirEnv))
            {
                Log.Fatal("RBXMANAGER_UITEST is set, but RBXMANAGER_DATA_DIR is missing.");
                Shutdown(1);
                return;
            }

            string realAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RbxManager");
            string fullDataDir = Path.GetFullPath(dataDirEnv);
            string fullRealAppData = Path.GetFullPath(realAppData);
            if (fullDataDir.Equals(fullRealAppData, StringComparison.OrdinalIgnoreCase) ||
                fullDataDir.StartsWith(fullRealAppData + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Log.Fatal("RBXMANAGER_DATA_DIR resolves inside real LocalAppData directory: {DataDir}", fullDataDir);
                Shutdown(1);
                return;
            }
        }
#endif

        CleanupLeftoverLoginFolders();

        string mutexName = AppMutexName;
#if DEBUG
        if (Environment.GetEnvironmentVariable("RBXMANAGER_UITEST") == "1")
        {
            mutexName = @"Local\RbxManager_Application_UiTest_Mutex";
        }
#endif

        _appSingleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            FocusExistingWindow();
            Shutdown();
            return;
        }

        Log.Information("RbxManager application starting up.");

        await _host.StartAsync();

#if DEBUG
        if (uiTestEnv == "1" && !string.IsNullOrWhiteSpace(dataDirEnv))
        {
            await UiTestHooks.EnsureSeededAsync(_host.Services, dataDirEnv);
        }
#endif

        Core.Settings.ISettingsService settingsService = _host.Services.GetRequiredService<Core.Settings.ISettingsService>();
        Core.Settings.AppSettings settings = await settingsService.LoadSettingsAsync();
        ApplyTheme(settings.Theme);

        // One place sizes/tiles every tracked Roblox window: manual launches, Servers-tab joins and auto-reconnect relaunches.
        _host.Services.GetRequiredService<Core.Sessions.ISessionTracker>().SessionStarted += session =>
            _ = Services.RobloxWindowSizer.ApplyIfEnabledAsync(settingsService, session.ProcessId, CancellationToken.None);

        bool isUiTest = Environment.GetEnvironmentVariable("RBXMANAGER_UITEST") == "1";
        if (!isUiTest || settings.MultiInstanceEnabled)
        {
            EnsureGlobalRobloxMutexHeld();
            IInstanceGate? instanceGate = _host.Services.GetService<IInstanceGate>();
            instanceGate?.TryEnable();
        }

        if (settings.IsMasterPasswordEnabled)
        {
            Core.Storage.IVaultKeyProvider keyProvider = _host.Services.GetRequiredService<Core.Storage.IVaultKeyProvider>();
            Core.Storage.IAccountStore accountStore = _host.Services.GetRequiredService<Core.Storage.IAccountStore>();

            Views.Windows.MasterPasswordWindow masterPasswordWindow = new(keyProvider, accountStore);
            bool? unlocked = masterPasswordWindow.ShowDialog();
            if (unlocked != true)
            {
                Shutdown();
                return;
            }
        }

        MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void CleanupLeftoverLoginFolders()
    {
        CleanupLeftoverBrowserProfiles();
        try
        {
            string rbxTemp = Path.Combine(Path.GetTempPath(), "RbxManager");
#if DEBUG
            string? debugDataDir = Environment.GetEnvironmentVariable("RBXMANAGER_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(debugDataDir))
            {
                rbxTemp = Path.Combine(debugDataDir, "temp");
            }
#endif
            if (!Directory.Exists(rbxTemp))
            {
                return;
            }

            string[] leftoverDirs = Directory.GetDirectories(rbxTemp, "login-*");
            int deletedCount = 0;
            foreach (string dir in leftoverDirs)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    deletedCount++;
                }
                catch
                {
                    // Best effort, ignore errors on individual folders in use
                }
            }

            if (leftoverDirs.Length > 0)
            {
                Log.Information("Startup login folder cleanup: found {Total}, deleted {Deleted}.", leftoverDirs.Length, deletedCount);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Startup login folder cleanup failed: {ExceptionType}.", ex.GetType().Name);
        }
    }

    // CDP Chrome profiles from a crashed/killed previous run would otherwise accumulate in %TEMP% forever.
    private static void CleanupLeftoverBrowserProfiles()
    {
        try
        {
            string[] leftoverProfiles = Directory.GetDirectories(Path.GetTempPath(), "RbxManager_Browser_*");
            int deletedCount = 0;
            foreach (string dir in leftoverProfiles)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    deletedCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still locked by a live Chrome instance; next startup retries.
                }
            }

            if (leftoverProfiles.Length > 0)
            {
                Log.Information("Startup browser profile cleanup: found {Total}, deleted {Deleted}.", leftoverProfiles.Length, deletedCount);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Startup browser profile cleanup failed: {ExceptionType}.", ex.GetType().Name);
        }
    }

    private static void ApplyTheme(string theme)
    {
        try
        {
            Wpf.Ui.Appearance.ApplicationTheme appTheme = theme switch
            {
                var t when string.Equals(t, "Light", StringComparison.OrdinalIgnoreCase) => Wpf.Ui.Appearance.ApplicationTheme.Light,
                var t when string.Equals(t, "System", StringComparison.OrdinalIgnoreCase) => Wpf.Ui.Appearance.ApplicationTheme.HighContrast,
                _ => Wpf.Ui.Appearance.ApplicationTheme.Dark
            };
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(appTheme);
            RbxManager.App.Themes.ThemeTokens.ApplyTheme(appTheme);
        }
        catch
        {
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("RbxManager application shutting down.");

        try
        {
            // Stop in-flight 60–120s launch queues and any CDP Chrome left open before the host disposes services.
            _host.Services.GetService<AccountsViewModel>()?.CancelBulkLaunch();
            _host.Services.GetService<ServersViewModel>()?.CancelJoin();
            RbxManager.App.Services.IncognitoBrowserLauncher.CleanupAllSessions();
        }
        catch (Exception ex)
        {
            Log.Warning("Shutdown cancellation failed: {ExceptionType}.", ex.GetType().Name);
        }

        try
        {
            IInstanceGate? gate = _host.Services.GetService<IInstanceGate>();
            gate?.Disable();
            if (gate is IDisposable disposableGate)
            {
                disposableGate.Dispose();
            }
        }
        catch
        {
        }

        using (_host)
        {
            await _host.StopAsync();
        }

        if (_appSingleInstanceMutex != null)
        {
            try
            {
                _appSingleInstanceMutex.ReleaseMutex();
            }
            catch
            {
            }
            _appSingleInstanceMutex.Dispose();
            _appSingleInstanceMutex = null;
        }

        GC.KeepAlive(_globalRobloxMutex);
        GC.KeepAlive(_globalRobloxSingletonMutex);
        ReleaseGlobalRobloxMutex(ref _globalRobloxMutex);
        ReleaseGlobalRobloxMutex(ref _globalRobloxSingletonMutex);

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }

    private static void EnsureGlobalRobloxMutexHeld()
    {
        try
        {
            if (_globalRobloxMutex == null)
            {
                _globalRobloxMutex = new Mutex(false, @"Global\ROBLOX_singletonEvent", out bool _);
                ApplyLenientMutexSecurity(_globalRobloxMutex);
                GC.KeepAlive(_globalRobloxMutex);
            }

            if (_globalRobloxSingletonMutex == null)
            {
                _globalRobloxSingletonMutex = new Mutex(false, "ROBLOX_singletonMutex", out bool _);
                ApplyLenientMutexSecurity(_globalRobloxSingletonMutex);
                GC.KeepAlive(_globalRobloxSingletonMutex);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to initialize global Roblox singleton mutex: {ExceptionType}.", ex.GetType().Name);
        }
    }

    private static void ApplyLenientMutexSecurity(Mutex mutex)
    {
        try
        {
            MutexSecurity security = mutex.GetAccessControl();
            MutexAccessRule securityBehavior = new(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow);
            security.AddAccessRule(securityBehavior);
            mutex.SetAccessControl(security);
        }
        catch
        {
            // Ignore when attached to a pre-existing mutex with restricted ACL modification rights
        }
    }

    private static void ReleaseGlobalRobloxMutex(ref Mutex? mutex)
    {
        if (mutex != null)
        {
            try
            {
                mutex.Dispose();
            }
            catch
            {
            }
            mutex = null;
        }
    }
}
