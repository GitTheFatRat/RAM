namespace RbxManager.App.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

[SupportedOSPlatform("windows")]
public static class IncognitoBrowserLauncher
{
    public const string LoginUrl = "https://www.roblox.com/NewLogin";
    public const string SignUpUrl = "https://www.roblox.com/NewLogin#signup";
    public const string DpiBypassArgs = "--disable-quic --disable-features=EncryptedClientHello,Kyber,PostQuantumCECPQ2";

    private static readonly HttpClient CdpHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static event Action<string>? CookieCaptured;

    public static bool TryLaunch(string url = LoginUrl)
    {
        (string exePath, string args)[] knownPaths = GetCandidateBrowserPaths(url);
        foreach (var (exePath, args) in knownPaths)
        {
            if (File.Exists(exePath) && TryStartCdpBrowserProcess(exePath, args, url))
            {
                return true;
            }
        }

        foreach (var (cmdName, args) in GetPathBrowserCommands(url))
        {
            if (TryStartCdpBrowserProcess(cmdName, args, url))
            {
                return true;
            }
        }

        return TryStartProcess(url, string.Empty);
    }

    internal static (string CmdName, string Args)[] GetPathBrowserCommands(string url = LoginUrl)
    {
        string incognitoArgs = BuildIncognitoArguments(url);
        string inPrivateArgs = BuildInPrivateArguments(url);

        return
        [
            ("chrome.exe", incognitoArgs),
            ("thorium.exe", incognitoArgs),
            ("msedge.exe", inPrivateArgs),
            ("brave.exe", incognitoArgs)
        ];
    }

    internal static (string ExePath, string Args)[] GetCandidateBrowserPaths(string url = LoginUrl)
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string incognitoArgs = BuildIncognitoArguments(url);
        string inPrivateArgs = BuildInPrivateArguments(url);

