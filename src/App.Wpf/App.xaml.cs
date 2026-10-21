namespace RbxManager.App;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using RbxManager.Core.Instance;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure;
using RbxManager.Infrastructure.Logging;
using RbxManager.Infrastructure.Storage;
using Serilog;
using Wpf.Ui;

public partial class App : Application
{
    private const string AppMutexName = @"Local\RbxManager_Application_SingleInstance_Mutex";

    private static Mutex? _appSingleInstanceMutex;
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
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.With<RobloxSecretRedactionEnricher>()
            .WriteTo.File(
                path: "logs/app-.log",
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
            Log.Error("AppDomain unhandled exception of type {ExceptionType}. IsTerminating: {IsTerminating}.",
                ex?.GetType().FullName ?? "Unknown", e.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error("Unobserved task exception of type {ExceptionType}.",
                e.Exception?.InnerException?.GetType().FullName ?? e.Exception?.GetType().FullName ?? "Unknown");
            e.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Dispatcher unhandled exception of type {ExceptionType}.", e.Exception.GetType().FullName);
        e.Handled = true;

        try
        {
            MessageBox.Show(
                "Đã xảy ra lỗi không mong muốn trong ứng dụng.",
                "RbxManager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore failure displaying message box during critical UI failure
        }
    }

    private static void FocusExistingWindow()
    {
        try
        {
            Process current = Process.GetCurrentProcess();
            Process? other = Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id);

            if (other != null && other.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
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

        // UI Services
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();

        // Views & ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<AccountsViewModel>();
        services.AddTransient<AccountsPage>();
        services.AddTransient<ServersViewModel>();
        services.AddTransient<ServersPage>();
        services.AddTransient<RunningViewModel>();
        services.AddTransient<RunningPage>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsPage>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _appSingleInstanceMutex = new Mutex(true, AppMutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            FocusExistingWindow();
            Shutdown();
            return;
        }

        Log.Information("RbxManager application starting up.");

        await _host.StartAsync();

        Core.Settings.ISettingsService settingsService = _host.Services.GetRequiredService<Core.Settings.ISettingsService>();
        Core.Settings.AppSettings settings = await settingsService.LoadSettingsAsync();
        ApplyTheme(settings.Theme);

        MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
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

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }
}
