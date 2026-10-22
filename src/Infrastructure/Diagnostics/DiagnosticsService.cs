namespace RbxManager.Infrastructure.Diagnostics;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Auth;
using RbxManager.Core.Common;
using RbxManager.Core.Diagnostics;
using RbxManager.Core.Instance;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Launch;

public sealed partial class DiagnosticsService : IDiagnosticsService
{
    private static readonly string[] EndpointsToProbe =
    [
        "https://www.roblox.com/",
        "https://users.roblox.com/",
        "https://auth.roblox.com/",
        "https://games.roblox.com/",
        "https://presence.roblox.com/",
        "https://apis.roblox.com/"
    ];

    private static readonly Regex SecretLeakRegex = new(
        @"(_\|WARNING:-DO-NOT-SHARE-THIS\S*)|(\.ROBLOSECURITY=\S*)|(\|_[A-Za-z0-9]{16,})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UsernamePathRegex = new(
        @"(?i)(?<=[A-Za-z]:[\\/]Users[\\/])[^\\/]+",
        RegexOptions.Compiled);

    private readonly IRegistryProvider _registryProvider;
    private readonly IProcessProvider _processProvider;
    private readonly IInstanceGate _instanceGate;
    private readonly ISettingsService _settingsService;
    private readonly IRobloxAuthService _authService;
    private readonly HttpClient _httpClient;

    public DiagnosticsService(
        IRegistryProvider registryProvider,
        IProcessProvider processProvider,
        IInstanceGate instanceGate,
        ISettingsService settingsService,
        IRobloxAuthService authService,
        HttpClient httpClient)
    {
        _registryProvider = registryProvider;
        _processProvider = processProvider;
        _instanceGate = instanceGate;
        _settingsService = settingsService;
        _authService = authService;
        _httpClient = httpClient;
    }

