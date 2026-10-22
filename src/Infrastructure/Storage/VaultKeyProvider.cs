namespace RbxManager.Infrastructure.Storage;

using System;
using RbxManager.Core.Models;
using RbxManager.Core.Storage;

public sealed class VaultKeyProvider : IVaultKeyProvider, IDisposable
{
    private readonly object _lock = new();
    private SecretValue? _key;
    private bool _disposed;

    public bool HasKey
    {
        get
        {
            lock (_lock)
            {
                return !_disposed && _key != null && _key.HasContent;
            }
        }
    }

    public SecretValue? GetKey()
    {
        lock (_lock)
        {
            return _disposed ? null : _key;
        }
    }

    public void SetKey(SecretValue? key)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            if (_key != null && !ReferenceEquals(_key, key))
            {
                _key.Dispose();
            }

            _key = key;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_key != null)
            {
                _key.Dispose();
                _key = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _key?.Dispose();
            _key = null;
            _disposed = true;
        }
    }
}
