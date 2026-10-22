namespace RbxManager.App.Tests.Helpers;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public static class StaRunner
{
    private static readonly BlockingCollection<(Action Action, TaskCompletionSource Tcs)> WorkQueue = new();
    private static readonly Thread StaThread;

    static StaRunner()
    {
        StaThread = new Thread(() =>
        {
            foreach ((Action action, TaskCompletionSource tcs) in WorkQueue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }
        })
        {
            IsBackground = true
        };

        StaThread.SetApartmentState(ApartmentState.STA);
        StaThread.Start();
    }

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkQueue.Add((action, tcs));
        tcs.Task.GetAwaiter().GetResult();
    }
}
