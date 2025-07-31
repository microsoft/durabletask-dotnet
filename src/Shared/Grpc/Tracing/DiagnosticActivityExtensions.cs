// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

// NOTE: Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/DiagnosticActivityExtensions.cs
namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Replica from System.Diagnostics.DiagnosticSource >= 6.0.0.
/// </summary>
enum ActivityStatusCode
{
    /// <summary>
    /// The default value indicating the status code is not initialized.
    /// </summary>
    Unset = 0,

    /// <summary>
    /// Indicates the operation has been validated and completed successfully.
    /// </summary>
    Ok = 1,

    /// <summary>
    /// Indicates an error was encountered during the operation.
    /// </summary>
    Error = 2,
}

/// <summary>
/// Extensions for <see cref="Activity"/>.
/// </summary>
static class DiagnosticActivityExtensions
{
    static readonly Action<Activity, string> SetSpanIdMethod;
    static readonly Action<Activity, ActivityStatusCode, string> SetStatusMethod;

    static DiagnosticActivityExtensions()
    {
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        SetSpanIdMethod = (typeof(Activity).GetField("_spanId", flags)
                          ?? throw new InvalidOperationException("The field Activity._spanId was not found."))
                            .CreateSetter<Activity, string>();
        SetStatusMethod = CreateSetStatus();
    }

    /// <summary>
    /// Explicitly sets the span ID for the given activity.
    /// </summary>
    /// <param name="activity">The activity on which to set the span ID.</param>
    /// <param name="spanId">The span ID to set.</param>
    public static void SetSpanId(this Activity activity, string spanId)
        => SetSpanIdMethod(activity, spanId);

    /// <summary>
    /// Explicitly sets the status code and description for the given activity.
    /// </summary>
    /// <param name="activity">The activity on which to set the span ID.</param>
    /// <param name="status">The status to set.</param>
    /// <param name="description">The description to set.</param>
    public static void SetStatus(this Activity activity, ActivityStatusCode status, string description)
        => SetStatusMethod(activity, status, description);

    static Action<Activity, ActivityStatusCode, string> CreateSetStatus()
    {
        MethodInfo? method = typeof(Activity).GetMethod("SetStatus");

        if (method is null)
        {
            return (activity, status, description) =>
            {
#pragma warning disable CA1510
                if (activity is null)
                {
                    throw new ArgumentNullException(nameof(activity));
                }
#pragma warning restore CA1510

                string? str = status switch
                {
                    ActivityStatusCode.Unset => "UNSET",
                    ActivityStatusCode.Ok => "OK",
                    ActivityStatusCode.Error => "ERROR",
                    _ => null,
                };

                activity.SetTag("otel.status_code", str);
                activity.SetTag("otel.status_description", description);
            };
        }

        /*
            building expression tree to effectively perform:
            (activity, status, description) => activity.SetStatus((ActivityStatusCode)(int)status, description);
        */

        ParameterExpression targetExp = Expression.Parameter(typeof(Activity), "target");
        ParameterExpression status = Expression.Parameter(typeof(ActivityStatusCode), "status");
        ParameterExpression description = Expression.Parameter(typeof(string), "description");
        UnaryExpression convert = Expression.Convert(status, typeof(int));
        convert = Expression.Convert(convert, method.GetParameters().First().ParameterType);
        MethodCallExpression callExp = Expression.Call(targetExp, method, convert, description);
        return Expression.Lambda<Action<Activity, ActivityStatusCode, string>>(callExp, targetExp, status, description)
            .Compile();
    }
}
