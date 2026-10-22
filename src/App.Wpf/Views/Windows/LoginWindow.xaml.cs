namespace RbxManager.App.Views.Windows;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RbxManager.App.Resources;
using RbxManager.App.Services;
using RbxManager.Core.Common;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Validation;
using RbxManager.Infrastructure.Http;
using Wpf.Ui.Controls;

public partial class LoginWindow : FluentWindow, IDisposable
{
    private string _tempUserDataFolder;
    private CancellationTokenSource? _pollingCts;
    private WebView2? _webView;
    private bool _isClosed;

    public SecretValue? CapturedCookie { get; private set; }
    public ProxyConfig? UsedProxy { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();

        _tempUserDataFolder = GenerateTempUserDataFolder();
        Closed += OnClosed;
    }

    private static string GenerateTempUserDataFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "RbxManager", "login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void OnProxyTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProxyFieldsPanel == null || TestProxyButton == null)
        {
            return;
        }

        bool isProxy = ProxyTypeComboBox.SelectedIndex > 0;
        ProxyFieldsPanel.Visibility = isProxy ? Visibility.Visible : Visibility.Collapsed;
        TestProxyButton.Visibility = isProxy ? Visibility.Visible : Visibility.Collapsed;
        ClearInlineStatus();
    }

    private void ClearInlineStatus()
    {
        if (InlineErrorTextBlock != null)
        {
            InlineErrorTextBlock.Text = string.Empty;
            InlineErrorTextBlock.Visibility = Visibility.Collapsed;
        }
        if (TestStatusTextBlock != null)
        {
            TestStatusTextBlock.Text = string.Empty;
            TestStatusTextBlock.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowInlineError(string error)
    {
        InlineErrorTextBlock.Text = error;
        InlineErrorTextBlock.Visibility = Visibility.Visible;
    }

    private ProxyConfig? BuildProxyFromInputs(out string? validationError)
    {
        validationError = null;
        if (ProxyTypeComboBox.SelectedIndex <= 0)
        {
            return null;
        }

        ProxyType type = ProxyTypeComboBox.SelectedIndex == 2 ? ProxyType.Socks5 : ProxyType.Http;
        string host = ProxyHostBox.Text?.Trim() ?? string.Empty;
        if (!int.TryParse(ProxyPortBox.Text?.Trim(), out int port))
        {
            validationError = "Proxy port must be a valid number.";
            return null;
        }

        string? username = string.IsNullOrWhiteSpace(ProxyUsernameBox.Text) ? null : ProxyUsernameBox.Text.Trim();
        string rawPassword = ProxyPasswordBox.Password;
        string? password = string.IsNullOrEmpty(rawPassword) ? null : rawPassword;

        Result validationResult = ProxyConfigValidator.ValidateStrict(host, port, username, password);
        if (!validationResult.IsSuccess)
        {
            validationError = validationResult.Error;
            return null;
        }

        SecretValue? passwordSecret = password != null ? SecretValue.FromString(password) : null;
        return new ProxyConfig(host, port, username, passwordSecret, type);
    }

    private async void OnTestProxyClick(object sender, RoutedEventArgs e)
    {
        ClearInlineStatus();
        using ProxyConfig? proxy = BuildProxyFromInputs(out string? error);
        if (error != null)
        {
            ShowInlineError(error);
            return;
        }

        TestStatusTextBlock.Text = Strings.TestingProxy;
        TestStatusTextBlock.Visibility = Visibility.Visible;
        TestProxyButton.IsEnabled = false;

        try
        {
            SocketsHttpHandler handler = RobloxHttpClientFactory.CreateHandler(proxy);
            handler.UseCookies = false;
            handler.AllowAutoRedirect = false;

            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            using HttpResponseMessage response = await client.GetAsync("https://www.roblox.com/").ConfigureAwait(true);
            string successFormat = Strings.ProxyTestSuccess;
            TestStatusTextBlock.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, successFormat, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            string failedFormat = Strings.ProxyTestFailed;
            TestStatusTextBlock.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, failedFormat, ex.Message);
        }
        finally
        {
            TestProxyButton.IsEnabled = true;
        }
    }

    private async void OnStartBrowserClick(object sender, RoutedEventArgs e)
    {
        ClearInlineStatus();
        ProxyConfig? proxy = BuildProxyFromInputs(out string? error);
        if (error != null)
        {
            ShowInlineError(error);
            return;
        }

        ProxyPasswordBox.Clear();
        UsedProxy = proxy;

        ProxyPanel.Visibility = Visibility.Collapsed;
        BrowserContainer.Visibility = Visibility.Visible;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingProgress.Visibility = Visibility.Visible;
        NavigationFailureOverlay.Visibility = Visibility.Collapsed;

        await InitializeBrowserAsync().ConfigureAwait(true);
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            _webView = new WebView2();
            WebViewHost.Children.Add(_webView);

            string? additionalArgs = ProxyBrowserArgumentBuilder.BuildAdditionalBrowserArguments(UsedProxy);
            CoreWebView2EnvironmentOptions? options = additionalArgs != null
                ? new CoreWebView2EnvironmentOptions(additionalBrowserArguments: additionalArgs)
                : null;

            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _tempUserDataFolder,
                options: options);

            await _webView.EnsureCoreWebView2Async(env);

            _webView.CoreWebView2.BasicAuthenticationRequested += OnBasicAuthenticationRequested;
            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Navigate("https://www.roblox.com/login");

            _pollingCts = new CancellationTokenSource();
            _ = StartCookiePollingAsync(_pollingCts.Token);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingProgress.Visibility = Visibility.Collapsed;

            System.Windows.MessageBox.Show(
                Strings.WebView2Missing,
                Strings.ErrorTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);

            ReturnToProxyPanel();
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingProgress.Visibility = Visibility.Collapsed;

            ShowInlineError(ex.Message);
            ReturnToProxyPanel();
        }
    }

    private void OnBasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        WebViewProxyAuthHandler.HandleBasicAuthentication(e, UsedProxy);
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

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingProgress.Visibility = Visibility.Collapsed;

        if (!e.IsSuccess)
        {
            NavigationFailureOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            NavigationFailureOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnChangeProxyClick(object sender, RoutedEventArgs e)
    {
        ReturnToProxyPanel();
    }

    private void ReturnToProxyPanel()
    {
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
            _pollingCts.Dispose();
            _pollingCts = null;
        }

        CleanupWebView();
        CleanTempFolder();
        _tempUserDataFolder = GenerateTempUserDataFolder();

        BrowserContainer.Visibility = Visibility.Collapsed;
        NavigationFailureOverlay.Visibility = Visibility.Collapsed;
        ProxyPanel.Visibility = Visibility.Visible;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
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

                if (_webView?.CoreWebView2 == null)
                {
                    break;
                }

                List<CoreWebView2Cookie> cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.roblox.com");
                CoreWebView2Cookie? roblosecurityCookie = cookies.FirstOrDefault(c => string.Equals(c.Name, ".ROBLOSECURITY", StringComparison.OrdinalIgnoreCase));

                if (roblosecurityCookie != null && !string.IsNullOrWhiteSpace(roblosecurityCookie.Value))
                {
                    CapturedCookie = SecretValue.FromString(roblosecurityCookie.Value);
                    DialogResult = true;
                    Close();
                    break;
                }
            }

            if (!ct.IsCancellationRequested && DateTimeOffset.UtcNow >= timeout)
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        Strings.LoginTimeoutMessage,
                        Strings.ErrorTitle,
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);

                    DialogResult = false;
                    Close();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on close
        }
        catch
        {
            // Polling exceptions should not crash the window
        }
    }

    private void CleanupWebView()
    {
        if (_webView != null)
        {
            try
            {
                WebViewHost.Children.Clear();
                _webView.Dispose();
            }
            catch
            {
                // Best effort cleanup
            }
            _webView = null;
        }
    }

    private void CleanTempFolder()
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_tempUserDataFolder))
                {
                    Directory.Delete(_tempUserDataFolder, recursive: true);
                }
                break;
            }
            catch
            {
                if (attempt < 5)
                {
                    Thread.Sleep(500);
                }
            }
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

        CleanupWebView();
        CleanTempFolder();

        if (DialogResult != true)
        {
            UsedProxy?.Dispose();
            UsedProxy = null;
        }
    }

    public void Dispose()
    {
        OnClosed(this, EventArgs.Empty);
        GC.SuppressFinalize(this);
    }
}
