// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DurableTask.Plugins;

/// <summary>
/// A simple plugin implementation that aggregates interceptors. This is the recommended
/// way to build plugins, following Temporal's SimplePlugin pattern.
/// </summary>
public sealed class SimplePlugin : IDurableTaskPlugin
{
    readonly List<IOrchestrationInterceptor> orchestrationInterceptors;
    readonly List<IActivityInterceptor> activityInterceptors;

    SimplePlugin(
        string name,
        List<IOrchestrationInterceptor> orchestrationInterceptors,
        List<IActivityInterceptor> activityInterceptors)
    {
        this.Name = name;
        this.orchestrationInterceptors = orchestrationInterceptors;
        this.activityInterceptors = activityInterceptors;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<IOrchestrationInterceptor> OrchestrationInterceptors => this.orchestrationInterceptors;

    /// <inheritdoc />
    public IReadOnlyList<IActivityInterceptor> ActivityInterceptors => this.activityInterceptors;

    /// <summary>
    /// Creates a new <see cref="Builder"/> for constructing a <see cref="SimplePlugin"/>.
    /// </summary>
    /// <param name="name">The unique name of the plugin.</param>
    /// <returns>A new builder instance.</returns>
    public static Builder NewBuilder(string name)
    {
        Check.NotNullOrEmpty(name);
        return new Builder(name);
    }

    /// <summary>
    /// Builder for constructing <see cref="SimplePlugin"/> instances.
    /// </summary>
    public sealed class Builder
    {
        readonly string name;
        readonly List<IOrchestrationInterceptor> orchestrationInterceptors = new();
        readonly List<IActivityInterceptor> activityInterceptors = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="Builder"/> class.
        /// </summary>
        /// <param name="name">The plugin name.</param>
        internal Builder(string name)
        {
            this.name = name;
        }

        /// <summary>
        /// Adds an orchestration interceptor to the plugin.
        /// </summary>
        /// <param name="interceptor">The interceptor to add.</param>
        /// <returns>This builder, for call chaining.</returns>
        public Builder AddOrchestrationInterceptor(IOrchestrationInterceptor interceptor)
        {
            Check.NotNull(interceptor);
            this.orchestrationInterceptors.Add(interceptor);
            return this;
        }

        /// <summary>
        /// Adds an activity interceptor to the plugin.
        /// </summary>
        /// <param name="interceptor">The interceptor to add.</param>
        /// <returns>This builder, for call chaining.</returns>
        public Builder AddActivityInterceptor(IActivityInterceptor interceptor)
        {
            Check.NotNull(interceptor);
            this.activityInterceptors.Add(interceptor);
            return this;
        }

        /// <summary>
        /// Builds the <see cref="SimplePlugin"/> instance.
        /// </summary>
        /// <returns>A new <see cref="SimplePlugin"/>.</returns>
        public SimplePlugin Build()
        {
            return new SimplePlugin(
                this.name,
                new List<IOrchestrationInterceptor>(this.orchestrationInterceptors),
                new List<IActivityInterceptor>(this.activityInterceptors));
        }
    }
}
