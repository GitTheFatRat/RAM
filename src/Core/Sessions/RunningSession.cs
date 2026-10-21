namespace RbxManager.Core.Sessions;

using System;
using RbxManager.Core.Models;

public sealed record RunningSession(
    AccountId AccountId,
    LaunchRequest Request,
    int ProcessId,
    DateTimeOffset StartedAt);
