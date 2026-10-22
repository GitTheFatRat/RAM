namespace RbxManager.Core.Models;

using System;

public sealed record Account(
    AccountId Id,
    string Alias,
    string? Group,
    string? Note,
    SecretValue Cookie,
    DateTimeOffset CreatedAt,
    string? Username = null,
    long? UserId = null,
    AccountStatus Status = AccountStatus.Unknown,
    DateTimeOffset? LastCheckedAt = null);
