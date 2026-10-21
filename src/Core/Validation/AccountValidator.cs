namespace RbxManager.Core.Validation;

using System;
using System.Collections.Generic;
using System.Linq;
using RbxManager.Core.Common;
using RbxManager.Core.Models;

public static class AccountValidator
{
    public static Result Validate(Account account, IEnumerable<Account>? existingAccounts = null)
    {
        ArgumentNullException.ThrowIfNull(account);

        string trimmedAlias = account.Alias?.Trim() ?? string.Empty;
        if (trimmedAlias.Length == 0)
        {
            return Result.Failure("Alias is required.");
        }

        if (trimmedAlias.Length > 50)
        {
            return Result.Failure("Alias must be between 1 and 50 characters.");
        }

        if (account.Group is { Length: > 30 })
        {
            return Result.Failure("Group must not exceed 30 characters.");
        }

        if (account.Note is { Length: > 200 })
        {
            return Result.Failure("Note must not exceed 200 characters.");
        }

        string revealedCookie = account.Cookie.Reveal();
        if (string.IsNullOrWhiteSpace(revealedCookie))
        {
            return Result.Failure("Cookie must not be empty.");
        }

        if (existingAccounts != null)
        {
            bool isDuplicate = existingAccounts.Any(existing =>
                existing.Id != account.Id &&
                string.Equals(existing.Alias.Trim(), trimmedAlias, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                return Result.Failure($"An account with alias '{trimmedAlias}' already exists.");
            }
        }

        return Result.Success();
    }
}
