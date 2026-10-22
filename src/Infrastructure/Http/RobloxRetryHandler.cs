namespace RbxManager.Infrastructure.Http;

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

public sealed class RobloxRetryHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public RobloxRetryHandler(TimeSpan? baseDelay = null)
    {
        _pipeline = CreatePipeline(baseDelay ?? TimeSpan.FromSeconds(1));
    }

    public RobloxRetryHandler(HttpMessageHandler innerHandler, TimeSpan? baseDelay = null) : base(innerHandler)
    {
        _pipeline = CreatePipeline(baseDelay ?? TimeSpan.FromSeconds(1));
    }

    public RobloxRetryHandler(ResiliencePipeline<HttpResponseMessage> pipeline)
    {
        _pipeline = pipeline;
    }

    public RobloxRetryHandler(HttpMessageHandler innerHandler, ResiliencePipeline<HttpResponseMessage> pipeline) : base(innerHandler)
    {
        _pipeline = pipeline;
    }

    private static ResiliencePipeline<HttpResponseMessage> CreatePipeline(TimeSpan baseDelay)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = baseDelay > TimeSpan.Zero,
                Delay = baseDelay,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response =>
                    {
                        int statusCode = (int)response.StatusCode;
                        return statusCode == 429 || (statusCode >= 500 && statusCode <= 599);
                    }),
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose();
                    return ValueTask.CompletedTask;
                },
                DelayGenerator = args =>
                {
                    HttpResponseMessage? response = args.Outcome.Result;
                    if (response?.Headers.RetryAfter != null)
                    {
                        if (response.Headers.RetryAfter.Delta.HasValue)
                        {
                            return ValueTask.FromResult<TimeSpan?>(response.Headers.RetryAfter.Delta.Value);
                        }

                        if (response.Headers.RetryAfter.Date.HasValue)
                        {
                            TimeSpan diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                            if (diff > TimeSpan.Zero)
                            {
                                return ValueTask.FromResult<TimeSpan?>(diff);
                            }
                        }
                    }

                    return ValueTask.FromResult<TimeSpan?>(null);
                }
            })
            .Build();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async state => await base.SendAsync(request, state).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }
}
