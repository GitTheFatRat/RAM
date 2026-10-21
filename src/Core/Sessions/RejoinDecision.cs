namespace RbxManager.Core.Sessions;

using System;

public sealed record RejoinDecision(
    bool ShouldRejoin,
    TimeSpan Delay,
    bool GaveUp,
    string Reason)
{
    public static RejoinDecision DoNotRejoin(string reason = "Intentional exit.") =>
        new(false, TimeSpan.Zero, false, reason);

    public static RejoinDecision Rejoin(TimeSpan delay, string reason = "Unexpected exit.") =>
        new(true, delay, false, reason);

    public static RejoinDecision BreakerTripped(string reason = "Maximum attempts exceeded in window.") =>
        new(false, TimeSpan.Zero, true, reason);
}
