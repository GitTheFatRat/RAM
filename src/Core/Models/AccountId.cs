namespace RbxManager.Core.Models;

using System.Globalization;

public readonly record struct AccountId
{
    public long Value { get; }

    public AccountId(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Roblox account ID must be positive.");
        }

        Value = value;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public static implicit operator long(AccountId accountId) => accountId.Value;
}
