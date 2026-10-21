namespace RbxManager.App;

using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure;
using RbxManager.Infrastructure.Storage;
using Serilog;
using Wpf.Ui;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        ConfigureCulture();
        ConfigureLogging();

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
            .WriteTo.File(
                path: "logs/app-.log",
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
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
        services.AddTransient<SettingsPage>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Information("RbxManager application starting up.");

        await _host.StartAsync();

        MainWindow mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("RbxManager application shutting down.");

        using (_host)
        {
            await _host.StopAsync();
        }

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }
}
