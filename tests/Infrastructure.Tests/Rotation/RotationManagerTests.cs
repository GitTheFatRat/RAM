namespace RbxManager.Infrastructure.Tests.Rotation;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Maintenance;
using RbxManager.Core.Models;
using RbxManager.Core.Rotation;
using RbxManager.Core.Sessions;
using RbxManager.Core.Storage;
using RbxManager.Infrastructure.Rotation;

public sealed class RotationManagerTests
{
    private static readonly TimeSpan GroupDuration = TimeSpan.FromHours(2);

    private readonly FakeTimeProvider _time = new();
    private readonly IAccountStore _store = Substitute.For<IAccountStore>();
    private readonly ILauncher _launcher = Substitute.For<ILauncher>();
    private readonly ISessionTracker _tracker = Substitute.For<ISessionTracker>();
    private readonly IRobloxCacheCleaner _cleaner = Substitute.For<IRobloxCacheCleaner>();
    private readonly ConcurrentQueue<string> _events = new();

    public RotationManagerTests()
    {
        SecretValue cookie = SecretValue.FromString("fake");
        List<Account> accounts =
        [
            new(new AccountId(1), "A1", "Group 1", null, cookie, DateTimeOffset.UtcNow),
            new(new AccountId(2), "A2", "group 1", null, cookie, DateTimeOffset.UtcNow),
            new(new AccountId(3), "B1", "Group 2", null, cookie, DateTimeOffset.UtcNow),
            new(new AccountId(4), "B2", "Group 2", null, cookie, DateTimeOffset.UtcNow, Status: AccountStatus.Expired),
            new(new AccountId(5), "C1", null, null, cookie, DateTimeOffset.UtcNow),
        ];
        _store.ListAsync(Arg.Any<CancellationToken>()).Returns(accounts);
        _launcher.LaunchAsync(Arg.Any<LaunchRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _tracker.TrackLaunchAsync(Arg.Any<AccountId>(), Arg.Any<LaunchRequest>(), Arg.Any<Func<Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                LaunchRequest request = call.Arg<LaunchRequest>();
                _events.Enqueue($"launch {request.AccountId.Value}@{request.PlaceId}");
                await call.Arg<Func<Task<Result>>>()();
                return (RunningSession?)null;
            });
        _tracker.CloseAllSessionsAsync(false).Returns(_ =>
        {
            _events.Enqueue("kill-all");
            return Task.CompletedTask;
        });
        _cleaner.CleanAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _events.Enqueue("clean");
            return new CacheCleanResult(0, 0, 0);
        });
    }

    private RotationManager CreateManager() => new(_store, _launcher, _tracker, _cleaner, _time);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for rotation state.");
    }

    [Fact]
    public async Task Cycle_LaunchesGroupWaitsKillsWaits10sCleansThenNextGroupAndWrapsAround()
    {
        using RotationManager manager = CreateManager();
        Assert.True(manager.Start(new RotationPlan(["Group 1", "Group 2"], GroupDuration, 1818)).IsSuccess);

        await WaitUntilAsync(() => manager.Status is { Phase: RotationPhase.Farming, CurrentGroup: "Group 1" });
        Assert.Equal(["launch 1@1818", "launch 2@1818"], _events.ToArray());
        Assert.Equal(_time.GetUtcNow() + GroupDuration, manager.Status.NextSwitchAt);

        _time.Advance(GroupDuration - TimeSpan.FromSeconds(1));
        await Task.Delay(50);
        Assert.DoesNotContain("kill-all", _events);

        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => manager.Status.Phase == RotationPhase.Stopping && _events.Contains("kill-all"));
        Assert.DoesNotContain("clean", _events);

        _time.Advance(RotationManager.ExitGracePeriod);
        await WaitUntilAsync(() => manager.Status is { Phase: RotationPhase.Farming, CurrentGroup: "Group 2" });

        // Group 2 skips the expired account; accounts without a group are never launched.
        Assert.Equal(["launch 1@1818", "launch 2@1818", "kill-all", "clean", "launch 3@1818"], _events.ToArray());

        _time.Advance(GroupDuration);
        await WaitUntilAsync(() => manager.Status.Phase == RotationPhase.Stopping && _events.Count(e => e == "kill-all") == 2);
        _time.Advance(RotationManager.ExitGracePeriod);
        await WaitUntilAsync(() => manager.Status is { Phase: RotationPhase.Farming, CurrentGroup: "Group 1" });
        Assert.Equal(["kill-all", "clean", "launch 1@1818", "launch 2@1818"], _events.Skip(5).ToArray());

        await manager.StopAsync();
        Assert.Equal(RotationStatus.Idle, manager.Status);
    }

    [Fact]
    public async Task Stop_DuringFarming_DoesNotKillSessions()
    {
        using RotationManager manager = CreateManager();
        manager.Start(new RotationPlan(["Group 1"], GroupDuration, 1818));
        await WaitUntilAsync(() => manager.Status.Phase == RotationPhase.Farming);

        await manager.StopAsync();

        Assert.False(manager.Status.IsRunning);
        await _tracker.DidNotReceive().CloseAllSessionsAsync(Arg.Any<bool>());
    }

    [Fact]
    public async Task Start_WhileRunning_IsRejected_AndCanRestartAfterStop()
    {
        using RotationManager manager = CreateManager();
        RotationPlan plan = new(["Group 1"], GroupDuration, 1818);
        manager.Start(plan);

        Assert.Equal("AlreadyRunning", manager.Start(plan).ErrorCode);

        await manager.StopAsync();
        Assert.True(manager.Start(plan).IsSuccess);
        await manager.StopAsync();
    }

    [Theory]
    [InlineData(0, 120, 1818, "NoGroups")]
    [InlineData(1, 0, 1818, "DurationTooShort")]
    [InlineData(1, 120, 0, "InvalidPlaceId")]
    public void Start_InvalidPlan_IsRejected(int groupCount, int minutes, long placeId, string expectedCode)
    {
        using RotationManager manager = CreateManager();
        string[] groups = Enumerable.Repeat("Group 1", groupCount).ToArray();

        Result result = manager.Start(new RotationPlan(groups, TimeSpan.FromMinutes(minutes), placeId));

        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.False(manager.Status.IsRunning);
    }
}
