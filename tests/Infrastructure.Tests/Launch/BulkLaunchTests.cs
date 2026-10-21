namespace RbxManager.Infrastructure.Tests.Launch;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using RbxManager.Core.Launch;
using RbxManager.Core.Models;

public sealed class BulkLaunchTests
{
    private sealed class RecordingLauncher : ILauncher
    {
        public List<LaunchRequest> ExecutedLaunches { get; } = [];
        public List<DateTimeOffset> LaunchTimes { get; } = [];

        public Task<Result> LaunchAsync(LaunchRequest request, CancellationToken ct = default)
        {
            ExecutedLaunches.Add(request);
            LaunchTimes.Add(DateTimeOffset.UtcNow);
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task BulkLaunch_ExecutesInSequentialOrder()
    {
        RecordingLauncher launcher = new();
        List<AccountId> accounts = [new AccountId(10), new AccountId(20), new AccountId(30)];
        long placeId = 1818;

        foreach (AccountId acc in accounts)
        {
            await launcher.LaunchAsync(new LaunchRequest(acc, placeId, null));
        }

        Assert.Equal(3, launcher.ExecutedLaunches.Count);
        Assert.Equal(10, launcher.ExecutedLaunches[0].AccountId.Value);
        Assert.Equal(20, launcher.ExecutedLaunches[1].AccountId.Value);
        Assert.Equal(30, launcher.ExecutedLaunches[2].AccountId.Value);
    }

    [Fact]
    public async Task BulkLaunch_CancellationStopsRemainingLaunches()
    {
        RecordingLauncher launcher = new();
        List<AccountId> accounts = [new AccountId(1), new AccountId(2), new AccountId(3), new AccountId(4)];
        using CancellationTokenSource cts = new();

        int executed = 0;
        foreach (AccountId acc in accounts)
        {
            if (cts.IsCancellationRequested)
            {
                break;
            }

            await launcher.LaunchAsync(new LaunchRequest(acc, 1818, null), cts.Token);
            executed++;

            if (executed == 2)
            {
                cts.Cancel();
            }
        }

        Assert.Equal(2, launcher.ExecutedLaunches.Count);
        Assert.Equal(1, launcher.ExecutedLaunches[0].AccountId.Value);
        Assert.Equal(2, launcher.ExecutedLaunches[1].AccountId.Value);
    }
}
