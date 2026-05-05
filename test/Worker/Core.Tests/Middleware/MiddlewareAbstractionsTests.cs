// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Worker.Middleware;

namespace Microsoft.DurableTask.Worker.Tests;

public class MiddlewareAbstractionsTests
{
    [Fact]
    public void FeatureCollection_Get_ReturnsFeatureSetByConcreteType()
    {
        // Arrange
        MiddlewareFeatureCollection features = new();
        TestFeature expected = new();

        // Act
        features.Set(expected);
        TestFeature? actual = features.Get<TestFeature>();

        // Assert
        actual.Should().BeSameAs(expected);
    }

    [Fact]
    public void FeatureCollection_SetNull_RemovesFeature()
    {
        // Arrange
        MiddlewareFeatureCollection features = new();
        TestFeature feature = new();
        OtherFeature otherFeature = new();
        features.Set(feature);
        features.Set(otherFeature);

        // Act
        features.Set<TestFeature>(null);

        // Assert
        features.Get<TestFeature>().Should().BeNull();
        features.Get<OtherFeature>().Should().BeSameAs(otherFeature);
    }

    [Fact]
    public void OrchestrationContext_GetInput_ReturnsTypedInputWhenCompatible()
    {
        // Arrange
        TestTaskOrchestrationMiddlewareContext context = new("hello");

        // Act
        string? input = context.GetInput<string>();

        // Assert
        input.Should().Be("hello");
    }

    [Fact]
    public void OrchestrationContext_GetInput_ReturnsNullWhenInputIsNull()
    {
        // Arrange
        TestTaskOrchestrationMiddlewareContext context = new(null);

        // Act
        string? input = context.GetInput<string>();

        // Assert
        input.Should().BeNull();
    }

    [Fact]
    public void OrchestrationContext_GetInput_ThrowsWhenInputIsIncompatible()
    {
        // Arrange
        TestTaskOrchestrationMiddlewareContext context = new("hello");

        // Act
        Action act = () => context.GetInput<int>();

        // Assert
        act.Should()
            .ThrowExactly<InvalidCastException>()
            .WithMessage("*System.String*System.Int32*");
    }

    [Fact]
    public void ActivityContext_GetInput_ReturnsTypedInputWhenCompatible()
    {
        // Arrange
        TestTaskActivityMiddlewareContext context = new(42);

        // Act
        int input = context.GetInput<int>();

        // Assert
        input.Should().Be(42);
    }

    [Fact]
    public void ActivityContext_GetInput_ReturnsNullWhenInputIsNull()
    {
        // Arrange
        TestTaskActivityMiddlewareContext context = new(null);

        // Act
        string? input = context.GetInput<string>();

        // Assert
        input.Should().BeNull();
    }

    [Fact]
    public void ActivityContext_SetResult_UpdatesResult()
    {
        // Arrange
        TestTaskActivityMiddlewareContext context = new(null);
        object expected = new();

        // Act
        context.SetResult(expected);

        // Assert
        context.Result.Should().BeSameAs(expected);
    }

    sealed class TestFeature
    {
    }

    sealed class OtherFeature
    {
    }

    sealed class TestTaskOrchestrationMiddlewareContext : TaskOrchestrationMiddlewareContext
    {
        readonly object? input;

        public TestTaskOrchestrationMiddlewareContext(object? input)
        {
            this.input = input;
        }

        public override TaskName Name => "TestOrchestrator";

        public override string InstanceId => "test-instance";

        public override string Version => string.Empty;

        public override ParentOrchestrationInstance? Parent => null;

        public override IReadOnlyDictionary<string, string>? Tags => null;

        public override bool IsReplaying => false;

        public override Type InputType => this.input?.GetType() ?? typeof(object);

        public override object? Input => this.input;

        public override string? RawInput => null;

        public override TaskOrchestrationContext OrchestrationContext { get; } =
            Mock.Of<TaskOrchestrationContext>();

        public override IMiddlewareFeatures Features { get; } = new MiddlewareFeatureCollection();

        public override CancellationToken CancellationToken => CancellationToken.None;

        public override object? Result { get; }
    }

    sealed class TestTaskActivityMiddlewareContext : TaskActivityMiddlewareContext
    {
        readonly object? input;
        object? result;

        public TestTaskActivityMiddlewareContext(object? input)
        {
            this.input = input;
        }

        public override TaskName Name => "TestActivity";

        public override string InstanceId => "test-instance";

        public override Type InputType => this.input?.GetType() ?? typeof(object);

        public override object? Input => this.input;

        public override string? RawInput => null;

        public override TaskActivityContext ActivityContext { get; } = Mock.Of<TaskActivityContext>();

        public override IMiddlewareFeatures Features { get; } = new MiddlewareFeatureCollection();

        public override IServiceProvider Services { get; } = Mock.Of<IServiceProvider>();

        public override CancellationToken CancellationToken => CancellationToken.None;

        public override object? Result => this.result;

        public override void SetResult(object? result)
        {
            this.result = result;
        }
    }
}