    public async Task<string> RunDiagnosticsAsync(Account? targetAccount = null, CancellationToken ct = default)
    {
        StringBuilder sb = new();

        sb.AppendLine("Safe to share: redacted. No cookies or tickets.");
        sb.AppendLine();

        // 1. Environment
        sb.AppendLine("[Environment]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"AppVersion: {typeof(DiagnosticsService).Assembly.GetName().Version?.ToString() ?? "1.0.0"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OsVersion: {Environment.OSVersion.VersionString}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine(CultureInfo.InvariantCulture, $".NetRuntime: {Environment.Version}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"IsElevated: {(_registryProvider.IsProcessElevated() ? "yes (WARNING: Running elevated is not recommended)" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"UiCulture: {CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"WebView2Runtime: {_registryProvider.GetWebView2Version() ?? "missing"}");
        sb.AppendLine();

        // 2. Roblox Client
        sb.AppendLine("[Roblox Client]");
        bool handlerExists = _registryProvider.ProtocolHandlerExists("roblox-player");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ProtocolHandlerRegistered: {(handlerExists ? "yes" : "no")}");

        string? command = _registryProvider.GetProtocolHandlerCommand("roblox-player");
        if (!string.IsNullOrWhiteSpace(command))
        {
            string bootstrapperFlag = string.Empty;
            if (command.Contains("Bloxstrap", StringComparison.OrdinalIgnoreCase))
            {
                bootstrapperFlag = " (Custom bootstrapper detected: Bloxstrap)";
            }
            else if (command.Contains("Fishstrap", StringComparison.OrdinalIgnoreCase))
            {
                bootstrapperFlag = " (Custom bootstrapper detected: Fishstrap)";
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"ProtocolHandlerCommand: {command}{bootstrapperFlag}");
        }
        else
        {
            sb.AppendLine("ProtocolHandlerCommand: None");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"StandardInstallFolderExists: {(_registryProvider.StandardRobloxInstallFolderExists() ? "yes" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RunningPlayerCount: {_processProvider.GetRobloxProcesses().Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"MultiInstanceAvailable: {(_instanceGate.IsAvailable ? "yes" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"MultiInstanceEnabled: {(_instanceGate.IsMultiInstanceEnabled ? "yes" : "no")}");
        sb.AppendLine();

        // 3. Network
        sb.AppendLine("[Network]");
        bool proxyInUse = false;
        try
        {
            Uri target = new("https://www.roblox.com/");
            Uri? proxy = System.Net.WebRequest.DefaultWebProxy?.GetProxy(target);
            if (proxy != null && !string.Equals(proxy.Host, target.Host, StringComparison.OrdinalIgnoreCase))
            {
                proxyInUse = true;
            }
        }
        catch
        {
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"SystemProxyInUse: {(proxyInUse ? "yes" : "no")}");

        foreach (string endpoint in EndpointsToProbe)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string statusStr;
            List<string> headerNames = [];

            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
                using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                sw.Stop();
                statusStr = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);

                foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
                {
                    headerNames.Add(header.Key);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                statusStr = ex.GetType().Name;
            }

            string headersInfo = headerNames.Count > 0 ? $", Headers=[{string.Join(", ", headerNames)}]" : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture, $"Endpoint {endpoint}: Status={statusStr}, Elapsed={sw.ElapsedMilliseconds}ms{headersInfo}");
        }
        sb.AppendLine();

        // 4. Templates
        sb.AppendLine("[Templates]");
        AdvancedSettings? advanced = _settingsService.CurrentSettings.Advanced;
        bool launchOverrideActive = !string.IsNullOrWhiteSpace(advanced?.LaunchUriTemplate);
        bool launchOverrideValid = !launchOverrideActive || LaunchTemplateValidator.ValidateLaunchUriTemplate(advanced!.LaunchUriTemplate).IsSuccess;
        sb.AppendLine(CultureInfo.InvariantCulture, $"LaunchUriTemplate: OverrideActive={(launchOverrideActive ? "yes" : "no")}, Valid={(launchOverrideValid ? "yes" : "no")}");

        bool placeOverrideActive = !string.IsNullOrWhiteSpace(advanced?.PlaceLauncherUrlTemplate);
        bool placeOverrideValid = !placeOverrideActive || LaunchTemplateValidator.ValidatePlaceLauncherUrlTemplate(advanced!.PlaceLauncherUrlTemplate).IsSuccess;
        sb.AppendLine(CultureInfo.InvariantCulture, $"PlaceLauncherUrlTemplate: OverrideActive={(placeOverrideActive ? "yes" : "no")}, Valid={(placeOverrideValid ? "yes" : "no")}");
        sb.AppendLine();

        // 5. Optional Per-Account Check
        if (targetAccount != null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"[Account Check: {targetAccount.Alias}]");

            // Cookie validation
            Result<RobloxUser> cookieResult = await _authService.ValidateCookieAsync(targetAccount.Cookie, ct).ConfigureAwait(false);
            if (cookieResult.IsSuccess)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"CookieValidation: Success (UserId={cookieResult.Value!.Id}, Username={cookieResult.Value.Name})");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"CookieValidation: Failed (Code={cookieResult.ErrorCode ?? "Unknown"})");
                if (cookieResult.Details != null)
                {
                    AppendAuthFailureDetails(sb, cookieResult.Details);
                }
            }

            // Auth Ticket & CSRF
            Result<SecretValue> ticketResult = await _authService.GetAuthTicketAsync(targetAccount.Cookie, ct).ConfigureAwait(false);
            bool ticketObtained = ticketResult.IsSuccess;
            if (ticketResult.IsSuccess && ticketResult.Value != null)
            {
                ticketResult.Value.Dispose();
            }

            bool csrfObtained = ticketResult.IsSuccess || (ticketResult.Details?.CsrfTokenObtained ?? false);
            sb.AppendLine(CultureInfo.InvariantCulture, $"CsrfTokenObtained: {(csrfObtained ? "yes" : "no")}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"AuthTicketObtained: {(ticketObtained ? "yes" : "no")}");

            if (!ticketResult.IsSuccess)
            {
                if (ticketResult.Details != null)
                {
                    AppendAuthFailureDetails(sb, ticketResult.Details);
                }
                else if (!string.IsNullOrWhiteSpace(ticketResult.Error))
                {
                    string truncatedMsg = ticketResult.Error.Length > 120
                        ? ticketResult.Error.Substring(0, 120)
                        : ticketResult.Error;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"TicketError: Code={ticketResult.ErrorCode ?? "Unknown"}, Message={truncatedMsg}");
                }
            }

            // Dry-run URI
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string dryRunUri = RobloxUriBuilder.BuildLaunchUri(
                "<ticket>",
                1818,
                null,
                unixMs,
                "123456789",
                advanced?.LaunchUriTemplate,
                advanced?.PlaceLauncherUrlTemplate);

            sb.AppendLine(CultureInfo.InvariantCulture, $"LaunchUriDryRun: {dryRunUri}");
            sb.AppendLine();
        }

        string rawReport = sb.ToString();
        return SanitizeReport(rawReport);
    }

    private static void AppendAuthFailureDetails(StringBuilder sb, AuthFailureDetails details)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"Stage: {details.Stage}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"HttpStatus: {(details.HttpStatus.HasValue ? details.HttpStatus.Value.ToString(CultureInfo.InvariantCulture) : "None")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RobloxErrorCodes: [{string.Join(", ", details.RobloxErrorCodes)}]");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RobloxMessage: {details.RobloxMessage ?? "None"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CsrfTokenObtained: {(details.CsrfTokenObtained ? "yes" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TicketHeaderPresent: {(details.TicketHeaderPresent ? "yes" : "no")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"ResponseHeaderNames: [{string.Join(", ", details.ResponseHeaderNames)}]");
    }

    public static string SanitizeReport(string report)
    {
        if (string.IsNullOrEmpty(report))
        {
            return report;
        }

        string redacted = SecretLeakRegex.Replace(report, "[REDACTED]");
        return UsernamePathRegex.Replace(redacted, "<user>");
    }
}
