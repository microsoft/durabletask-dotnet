// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// NOTE: Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/DiagnosticActivityExtensions.cs

using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Replica from System.Diagnostics.DiagnosticSource >= 6.0.0
/// </summary>
enum ActivityStatusCode
{
    Unset = 0,
    OK = 1,
    Error = 2,
}

/// <summary>
/// Extensions for <see cref="Activity"/>.
/// </summary>
static class DiagnosticActivityExtensions
{
    static readonly Action<Activity, string> s_setSpanId;
    static readonly Action<Activity, ActivityStatusCode, string> s_setStatus;

    static DiagnosticActivityExtensions()
    {
        BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        s_setSpanId = (typeof(Activity).GetField("_spanId", flags) ?? throw new InvalidOperationException("The field Activity._spanId was not found.")).CreateSetter<Activity, string>();
        s_setStatus = CreateSetStatus();
    }

    public static void SetSpanId(this Activity activity, string spanId)
        => s_setSpanId(activity, spanId);

    public static void SetStatus(this Activity activity, ActivityStatusCode status, string description)
        => s_setStatus(activity, status, description);

    static Action<Activity, ActivityStatusCode, string> CreateSetStatus()
    {
        MethodInfo method = typeof(Activity).GetMethod("SetStatus");
        if (method is null)
        {
            return (activity, status, description) => {
                if (activity is null)
                {
                    throw new ArgumentNullException(nameof(activity));
                }
                string str = status switch
                {
                    ActivityStatusCode.Unset => "UNSET",
                    ActivityStatusCode.OK => "OK",
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
