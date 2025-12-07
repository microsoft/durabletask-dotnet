// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;

namespace Microsoft.DurableTask.Tests;

/// <summary>
/// Tests for <see cref="TaskFailureDetails"/>.
/// </summary>
public class TaskFailureDetailsTests
{
    [Fact]
    public void FromException_CapturesOriginalException()
    {
        Exception originalException = new InvalidOperationException("Test error");
        TaskFailureDetails details = TaskFailureDetails.FromException(originalException);

        Assert.NotNull(details);
        Assert.Same(originalException, details.OriginalException);
        Assert.Equal(typeof(InvalidOperationException).FullName, details.ErrorType);
        Assert.Equal("Test error", details.ErrorMessage);
    }

    [Fact]
    public void FromException_CapturesOriginalExceptionWithInnerException()
    {
        Exception innerException = new ArgumentException("Inner error");
        Exception outerException = new InvalidOperationException("Outer error", innerException);
        TaskFailureDetails details = TaskFailureDetails.FromException(outerException);

        Assert.NotNull(details);
        Assert.Same(outerException, details.OriginalException);
        Assert.Equal(typeof(InvalidOperationException).FullName, details.ErrorType);
        Assert.Equal("Outer error", details.ErrorMessage);

        // Check inner failure details
        Assert.NotNull(details.InnerFailure);
        Assert.Same(innerException, details.InnerFailure.OriginalException);
        Assert.Equal(typeof(ArgumentException).FullName, details.InnerFailure.ErrorType);
        Assert.Equal("Inner error", details.InnerFailure.ErrorMessage);
    }

    [Fact]
    public void OriginalException_AllowsAccessToCustomExceptionProperties()
    {
        CustomException customException = new CustomException(statusCode: 404, message: "Not Found");
        TaskFailureDetails details = TaskFailureDetails.FromException(customException);

        Assert.NotNull(details.OriginalException);
        CustomException? retrievedException = details.OriginalException as CustomException;
        Assert.NotNull(retrievedException);
        Assert.Equal(404, retrievedException.StatusCode);
        Assert.Equal("Not Found", retrievedException.Message);
    }

    [Fact]
    public void OriginalException_AllowsUseWithTransientErrorDetector()
    {
        // Simulate a SQL transient error scenario
        SqlException sqlException = new SqlException(isTransient: true);
        TaskFailureDetails details = TaskFailureDetails.FromException(sqlException);

        Assert.NotNull(details.OriginalException);
        SqlException? retrievedException = details.OriginalException as SqlException;
        Assert.NotNull(retrievedException);
        Assert.True(retrievedException.IsTransient);
    }

    [Fact]
    public void OriginalException_IsNullForDeserializedFailureDetails()
    {
        // When creating TaskFailureDetails directly (simulating deserialization scenario)
        TaskFailureDetails details = new TaskFailureDetails(
            ErrorType: typeof(InvalidOperationException).FullName!,
            ErrorMessage: "Test error",
            StackTrace: null,
            InnerFailure: null,
            Properties: null);

        Assert.Null(details.OriginalException);
    }

    [Fact]
    public void IsCausedBy_WorksWithOriginalException()
    {
        Exception exception = new InvalidOperationException("Test error");
        TaskFailureDetails details = TaskFailureDetails.FromException(exception);

        Assert.True(details.IsCausedBy<InvalidOperationException>());
        Assert.True(details.IsCausedBy<Exception>());
        Assert.False(details.IsCausedBy<ArgumentException>());
    }

    /// <summary>
    /// Custom exception to test access to specific properties.
    /// </summary>
    private class CustomException : Exception
    {
        public CustomException(int statusCode, string message)
            : base(message)
        {
            this.StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }

    /// <summary>
    /// Mock SQL exception to test transient error scenarios.
    /// </summary>
    private class SqlException : Exception
    {
        public SqlException(bool isTransient)
            : base("SQL error")
        {
            this.IsTransient = isTransient;
        }

        public bool IsTransient { get; }
    }
}
