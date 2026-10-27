// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Grpc.Core.Interceptors;

namespace Microsoft.DurableTask.Client.Grpc;

/// <summary>
/// Applies the configured timeout without replacing an earlier deadline or timing out long polls and streams.
/// </summary>
sealed class DefaultUnaryCallTimeoutInterceptor : Interceptor
{
    readonly TimeSpan timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultUnaryCallTimeoutInterceptor"/> class.
    /// </summary>
    /// <param name="timeout">The positive timeout applied to each unary call.</param>
    public DefaultUnaryCallTimeoutInterceptor(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout), timeout, "DefaultUnaryCallTimeout must be positive.");
        }

        this.timeout = timeout;
    }

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, this.WithDeadline(context));

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, this.WithDeadline(context));

    /// <summary>
    /// Identifies calls whose normal operation can wait indefinitely for an orchestration state change.
    /// </summary>
    /// <param name="methodFullName">The fully qualified gRPC method name.</param>
    /// <returns>Whether the call is a long poll.</returns>
    internal static bool IsLongPollingMethod(string methodFullName)
        => methodFullName is "/TaskHubSidecarService/WaitForInstanceCompletion"
            or "/TaskHubSidecarService/WaitForInstanceStart";

    ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (IsLongPollingMethod(context.Method.FullName))
        {
            return context;
        }

        DateTime now = DateTime.UtcNow;

        // Avoid overflow and DateTime.MaxValue, which gRPC treats as no deadline.
        long ticks = Math.Min(this.timeout.Ticks, DateTime.MaxValue.Ticks - now.Ticks - 1);
        DateTime deadline = now.AddTicks(ticks);
        if (context.Options.Deadline is DateTime existing && existing <= deadline)
        {
            return context;
        }

        return new(context.Method, context.Host, context.Options.WithDeadline(deadline));
    }
}
