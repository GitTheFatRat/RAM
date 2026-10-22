namespace RbxManager.Core.Common;

using RbxManager.Core.Auth;

public sealed class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }
    public AuthFailureDetails? Details { get; }

    internal Result(bool isSuccess, string? error, string? errorCode = null, AuthFailureDetails? details = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
        Details = details;
    }

    public static Result Success() => new(true, null, null, null);
    public static Result Failure(string error, string? errorCode = null, AuthFailureDetails? details = null) =>
        new(false, error, errorCode, details);

    public static Result<T> Success<T>(T value) => new(true, value, null, null, null);
    public static Result<T> Failure<T>(string error, string? errorCode = null, AuthFailureDetails? details = null) =>
        new(false, default, error, errorCode, details);
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public string? ErrorCode { get; }
    public AuthFailureDetails? Details { get; }

    internal Result(bool isSuccess, T? value, string? error, string? errorCode, AuthFailureDetails? details = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
        Details = details;
    }
}

public static class AuthErrorCodes
{
    public const string Expired = "Expired";
    public const string RateLimited = "RateLimited";
    public const string Network = "Network";
    public const string Unexpected = "Unexpected";
}
