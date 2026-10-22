namespace RbxManager.Core.Processes;

using System;
using System.Net;
using System.Net.Sockets;
using RbxManager.Core.Models;

public static class ProxyBrowserArgumentBuilder
{
    public static string? BuildAdditionalBrowserArguments(ProxyConfig? proxy)
    {
        if (proxy == null)
        {
            return null;
        }

        string scheme = proxy.Type switch
        {
            ProxyType.Http => "http",
            ProxyType.Socks5 => "socks5",
            _ => throw new ArgumentOutOfRangeException(nameof(proxy), $"Unsupported proxy type '{proxy.Type}'.")
        };

        string host = proxy.Host.Trim();
        string unbracketed = host;
        if (unbracketed.StartsWith('[') && unbracketed.EndsWith(']'))
        {
            unbracketed = unbracketed[1..^1];
        }

        if (IPAddress.TryParse(unbracketed, out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            host = $"[{unbracketed}]";
        }

        return $"--proxy-server={scheme}://{host}:{proxy.Port}";
    }
}
