// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentAssertions;
using Microsoft.DurableTask.Plugins;
using Microsoft.DurableTask.Plugins.BuiltIn;
using Xunit;

namespace Microsoft.DurableTask.Extensions.Plugins.Tests;

public class ValidationPluginTests
{
    [Fact]
    public async Task ValidationPlugin_PassesValidInput()
    {
        // Arrange
        IInputValidator validator = new AlwaysValidValidator();
        ValidationPlugin plugin = new(validator);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, "valid-input");

        // Act & Assert
        await plugin.OrchestrationInterceptors[0].Invoking(
            i => i.OnOrchestrationStartingAsync(context))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidationPlugin_RejectsInvalidInput()
    {
        // Arrange
        IInputValidator validator = new AlwaysInvalidValidator();
        ValidationPlugin plugin = new(validator);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, "bad");

        // Act & Assert
        await plugin.OrchestrationInterceptors[0].Invoking(
            i => i.OnOrchestrationStartingAsync(context))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*validation failed*");
    }

    [Fact]
    public async Task ValidationPlugin_RejectsInvalidActivityInput()
    {
        // Arrange
        IInputValidator validator = new AlwaysInvalidValidator();
        ValidationPlugin plugin = new(validator);
        ActivityInterceptorContext context = new("TestActivity", "instance1", "bad");

        // Act & Assert
        await plugin.ActivityInterceptors[0].Invoking(
            i => i.OnActivityStartingAsync(context))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*validation failed*");
    }

    [Fact]
    public async Task ValidationPlugin_RunsMultipleValidators()
    {
        // Arrange
        IInputValidator validator1 = new AlwaysValidValidator();
        IInputValidator validator2 = new AlwaysInvalidValidator();
        ValidationPlugin plugin = new(validator1, validator2);
        OrchestrationInterceptorContext context = new("TestOrch", "instance1", false, "input");

        // Act & Assert - second validator should cause failure
        await plugin.OrchestrationInterceptors[0].Invoking(
            i => i.OnOrchestrationStartingAsync(context))
            .Should().ThrowAsync<ArgumentException>();
    }

    sealed class AlwaysValidValidator : IInputValidator
    {
        public Task<ValidationResult> ValidateAsync(TaskName taskName, object? input) =>
            Task.FromResult(ValidationResult.Success);
    }

    sealed class AlwaysInvalidValidator : IInputValidator
    {
        public Task<ValidationResult> ValidateAsync(TaskName taskName, object? input) =>
            Task.FromResult(ValidationResult.Failure("Input is invalid"));
    }
}
