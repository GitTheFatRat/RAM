namespace RbxManager.Core.Models;

using System;
using System.Security.Cryptography;
using System.Text;

public sealed class SecretValue : IDisposable, IEquatable<SecretValue>
{
    private readonly byte[] _bytes;
    private bool _disposed;

    private SecretValue(byte[] bytes)
    {
        _bytes = bytes;
    }

    public static SecretValue FromString(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        return new SecretValue(bytes);
    }

    /// <summary>
    /// Reveals the underlying secret as a string. This is the ONLY way to read the secret content.
    /// </summary>
    public string Reveal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Encoding.UTF8.GetString(_bytes);
    }

    /// <summary>
    /// Creates an independent clone of this secret value with a separate byte buffer.
    /// </summary>
    public SecretValue Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] copy = new byte[_bytes.Length];
        Array.Copy(_bytes, copy, _bytes.Length);
        return new SecretValue(copy);
    }

    /// <summary>
    /// Gets a value indicating whether the secret contains at least one non-ASCII whitespace byte.
    /// Evaluated directly on the underlying byte buffer without allocating a string.
    /// </summary>
    public bool HasContent
    {
        get
        {
            if (_disposed)
            {
                return false;
            }

            foreach (byte b in _bytes)
            {
                if (!IsAsciiWhitespace(b))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool IsAsciiWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0B or 0x0C;

    public override string ToString() => "[REDACTED]";

    public bool Equals(SecretValue? other)
    {
        if (other is null || _disposed || other._disposed)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    }

    public override bool Equals(object? obj) => Equals(obj as SecretValue);

    public override int GetHashCode() => 0;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_bytes);
        _disposed = true;
    }

    ~SecretValue()
    {
        Dispose(false);
    }
}
