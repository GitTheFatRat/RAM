namespace RbxManager.Core.Models;

using System;

public sealed record ProxyConfig(
    string Host,
    int Port,
    string? Username = null,
    SecretValue? Password = null,
    ProxyType Type = ProxyType.Http) : IDisposable
{
    private bool _disposed;

    public ProxyConfig DeepClone()
    {
        return new ProxyConfig(
            Host,
            Port,
            Username,
            Password?.Clone(),
            Type);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Password?.Dispose();
        _disposed = true;
    }
}
