namespace RbxManager.Infrastructure.Storage;

using System;

internal sealed class AccountDto
{
    public long Id { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string? Note { get; set; }
    public string Cookie { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string? Username { get; set; }
    public long? UserId { get; set; }
    public int Status { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
}
