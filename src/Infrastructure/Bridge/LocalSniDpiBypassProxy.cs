namespace RbxManager.Infrastructure.Bridge;

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

/// <summary>
/// Loopback-only, zero-decryption TLS Record Fragmentation proxy (127.0.0.1:48124) for *.roblox.com.
/// Splits the initial TLS ClientHello record across the "roblox.com" SNI boundary into two valid TLS records
/// so Vietnamese ISP DPI filters do not drop the handshake, while preserving 100% of the user's residential
/// Vietnam IP and real browser TLS (JA3/JA4) fingerprint without any VPN or MITM certificate.
/// </summary>
public sealed class LocalSniDpiBypassProxy : IDisposable
{
    public const int DefaultProxyPort = 48124;
    private const string InternetSettingsRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string AutoConfigUrlValueName = "AutoConfigURL";
    private const int InternetOptionSettingsChanged = 39;
    private const int InternetOptionRefresh = 37;

    private readonly int _port;
    private readonly bool _configureSystemPac;
    private readonly object _sync = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _previousAutoConfigUrl;
    private bool _pacConfiguredByUs;
    private bool _disposed;

    public LocalSniDpiBypassProxy(int port = DefaultProxyPort, bool configureSystemPac = true)
    {
        _port = port > 0 ? port : DefaultProxyPort;
        _configureSystemPac = configureSystemPac;
    }

    public int Port => _port;

