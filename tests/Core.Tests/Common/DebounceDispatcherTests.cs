namespace RbxManager.Core.Tests.Common;

using System;
using System.Threading;
using System.Threading.Tasks;
using RbxManager.Core.Common;
using Xunit;

public sealed class DebounceDispatcherTests
{
    [Fact]
    public async Task Debounce_ExecutesActionAfterDelay()
    {
        bool executed = false;
        using DebounceDispatcher debouncer = new(
            TimeSpan.FromMilliseconds(50),
            (delay, ct) => Task.Delay(delay, ct));

        debouncer.Debounce(() => executed = true);

        Assert.False(executed);
        await Task.Delay(100);
        Assert.True(executed);
    }

    [Fact]
    public async Task Debounce_RapidCalls_OnlyExecutesLastAction()
    {
        int executionCount = 0;
        int lastValue = 0;

        using DebounceDispatcher debouncer = new(
            TimeSpan.FromMilliseconds(60),
            (delay, ct) => Task.Delay(delay, ct));

        for (int i = 1; i <= 5; i++)
        {
            int val = i;
            debouncer.Debounce(() =>
            {
                Interlocked.Increment(ref executionCount);
                lastValue = val;
            });
            await Task.Delay(10);
        }

        // Wait for debounce period to finish
        await Task.Delay(150);

        Assert.Equal(1, executionCount);
        Assert.Equal(5, lastValue);
    }

    [Fact]
    public async Task Debounce_Dispose_CancelsPendingAction()
    {
        bool executed = false;
        DebounceDispatcher debouncer = new(
            TimeSpan.FromMilliseconds(100),
            (delay, ct) => Task.Delay(delay, ct));

        debouncer.Debounce(() => executed = true);
        debouncer.Dispose();

        await Task.Delay(150);
        Assert.False(executed);
    }
}
