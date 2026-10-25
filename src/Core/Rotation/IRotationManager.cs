namespace RbxManager.Core.Rotation;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RbxManager.Core.Common;

public sealed record RotationPlan(IReadOnlyList<string> Groups, TimeSpan GroupDuration, long PlaceId)
{
    public static readonly TimeSpan MinimumGroupDuration = TimeSpan.FromMinutes(1);

    public Result Validate()
    {
        if (Groups.Count == 0)
        {
            return Result.Failure("Select at least one group.", "NoGroups");
        }

        if (GroupDuration < MinimumGroupDuration)
        {
            return Result.Failure("Group duration must be at least 1 minute.", "DurationTooShort");
        }

        return PlaceId > 0 ? Result.Success() : Result.Failure("PlaceId must be greater than 0.", "InvalidPlaceId");
    }
}

public enum RotationPhase
{
    Idle,
    Launching,
    Farming,
    Stopping,
    Cleaning
}

public sealed record RotationStatus(RotationPhase Phase, string? CurrentGroup, DateTimeOffset? NextSwitchAt)
{
    public static readonly RotationStatus Idle = new(RotationPhase.Idle, null, null);

    public bool IsRunning => Phase != RotationPhase.Idle;
}

public interface IRotationManager
{
    RotationStatus Status { get; }

    event Action<RotationStatus>? StatusChanged;

    Result Start(RotationPlan plan);

    Task StopAsync();
}