        return
        [
            (Path.Combine(pf, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(pfX86, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"), incognitoArgs),
            (Path.Combine(localAppData, "Thorium", "Application", "thorium.exe"), incognitoArgs),
            (Path.Combine(pf, "Thorium", "Application", "thorium.exe"), incognitoArgs),
            (Path.Combine(pfX86, "Microsoft", "Edge", "Application", "msedge.exe"), inPrivateArgs),
            (Path.Combine(pf, "Microsoft", "Edge", "Application", "msedge.exe"), inPrivateArgs),
            (Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), incognitoArgs),
            (Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"), incognitoArgs)
        ];
    }

    internal static string BuildIncognitoArguments(string url = LoginUrl) =>
        $"--incognito {DpiBypassArgs} \"{url}\"";

    internal static string BuildInPrivateArguments(string url = LoginUrl) =>
        $"--inprivate {DpiBypassArgs} \"{url}\"";

    private static bool TryStartCdpBrowserProcess(string fileName, string baseArguments, string url)
    {
        int cdpPort = GetFreeLoopbackPort();
        if (cdpPort <= 0)
        {
            return TryStartProcess(fileName, baseArguments);
        }

        string profileDir = Path.Combine(
            Path.GetTempPath(),
            "RbxManager_Browser_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(profileDir);
            string fullArgs = $"--user-data-dir=\"{profileDir}\" --no-first-run --no-default-browser-check --remote-debugging-port={cdpPort} {baseArguments}";
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = fullArgs,
                UseShellExecute = false
            };

            Process? proc = Process.Start(psi);
            if (proc == null)
            {
                TryDeleteDirectory(profileDir);
                return false;
            }

            bool isSignUp = url.Contains("#signup", StringComparison.OrdinalIgnoreCase);
            _ = Task.Run(() => RunCdpSessionAsync(cdpPort, isSignUp, profileDir, proc));
            return true;
        }
        catch
        {
            TryDeleteDirectory(profileDir);
            return false;
        }
    }

    private static bool TryStartProcess(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            using Process? proc = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int GetFreeLoopbackPort()
    {
        try
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task RunCdpSessionAsync(int port, bool isSignUp, string profileDir, Process browserProcess)
    {
        using (browserProcess)
        {
            try
            {
                using CancellationTokenSource cts = new(TimeSpan.FromMinutes(15));
                string? wsUrl = await WaitForRobloxPageWebSocketAsync(port, browserProcess, cts.Token).ConfigureAwait(false);
                if (wsUrl != null)
                {
                    await DrivePageOverCdpAsync(wsUrl, isSignUp, browserProcess, cts.Token).ConfigureAwait(false);
                }

                while (!browserProcess.HasExited && !cts.IsCancellationRequested)
                {
                    await Task.Delay(1000, cts.Token).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore background CDP session teardown errors
            }
            finally
            {
                await Task.Delay(1000).ConfigureAwait(false);
                TryDeleteDirectory(profileDir);
            }
        }
    }

    private static async Task<string?> WaitForRobloxPageWebSocketAsync(int port, Process browserProcess, CancellationToken ct)
    {
        string listEndpoint = $"http://127.0.0.1:{port}/json";
        for (int attempt = 0; attempt < 80 && !browserProcess.HasExited && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                string json = await CdpHttpClient.GetStringAsync(listEndpoint, ct).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    string? type = item.TryGetProperty("type", out JsonElement typeProp) ? typeProp.GetString() : null;
                    string? url = item.TryGetProperty("url", out JsonElement urlProp) ? urlProp.GetString() : null;
                    string? ws = item.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsProp) ? wsProp.GetString() : null;

                    if (string.Equals(type, "page", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(ws) &&
                        url != null &&
                        url.Contains("roblox.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return ws;
                    }
                }
            }
            catch
            {
                // Browser HTTP server not ready yet
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task DrivePageOverCdpAsync(string wsUrl, bool isSignUp, Process browserProcess, CancellationToken ct)
    {
        using ClientWebSocket ws = new();
        await ws.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

        int msgId = 1;
        await SendCdpCommandAsync(ws, msgId++, "Page.enable", "{}", ct).ConfigureAwait(false);
        await SendCdpCommandAsync(ws, msgId++, "Runtime.enable", "{}", ct).ConfigureAwait(false);
        await SendCdpCommandAsync(ws, msgId++, "Network.enable", "{}", ct).ConfigureAwait(false);

        if (isSignUp)
        {
            string addScriptParams = JsonSerializer.Serialize(new { source = SignUpDomScript });
            await SendCdpCommandAsync(ws, msgId++, "Page.addScriptToEvaluateOnNewDocument", addScriptParams, ct).ConfigureAwait(false);

            string evalParams = JsonSerializer.Serialize(new { expression = SignUpDomScript, returnByValue = true });
            await SendCdpCommandAsync(ws, msgId++, "Runtime.evaluate", evalParams, ct).ConfigureAwait(false);
        }

        bool cookieReported = false;
        while (!browserProcess.HasExited && ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            if (isSignUp)
            {
                string evalParams = JsonSerializer.Serialize(new { expression = SignUpDomScript, returnByValue = true });
                await SendCdpCommandAsync(ws, msgId++, "Runtime.evaluate", evalParams, ct).ConfigureAwait(false);
            }

            int cookieCmdId = msgId++;
            await SendCdpCommandAsync(ws, cookieCmdId, "Network.getAllCookies", "{}", ct).ConfigureAwait(false);
            string? capturedCookie = await ReadUntilCommandResponseAsync(ws, cookieCmdId, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(capturedCookie))
            {
                string showBannerJs = $"window.__showCapturedRobloxCookie && window.__showCapturedRobloxCookie({JsonSerializer.Serialize(capturedCookie)});";
                string bannerParams = JsonSerializer.Serialize(new { expression = showBannerJs, returnByValue = true });
                await SendCdpCommandAsync(ws, msgId++, "Runtime.evaluate", bannerParams, ct).ConfigureAwait(false);

                if (!cookieReported)
                {
                    cookieReported = true;
                    CookieCaptured?.Invoke(capturedCookie);
                }
            }

            await Task.Delay(1200, ct).ConfigureAwait(false);
        }
    }

    private static async Task SendCdpCommandAsync(ClientWebSocket ws, int id, string method, string rawParamsJson, CancellationToken ct)
    {
        string payload = $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{rawParamsJson}}}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadUntilCommandResponseAsync(ClientWebSocket ws, int expectedId, CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        using CancellationTokenSource readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readTimeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            while (ws.State == WebSocketState.Open && !readTimeout.IsCancellationRequested)
            {
                using MemoryStream ms = new();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, readTimeout.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                string message = Encoding.UTF8.GetString(ms.ToArray());
                using JsonDocument doc = JsonDocument.Parse(message);
                if (!doc.RootElement.TryGetProperty("id", out JsonElement idProp) || idProp.GetInt32() != expectedId)
                {
                    continue;
                }

                if (doc.RootElement.TryGetProperty("result", out JsonElement resProp) &&
                    resProp.TryGetProperty("cookies", out JsonElement cookiesProp) &&
                    cookiesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement cookie in cookiesProp.EnumerateArray())
                    {
                        string? name = cookie.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                        string? val = cookie.TryGetProperty("value", out JsonElement v) ? v.GetString() : null;
                        if (string.Equals(name, ".ROBLOSECURITY", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }
                }

                return null;
            }
        }
        catch
        {
            // Ignore read timeout and continue polling loop
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore locked temp files
        }
    }

    internal const string SignUpDomScript = """
        (function() {
          window.__showCapturedRobloxCookie = function(cookieVal) {
            if (!cookieVal) return;
            var existing = document.getElementById('rbx-mgr-cookie-banner');
            if (existing) {
              var ta = document.getElementById('rbx-mgr-cookie-textarea');
              if (ta && ta.value !== cookieVal) ta.value = cookieVal;
              return;
            }
            var banner = document.createElement('div');
            banner.id = 'rbx-mgr-cookie-banner';
            banner.style.cssText = 'position:fixed;top:12px;left:50%;transform:translateX(-50%);z-index:2147483647;background:#143323;border:2px solid #22c55e;color:#fff;padding:16px 20px;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,0.65);width:92%;max-width:580px;font-family:Segoe UI,sans-serif;';
            banner.innerHTML = '<div style="font-weight:700;font-size:15px;color:#4ade80;margin-bottom:6px;">✅ Đã lấy thành công Cookie .ROBLOSECURITY (Đã tự động điền vào RbxManager!)</div>' +
              '<textarea id="rbx-mgr-cookie-textarea" readonly style="width:100%;height:56px;background:#0b1c13;color:#bbf7d0;border:1px solid #15803d;border-radius:6px;padding:6px;font-size:12px;font-family:Consolas,monospace;resize:none;">' + cookieVal + '</textarea>' +
              '<div style="margin-top:8px;display:flex;gap:10px;justify-content:flex-end;">' +
              '<button id="rbx-mgr-copy-cookie-btn" type="button" style="background:#22c55e;color:#052e16;border:none;border-radius:6px;padding:7px 16px;font-weight:700;cursor:pointer;">📋 Sao chép Cookie</button>' +
              '</div>';
            document.body.appendChild(banner);
            var copyBtn = document.getElementById('rbx-mgr-copy-cookie-btn');
            if (copyBtn) {
              copyBtn.onclick = function() {
                var ta = document.getElementById('rbx-mgr-cookie-textarea');
                if (ta) { ta.select(); document.execCommand('copy'); }
                if (navigator.clipboard) navigator.clipboard.writeText(cookieVal);
                copyBtn.textContent = '✓ Đã sao chép!';
              };
            }
          };

          if (document.getElementById('rbx-mgr-signup-root')) return 'already_mounted';
          if (!document.body) return 'no_body';

          var style = document.getElementById('rbx-mgr-signup-style');
          if (!style) {
            style = document.createElement('style');
            style.id = 'rbx-mgr-signup-style';
            style.textContent = `
              body.rbx-mgr-signup-mode #react-login-web-app,
              body.rbx-mgr-signup-mode #react-login-container { display: none !important; }
              #rbx-mgr-signup-root {
                background: #232527;
                color: #ffffff;
                max-width: 430px;
                margin: 42px auto;
                padding: 28px 26px;
                border-radius: 12px;
                border: 1px solid #393b3d;
                box-shadow: 0 12px 36px rgba(0, 0, 0, 0.55);
                font-family: 'Builder Sans', 'Segoe UI', Arial, sans-serif;
                position: relative;
                z-index: 10;
              }
              #rbx-mgr-signup-root h2 {
                margin: 0 0 6px 0;
                font-size: 24px;
                font-weight: 800;
                text-align: center;
                color: #fff;
              }
              #rbx-mgr-signup-root .rbx-su-sub {
                text-align: center;
                font-size: 13px;
                color: #b8b8b8;
                margin-bottom: 18px;
              }
              #rbx-mgr-signup-root label {
                display: block;
                font-size: 13px;
                font-weight: 600;
                color: #d4d4d8;
                margin: 12px 0 6px 0;
              }
              #rbx-mgr-signup-root input, #rbx-mgr-signup-root select {
                width: 100%;
                box-sizing: border-box;
                background: #111214;
                border: 1px solid #4b4d50;
                color: #fff;
                border-radius: 8px;
                padding: 10px 12px;
                font-size: 14px;
                outline: none;
              }
              #rbx-mgr-signup-root input:focus, #rbx-mgr-signup-root select:focus {
                border-color: #3b82f6;
              }
              #rbx-mgr-signup-root .rbx-su-row {
                display: flex;
                gap: 8px;
              }
              #rbx-mgr-signup-root .rbx-su-msg {
                font-size: 12px;
                min-height: 16px;
                margin-top: 4px;
              }
              #rbx-mgr-signup-root .rbx-su-gender-btn {
                flex: 1;
                background: #111214;
                border: 1px solid #4b4d50;
                color: #d4d4d8;
                padding: 9px;
                border-radius: 8px;
                cursor: pointer;
                font-weight: 600;
                font-size: 13px;
              }
              #rbx-mgr-signup-root .rbx-su-gender-btn.active {
                border-color: #3b82f6;
                background: #1e293b;
                color: #60a5fa;
              }
              #rbx-su-submit {
                width: 100%;
                margin-top: 18px;
                background: #2563eb;
                color: #ffffff;
                border: none;
                border-radius: 8px;
                padding: 12px;
                font-size: 15px;
                font-weight: 700;
                cursor: pointer;
              }
              #rbx-su-submit:hover { background: #1d4ed8; }
              #rbx-su-submit:disabled { opacity: 0.6; cursor: wait; }
            `;
            document.head.appendChild(style);
          }

          document.body.classList.add('rbx-mgr-signup-mode');
          document.title = 'Đăng ký Roblox - Create Account';

          var root = document.createElement('div');
          root.id = 'rbx-mgr-signup-root';

          var dayOpts = '';
          for (var d = 1; d <= 31; d++) dayOpts += '<option value="' + d + '"' + (d === 15 ? ' selected' : '') + '>Ngày ' + d + '</option>';
          var monthOpts = '';
          for (var m = 1; m <= 12; m++) monthOpts += '<option value="' + m + '"' + (m === 6 ? ' selected' : '') + '>Tháng ' + m + '</option>';
          var yearOpts = '';
          for (var y = 2020; y >= 1950; y--) yearOpts += '<option value="' + y + '"' + (y === 2000 ? ' selected' : '') + '>' + y + '</option>';

          root.innerHTML = `
            <h2>Đăng ký Roblox</h2>
            <div class="rbx-su-sub">Tạo tài khoản Roblox trực tiếp (Bypass VNG Redirect)</div>

            <label>Ngày sinh (Birthday)</label>
            <div class="rbx-su-row">
              <select id="rbx-su-day">${dayOpts}</select>
              <select id="rbx-su-month">${monthOpts}</select>
              <select id="rbx-su-year">${yearOpts}</select>
            </div>

            <label>Tên người dùng (Username)</label>
            <input id="rbx-su-username" type="text" placeholder="Từ 3-20 ký tự, không dùng dấu cách" maxlength="20" autocomplete="off" />
            <div id="rbx-su-username-msg" class="rbx-su-msg"></div>

            <label>Mật khẩu (Password)</label>
            <input id="rbx-su-password" type="password" placeholder="Tối thiểu 8 ký tự" autocomplete="new-password" />
            <div id="rbx-su-password-msg" class="rbx-su-msg"></div>

            <label>Giới tính (Tùy chọn)</label>
            <div class="rbx-su-row">
              <button type="button" id="rbx-su-gender-f" class="rbx-su-gender-btn">♀ Nữ (Female)</button>
              <button type="button" id="rbx-su-gender-m" class="rbx-su-gender-btn active">♂ Nam (Male)</button>
            </div>

            <div id="rbx-su-status" style="margin-top:12px;font-size:13px;text-align:center;min-height:18px;color:#fbbf24;"></div>
            <button type="button" id="rbx-su-submit">Đăng ký (Sign Up)</button>

            <div style="margin-top:14px;text-align:center;">
              <a href="javascript:void(0)" id="rbx-su-switch-login" style="color:#93c5fd;font-size:13px;text-decoration:none;">Đã có tài khoản? Chuyển sang Đăng nhập</a>
            </div>
          `;

          var contentEl = document.getElementById('content') || document.body;
          contentEl.insertBefore(root, contentEl.firstChild);

          var selectedGender = 2;
          var btnF = document.getElementById('rbx-su-gender-f');
          var btnM = document.getElementById('rbx-su-gender-m');
          btnF.onclick = function() { selectedGender = 3; btnF.classList.add('active'); btnM.classList.remove('active'); };
          btnM.onclick = function() { selectedGender = 2; btnM.classList.add('active'); btnF.classList.remove('active'); };

          document.getElementById('rbx-su-switch-login').onclick = function() {
            document.body.classList.remove('rbx-mgr-signup-mode');
            root.style.display = 'none';
          };

          function getBirthdayIso() {
            var y = document.getElementById('rbx-su-year').value;
            var m = ('0' + document.getElementById('rbx-su-month').value).slice(-2);
            var d = ('0' + document.getElementById('rbx-su-day').value).slice(-2);
            return y + '-' + m + '-' + d + 'T00:00:00.000Z';
          }

          var uInput = document.getElementById('rbx-su-username');
          var uMsg = document.getElementById('rbx-su-username-msg');
          var uTimer = null;
          uInput.oninput = function() {
            clearTimeout(uTimer);
            var val = uInput.value.trim();
            if (val.length < 3) {
              uMsg.style.color = '#f87171';
              uMsg.textContent = 'Tên người dùng cần ít nhất 3 ký tự.';
              return;
            }
            uMsg.style.color = '#9ca3af';
            uMsg.textContent = 'Đang kiểm tra tên người dùng...';
            uTimer = setTimeout(function() {
              if (!window.CoreUtilities || !window.CoreUtilities.httpService) return;
              window.CoreUtilities.httpService.get(
                { url: 'https://auth.roblox.com/v1/usernames/validate', withCredentials: true },
                { username: val, birthday: getBirthdayIso(), context: 'Signup' }
              ).then(function(r) {
                if (r && r.data && r.data.code === 0) {
                  uMsg.style.color = '#4ade80';
                  uMsg.textContent = '✓ Tên người dùng hợp lệ!';
                } else {
                  uMsg.style.color = '#f87171';
                  uMsg.textContent = (r && r.data && r.data.message) || 'Tên người dùng không hợp lệ.';
                }
              }).catch(function() {});
            }, 350);
          };

          var pInput = document.getElementById('rbx-su-password');
          var pMsg = document.getElementById('rbx-su-password-msg');
          var pTimer = null;
          pInput.oninput = function() {
            clearTimeout(pTimer);
            var val = pInput.value;
            if (val.length < 8) {
              pMsg.style.color = '#f87171';
              pMsg.textContent = 'Mật khẩu cần ít nhất 8 ký tự.';
              return;
            }
            pTimer = setTimeout(function() {
              if (!window.CoreUtilities || !window.CoreUtilities.httpService) return;
              window.CoreUtilities.httpService.post(
                { url: 'https://auth.roblox.com/v2/passwords/validate', withCredentials: true },
                { username: uInput.value.trim() || 'RobloxUser', password: val }
              ).then(function(r) {
                if (r && r.data && r.data.code === 0) {
                  pMsg.style.color = '#4ade80';
                  pMsg.textContent = '✓ Mật khẩu hợp lệ!';
                } else {
                  pMsg.style.color = '#f87171';
                  pMsg.textContent = (r && r.data && r.data.message) || 'Mật khẩu yếu.';
                }
              }).catch(function() {});
            }, 350);
          };

          var submitBtn = document.getElementById('rbx-su-submit');
          var statusEl = document.getElementById('rbx-su-status');
          submitBtn.onclick = function() {
            var username = uInput.value.trim();
            var password = pInput.value;
            if (username.length < 3) {
              statusEl.style.color = '#f87171';
              statusEl.textContent = 'Vui lòng nhập tên người dùng hợp lệ (3-20 ký tự).';
              return;
            }
            if (password.length < 8) {
              statusEl.style.color = '#f87171';
              statusEl.textContent = 'Vui lòng nhập mật khẩu ít nhất 8 ký tự.';
              return;
            }
            if (!window.CoreUtilities || !window.CoreUtilities.httpService) {
              statusEl.style.color = '#fbbf24';
              statusEl.textContent = 'Đang khởi tạo dịch vụ bảo mật Roblox, vui lòng thử lại sau 2 giây...';
              return;
            }

            submitBtn.disabled = true;
            statusEl.style.color = '#fbbf24';
            statusEl.textContent = 'Đang gửi yêu cầu đăng ký (Hãy giải CAPTCHA nếu xuất hiện)...';

            var defaultAgreements = ['bcd1bb0e-5a98-4eb6-97e0-39170f27d032', '5c8b04b1-0890-4fa3-8c95-355808945530'];
            window.CoreUtilities.httpService.get({
              url: 'https://apis.roblox.com/user-agreements/v1/agreements-resolution/web',
              withCredentials: true
            }).then(function(agRes) {
              if (agRes && Array.isArray(agRes.data) && agRes.data.length > 0) {
                return agRes.data.map(function(x) { return x.id; });
              }
              return defaultAgreements;
            }).catch(function() {
              return defaultAgreements;
            }).then(function(agreementIds) {
              return window.CoreUtilities.httpService.post(
                { url: 'https://auth.roblox.com/v2/signup', withCredentials: true },
                {
                  username: username,
                  password: password,
                  birthday: getBirthdayIso(),
                  gender: selectedGender,
                  isTosAgreementBoxChecked: true,
                  agreementIds: agreementIds
                }
              );
            }).then(function(res) {
              submitBtn.disabled = false;
              statusEl.style.color = '#4ade80';
              statusEl.textContent = '🎉 Đăng ký thành công! Cookie .ROBLOSECURITY đã được tự động chuyển về phần mềm!';
            }).catch(function(err) {
              submitBtn.disabled = false;
              var errMsg = 'Đăng ký chưa hoàn tất hoặc CAPTCHA bị hủy. Vui lòng thử lại.';
              if (err && err.data && Array.isArray(err.data.errors) && err.data.errors[0]) {
                errMsg = err.data.errors[0].message || errMsg;
              }
              statusEl.style.color = '#f87171';
              statusEl.textContent = errMsg;
            });
          };

          return 'mounted';
        })();
        """;
}
