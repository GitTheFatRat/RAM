namespace RbxManager.Core.Sessions;

using System;
using System.Collections.Generic;

public sealed class RejoinPolicy
{
    public static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(30);
    public const int DefaultMaxAttempts = 5;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _window;
    private readonly int _maxAttempts;
    private readonly List<DateTimeOffset> _attemptTimestamps = new();
    private TimeSpan _nextDelay;

    public int AttemptCount => _attemptTimestamps.Count;

    public RejoinPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? initialDelay = null,
        TimeSpan? window = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _initialDelay = initialDelay ?? DefaultInitialDelay;
        _window = window ?? DefaultWindow;
        _maxAttempts = maxAttempts;
        _nextDelay = _initialDelay;
    }

    public RejoinDecision Evaluate(bool isIntentionalExit)
    {
        if (isIntentionalExit)
        {
            return RejoinDecision.DoNotRejoin("Intentional close.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset windowStart = now - _window;

        _attemptTimestamps.RemoveAll(ts => ts < windowStart);

        if (_attemptTimestamps.Count >= _maxAttempts)
        {
            return RejoinDecision.BreakerTripped("Maximum rejoin attempts reached within rolling window.");
        }

        TimeSpan delay = _attemptTimestamps.Count == 0 ? _initialDelay : _nextDelay;
        _nextDelay = delay * 2;

        _attemptTimestamps.Add(now);

        return RejoinDecision.Rejoin(delay, $"Attempt {_attemptTimestamps.Count} of {_maxAttempts}.");
    }

    public void Reset()
    {
        _attemptTimestamps.Clear();
        _nextDelay = _initialDelay;
    }
}
