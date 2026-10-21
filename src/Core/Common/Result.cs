namespace RbxManager.Core.Common;

public sealed class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    internal Result(bool isSuccess, string? error, string? errorCode = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string? errorCode = null) => new(false, error, errorCode);

    public static Result<T> Success<T>(T value) => new(true, value, null, null);
    public static Result<T> Failure<T>(string error, string? errorCode = null) => new(false, default, error, errorCode);
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }

    internal Result(bool isSuccess, T? value, string? error, string? errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
    }
}

public static class AuthErrorCodes
{
    public const string Expired = "Expired";
    public const string RateLimited = "RateLimited";
    public const string Network = "Network";
    public const string Unexpected = "Unexpected";
}
