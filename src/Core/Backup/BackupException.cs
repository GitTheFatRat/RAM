namespace RbxManager.Core.Backup;

using System;

public sealed class BackupException : Exception
{
    public const string DefaultErrorMessage = "Invalid password or corrupted backup file.";

    public BackupException() : base(DefaultErrorMessage)
    {
    }

    public BackupException(string message) : base(message)
    {
    }

    public BackupException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
