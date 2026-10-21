namespace RbxManager.Core.Models;

using System;
using RbxManager.Core.Common;

public sealed record LaunchRequest(AccountId AccountId, long PlaceId, string? JobId = null)
{
    public Result Validate()
    {
        if (PlaceId <= 0)
        {
            return Result.Failure("PlaceId must be greater than 0.");
        }

        if (!string.IsNullOrWhiteSpace(JobId) && !Guid.TryParse(JobId.Trim(), out _))
        {
            return Result.Failure("JobId must be a valid GUID.");
        }

        return Result.Success();
    }
}