    public string PacUrl => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_port}/proxy.pac");

    public bool Start()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            if (_listener != null)
            {
                return true;
            }

            try
            {
                TcpListener listener = new(IPAddress.Loopback, _port);
                listener.Start();

                CancellationTokenSource cts = new();
                _listener = listener;
                _cts = cts;
                _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token), CancellationToken.None);

                if (_configureSystemPac &&
                    !string.Equals(Environment.GetEnvironmentVariable("RBXMANAGER_UITEST"), "1", StringComparison.Ordinal))
                {
                    TryApplySystemPacLocked();
                }

                return true;
            }
            catch
            {
                StopLocked();
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            StopLocked();
        }
    }

    private void StopLocked()
    {
        if (_pacConfiguredByUs)
        {
            TryRestoreSystemPacLocked();
            _pacConfiguredByUs = false;
        }

        if (_cts != null)
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
            }

            _cts.Dispose();
            _cts = null;
        }

        if (_listener != null)
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
            }

            _listener = null;
        }
    }

    private void TryApplySystemPacLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsRegistryPath, writable: true);
            if (key == null)
            {
                return;
            }

            string? existing = key.GetValue(AutoConfigUrlValueName) as string;
            if (!string.IsNullOrWhiteSpace(existing) &&
                !existing.Contains("127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            {
                _previousAutoConfigUrl = existing;
            }
            else
            {
                _previousAutoConfigUrl = null;
            }

            key.SetValue(AutoConfigUrlValueName, PacUrl, RegistryValueKind.String);
            _pacConfiguredByUs = true;
            NotifyWinInetSettingsChanged();
        }
        catch
        {
        }
    }

    private void TryRestoreSystemPacLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(InternetSettingsRegistryPath, writable: true);
            if (key == null)
            {
                return;
            }

            string? current = key.GetValue(AutoConfigUrlValueName) as string;
            if (string.Equals(current, PacUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(_previousAutoConfigUrl))
                {
                    key.SetValue(AutoConfigUrlValueName, _previousAutoConfigUrl, RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(AutoConfigUrlValueName, throwOnMissingValue: false);
                }

                NotifyWinInetSettingsChanged();
            }
        }
        catch
        {
        }
    }

    private static void NotifyWinInetSettingsChanged()
    {
        try
        {
            _ = InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            _ = InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
        catch
        {
        }
    }

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;

                if (client.Client.RemoteEndPoint is IPEndPoint remoteEp && !IPAddress.IsLoopback(remoteEp.Address))
                {
                    return;
                }

                NetworkStream clientStream = client.GetStream();
                byte[] headerBuffer = new byte[4096];
                int totalRead = 0;

                while (totalRead < headerBuffer.Length)
                {
                    int read = await clientStream.ReadAsync(
                        headerBuffer.AsMemory(totalRead, headerBuffer.Length - totalRead),
                        ct).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return;
                    }

                    totalRead += read;
                    if (ContainsHeaderTerminator(headerBuffer, totalRead))
                    {
                        break;
                    }
                }

                string requestHeader = Encoding.ASCII.GetString(headerBuffer, 0, totalRead);
                int lineEnd = requestHeader.IndexOf('\n');
                string firstLine = (lineEnd >= 0 ? requestHeader[..lineEnd] : requestHeader).Trim();

                if (firstLine.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
                {
                    await WritePacResponseAsync(clientStream, ct).ConfigureAwait(false);
                    return;
                }

                if (!firstLine.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !TryParseAllowedRobloxTarget(parts[1], out string targetHost, out int targetPort))
                {
                    byte[] forbiddenBytes = Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n");
                    await clientStream.WriteAsync(forbiddenBytes, ct).ConfigureAwait(false);
                    return;
                }

                using TcpClient upstream = new()
                {
                    NoDelay = true,
                    ReceiveTimeout = 15000,
                    SendTimeout = 15000
                };

                using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(8));
                    await upstream.ConnectAsync(targetHost, targetPort, connectCts.Token).ConfigureAwait(false);
                }

                NetworkStream upstreamStream = upstream.GetStream();

                byte[] establishedBytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                await clientStream.WriteAsync(establishedBytes, ct).ConfigureAwait(false);
                await clientStream.FlushAsync(ct).ConfigureAwait(false);

                byte[] initialBuffer = new byte[16384];
                int initialRead = await clientStream.ReadAsync(initialBuffer, ct).ConfigureAwait(false);
                if (initialRead <= 0)
                {
                    return;
                }

                if (!await TryWriteSplitTlsClientHelloAsync(upstreamStream, initialBuffer, initialRead, ct).ConfigureAwait(false))
                {
                    await upstreamStream.WriteAsync(initialBuffer.AsMemory(0, initialRead), ct).ConfigureAwait(false);
                    await upstreamStream.FlushAsync(ct).ConfigureAwait(false);
                }

                Task clientToUpstream = clientStream.CopyToAsync(upstreamStream, ct);
                Task upstreamToClient = upstreamStream.CopyToAsync(clientStream, ct);
                await Task.WhenAny(clientToUpstream, upstreamToClient).ConfigureAwait(false);
            }
            catch
            {
                // Ignore normal socket disconnects or cancelled reads
            }
        }
    }

    private async Task WritePacResponseAsync(NetworkStream clientStream, CancellationToken ct)
    {
        string pacBody = string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            function FindProxyForURL(url, host) {
              var h = (host || "").toLowerCase();
              if (h === "www.roblox.com" || h === "roblox.com" || h.endsWith(".roblox.com")) {
                return "PROXY 127.0.0.1:{{_port}}; DIRECT";
              }
              return "DIRECT";
            }
            """);

        byte[] bodyBytes = Encoding.UTF8.GetBytes(pacBody);
        string header = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\nContent-Length: {bodyBytes.Length}\r\nCache-Control: no-cache, no-store, must-revalidate\r\nConnection: close\r\n\r\n");
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await clientStream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await clientStream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
        await clientStream.FlushAsync(ct).ConfigureAwait(false);
    }

    internal static bool TryParseAllowedRobloxTarget(string authority, out string host, out int port)
    {
        host = string.Empty;
        port = 443;

        if (string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        string trimmed = authority.Trim();
        int colonIdx = trimmed.LastIndexOf(':');
        if (colonIdx > 0)
        {
            if (!int.TryParse(trimmed[(colonIdx + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                port != 443)
            {
                return false;
            }

            host = trimmed[..colonIdx].Trim();
        }
        else
        {
            host = trimmed;
        }

        return string.Equals(host, "roblox.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<bool> TryWriteSplitTlsClientHelloAsync(
        Stream upstream,
        byte[] buffer,
        int count,
        CancellationToken ct)
    {
        if (count <= 5 || buffer[0] != 0x16)
        {
            return false;
        }

        int recordLength = (buffer[3] << 8) | buffer[4];
        if (recordLength <= 0 || recordLength + 5 > count)
        {
            return false;
        }

        int sniIndex = FindAsciiIgnoreCase(buffer, 5, recordLength, "roblox.com");
        if (sniIndex < 0)
        {
            return false;
        }

        int splitOffset = sniIndex + 3; // Split right after "rob" -> "rob" | "lox.com"
        int payloadLen1 = splitOffset - 5;
        int payloadLen2 = recordLength - payloadLen1;
        if (payloadLen1 <= 0 || payloadLen2 <= 0)
        {
            return false;
        }

        byte[] record1 = new byte[5 + payloadLen1];
        record1[0] = buffer[0];
        record1[1] = buffer[1];
        record1[2] = buffer[2];
        record1[3] = (byte)((payloadLen1 >> 8) & 0xFF);
        record1[4] = (byte)(payloadLen1 & 0xFF);
        Buffer.BlockCopy(buffer, 5, record1, 5, payloadLen1);

        byte[] record2 = new byte[5 + payloadLen2];
        record2[0] = buffer[0];
        record2[1] = buffer[1];
        record2[2] = buffer[2];
        record2[3] = (byte)((payloadLen2 >> 8) & 0xFF);
        record2[4] = (byte)(payloadLen2 & 0xFF);
        Buffer.BlockCopy(buffer, 5 + payloadLen1, record2, 5, payloadLen2);

        await upstream.WriteAsync(record1, ct).ConfigureAwait(false);
        await upstream.FlushAsync(ct).ConfigureAwait(false);

        await Task.Delay(15, ct).ConfigureAwait(false);

        await upstream.WriteAsync(record2, ct).ConfigureAwait(false);
        int trailingBytes = count - (recordLength + 5);
        if (trailingBytes > 0)
        {
            await upstream.WriteAsync(buffer.AsMemory(recordLength + 5, trailingBytes), ct).ConfigureAwait(false);
        }

        await upstream.FlushAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static bool ContainsHeaderTerminator(byte[] buffer, int length)
    {
        for (int i = 0; i <= length - 4; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static int FindAsciiIgnoreCase(byte[] buffer, int offset, int count, string needle)
    {
        int end = offset + count - needle.Length;
        for (int i = offset; i <= end; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                char c = (char)buffer[i + j];
                if (char.ToLowerInvariant(c) != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopLocked();
        }
    }
}
