namespace RbxManager.Core.Tests.Sessions;

using System;
using RbxManager.Core.Sessions;

public sealed class RejoinPolicyTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    [Fact]
    public void Evaluate_IntentionalExit_NeverRejoins()
    {
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        RejoinPolicy policy = new(timeProvider);

        RejoinDecision decision = policy.Evaluate(isIntentionalExit: true);

        Assert.False(decision.ShouldRejoin);
        Assert.False(decision.GaveUp);
        Assert.Equal(TimeSpan.Zero, decision.Delay);
    }

    [Fact]
    public void Evaluate_UnexpectedExits_DoublesDelayExponentially()
    {
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        RejoinPolicy policy = new(timeProvider, initialDelay: TimeSpan.FromSeconds(10));

        // 1st exit
        RejoinDecision d1 = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d1.ShouldRejoin);
        Assert.Equal(TimeSpan.FromSeconds(10), d1.Delay);

        // 2nd exit
        timeProvider.Advance(TimeSpan.FromSeconds(15));
        RejoinDecision d2 = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d2.ShouldRejoin);
        Assert.Equal(TimeSpan.FromSeconds(20), d2.Delay);

        // 3rd exit
        timeProvider.Advance(TimeSpan.FromSeconds(25));
        RejoinDecision d3 = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d3.ShouldRejoin);
        Assert.Equal(TimeSpan.FromSeconds(40), d3.Delay);

        // 4th exit
        timeProvider.Advance(TimeSpan.FromSeconds(45));
        RejoinDecision d4 = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d4.ShouldRejoin);
        Assert.Equal(TimeSpan.FromSeconds(80), d4.Delay);

        // 5th exit
        timeProvider.Advance(TimeSpan.FromSeconds(85));
        RejoinDecision d5 = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d5.ShouldRejoin);
        Assert.Equal(TimeSpan.FromSeconds(160), d5.Delay);
    }

    [Fact]
    public void Evaluate_AfterMaxAttemptsReached_CircuitBreakerTripsAndGivesUp()
    {
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        RejoinPolicy policy = new(timeProvider, maxAttempts: 5);

        for (int i = 0; i < 5; i++)
        {
            RejoinDecision d = policy.Evaluate(isIntentionalExit: false);
            Assert.True(d.ShouldRejoin);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        // 6th attempt inside the 30 minute window
        RejoinDecision breakerDecision = policy.Evaluate(isIntentionalExit: false);
        Assert.False(breakerDecision.ShouldRejoin);
        Assert.True(breakerDecision.GaveUp);
    }

    [Fact]
    public void Evaluate_WindowExpiry_ResetsCounterAndAllowsNewAttempts()
    {
        ManualTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        RejoinPolicy policy = new(timeProvider, maxAttempts: 5, window: TimeSpan.FromMinutes(30));

        for (int i = 0; i < 5; i++)
        {
            policy.Evaluate(isIntentionalExit: false);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
        }

        // Advance beyond the 30 minute window (31 minutes)
        timeProvider.Advance(TimeSpan.FromMinutes(31));

        // Old attempts are expired from the rolling window
        RejoinDecision d = policy.Evaluate(isIntentionalExit: false);
        Assert.True(d.ShouldRejoin);
        Assert.False(d.GaveUp);
    }
}
