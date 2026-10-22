namespace RbxManager.App.Services;

using System;
using Microsoft.Web.WebView2.Core;
using RbxManager.Core.Models;
using RbxManager.Core.Security;

public static class WebViewProxyAuthHandler
{
    public static void HandleBasicAuthentication(
        CoreWebView2BasicAuthenticationRequestedEventArgs args,
        ProxyConfig? proxy)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (proxy == null)
        {
            args.Cancel = true;
            return;
        }

        CoreWebView2ChallengeAdapter adapter = new(args);
        bool shouldAuth = ProxyAuthenticationDecision.ShouldAuthenticate(
            adapter,
            proxy.Host,
            proxy.Username,
            proxy.Password != null && proxy.Password.HasContent);

        if (shouldAuth && !string.IsNullOrWhiteSpace(proxy.Username) && proxy.Password != null)
        {
            string revealedPassword = proxy.Password.Reveal();
            adapter.SetResponse(proxy.Username, revealedPassword);
        }
    }

    private sealed class CoreWebView2ChallengeAdapter : IBasicAuthenticationChallenge
    {
        private readonly CoreWebView2BasicAuthenticationRequestedEventArgs _args;

        public CoreWebView2ChallengeAdapter(CoreWebView2BasicAuthenticationRequestedEventArgs args)
        {
            _args = args;
        }

        public string? Uri => _args.Uri;
        public string? Challenge => _args.Challenge;

        public void SetResponse(string username, string password)
        {
            _args.Response.UserName = username;
            _args.Response.Password = password;
            _args.Cancel = false;
        }

        public void Cancel()
        {
            _args.Cancel = true;
        }
    }
}
