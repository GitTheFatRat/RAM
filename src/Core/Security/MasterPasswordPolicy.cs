namespace RbxManager.Core.Security;

using System;
using RbxManager.Core.Common;

public static class MasterPasswordPolicy
{
    public const int MinimumLength = 12;

    public static Result Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return Result.Failure("Master password cannot be empty.");
        }

        if (password.Length < MinimumLength)
        {
            return Result.Failure($"Master password must be at least {MinimumLength} characters long.");
        }

        return Result.Success();
    }
}
