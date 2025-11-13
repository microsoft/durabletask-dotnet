// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq.Expressions;
using System.Reflection;

// NOTE: Modified from https://github.com/Azure/durabletask/blob/main/src/DurableTask.Core/Tracing/FieldInfoExtensionMethods.cs
namespace Microsoft.DurableTask.Tracing;

/// <summary>
/// Extensions for <see cref="FieldInfo"/>.
/// </summary>
static class FieldInfoExtensionMethods
{
    /// <summary>
    /// Create a re-usable setter for a <see cref="FieldInfo"/>.
    /// When cached and reused, This is quicker than using <see cref="FieldInfo.SetValue(object, object)"/>.
    /// </summary>
    /// <typeparam name="TTarget">The target type of the object.</typeparam>
    /// <typeparam name="TValue">The value type of the field.</typeparam>
    /// <param name="fieldInfo">The field info.</param>
    /// <returns>A re-usable action to set the field.</returns>
    internal static Action<TTarget, TValue> CreateSetter<TTarget, TValue>(this FieldInfo fieldInfo)
    {
#pragma warning disable CA1510
        if (fieldInfo == null)
        {
            throw new ArgumentNullException(nameof(fieldInfo));
        }
#pragma warning restore CA1510

        if (fieldInfo.DeclaringType is null)
        {
            throw new ArgumentException("FieldInfo.DeclaringType cannot be null.", nameof(fieldInfo));
        }

        ParameterExpression targetExp = Expression.Parameter(typeof(TTarget), "target");
        Expression source = targetExp;

        if (typeof(TTarget) != fieldInfo.DeclaringType)
        {
            source = Expression.Convert(targetExp, fieldInfo.DeclaringType);
        }

        // Creating the setter to set the value to the field
        ParameterExpression valueExp = Expression.Parameter(typeof(TValue), "value");
        MemberExpression fieldExp = Expression.Field(source, fieldInfo);
        BinaryExpression assignExp = Expression.Assign(fieldExp, valueExp);
        return Expression.Lambda<Action<TTarget, TValue>>(assignExp, targetExp, valueExp).Compile();
    }
}
