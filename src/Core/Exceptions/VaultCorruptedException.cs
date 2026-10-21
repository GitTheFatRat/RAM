namespace RbxManager.Core.Exceptions;

using System;

public sealed class VaultCorruptedException : Exception
{
    public VaultCorruptedException(string message) : base(message)
    {
    }

    public VaultCorruptedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
