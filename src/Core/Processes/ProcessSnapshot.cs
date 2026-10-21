namespace RbxManager.Core.Processes;

using System;

public sealed record ProcessSnapshot(
    int Pid,
    DateTimeOffset StartTime,
    long WorkingSetBytes);
