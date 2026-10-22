namespace RbxManager.Core.Validation;

using System;
using System.Collections.Generic;
using RbxManager.Core.Common;

public static class GhostDisconnectKeywordValidator
{
    public const int MaxKeywordsCount = 20;
    public const int MinKeywordLength = 3;
    public const int MaxKeywordLength = 80;

    public static readonly string[] DefaultKeywords =
    [
        "Connection lost",
        "Error 277",
        "Error 268"
    ];

    public static Result Validate(IReadOnlyList<string>? keywords)
    {
        if (keywords == null || keywords.Count == 0)
        {
            return Result.Success();
        }

        if (keywords.Count > MaxKeywordsCount)
        {
            return Result.Failure($"Keywords count cannot exceed {MaxKeywordsCount}.", "TooManyKeywords");
        }

        foreach (string kw in keywords)
        {
            if (string.IsNullOrWhiteSpace(kw))
            {
                return Result.Failure("Keyword cannot be empty or whitespace.", "EmptyKeyword");
            }

            string trimmed = kw.Trim();
            if (trimmed.Length < MinKeywordLength || trimmed.Length > MaxKeywordLength)
            {
                return Result.Failure($"Keyword '{trimmed}' must be between {MinKeywordLength} and {MaxKeywordLength} characters.", "InvalidKeywordLength");
            }
        }

        return Result.Success();
    }
}
