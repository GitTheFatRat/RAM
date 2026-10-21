namespace RbxManager.App.Views.Windows;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RbxManager.App.Resources;
using RbxManager.Core.Models;
using Wpf.Ui.Controls;

public partial class LoginWindow : FluentWindow, IDisposable
{
    private readonly string _tempUserDataFolder;
    private CancellationTokenSource? _pollingCts;
    private bool _isClosed;

    public SecretValue? CapturedCookie { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();

        _tempUserDataFolder = Path.Combine(Path.GetTempPath(), "RbxManager", "login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempUserDataFolder);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadingProgress.Visibility = Visibility.Visible;

        try
        {
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _tempUserDataFolder);
            await WebViewControl.EnsureCoreWebView2Async(environment);

            WebViewControl.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebViewControl.CoreWebView2.Navigate("https://www.roblox.com/login");

            _pollingCts = new CancellationTokenSource();
            _ = StartCookiePollingAsync(_pollingCts.Token);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            System.Windows.MessageBox.Show(
                Strings.WebView2Missing,
                Strings.ErrorTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                ex.Message,
                Strings.ErrorTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            DialogResult = false;
            Close();
        }
        finally
        {
            LoadingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri))
        {
            string host = uri.Host;
            bool isAllowed = string.Equals(host, "roblox.com", StringComparison.OrdinalIgnoreCase) ||
                             host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);

            if (!isAllowed)
            {
                e.Cancel = true;
            }
        }
        else
        {
            e.Cancel = true;
        }
    }

    private async Task StartCookiePollingAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        DateTimeOffset timeout = DateTimeOffset.UtcNow.AddMinutes(10);

        try
        {
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < timeout)
            {
                await timer.WaitForNextTickAsync(ct);

                if (WebViewControl.CoreWebView2 == null)
                {
                    break;
                }

                var cookies = await WebViewControl.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
                var roblosecurityCookie = cookies.FirstOrDefault(c => string.Equals(c.Name, ".ROBLOSECURITY", StringComparison.OrdinalIgnoreCase));

                if (roblosecurityCookie != null && !string.IsNullOrWhiteSpace(roblosecurityCookie.Value))
                {
                    CapturedCookie = SecretValue.FromString(roblosecurityCookie.Value);
                    DialogResult = true;
                    Close();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled when window closed
        }
        catch
        {
            // Polling exceptions should not crash the window
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;

        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
            _pollingCts = null;
        }

        try
        {
            WebViewControl.Dispose();
        }
        catch
        {
            // Best effort cleanup
        }

        try
        {
            if (Directory.Exists(_tempUserDataFolder))
            {
                Directory.Delete(_tempUserDataFolder, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    public void Dispose()
    {
        OnClosed(this, EventArgs.Empty);
        GC.SuppressFinalize(this);
    }
}
