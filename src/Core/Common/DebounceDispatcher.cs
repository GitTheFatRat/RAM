namespace RbxManager.Core.Common;

using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class DebounceDispatcher : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayFunc;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DebounceDispatcher(TimeSpan delay, Func<TimeSpan, CancellationToken, Task>? delayFunc = null)
    {
        _delay = delay;
        _delayFunc = delayFunc ?? Task.Delay;
    }

    public void Debounce(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _delayFunc(_delay, token);
                if (!token.IsCancellationRequested)
                {
                    action();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
