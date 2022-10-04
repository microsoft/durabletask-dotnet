// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using DurableTask.Core;
using Grpc.Core;
using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Options;
using Microsoft.DurableTask.Shims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DurableTask.Grpc;

public partial class DurableTaskGrpcWorker
{
    /// <summary>
    /// Builder object for constructing customized <see cref="DurableTaskGrpcWorker"/> instances.
    /// </summary>
    public sealed class Builder
    {
        internal DefaultTaskBuilder taskProvider = new();
        internal ILoggerFactory? loggerFactory;
        internal DataConverter? dataConverter;
        internal IServiceProvider? services;
        internal IConfiguration? configuration;
        internal TimerOptions? timerOptions;
        internal string? hostname;
        internal int? port;
        internal Channel? channel;

        internal Builder()
        {
        }

        /// <summary>
        /// Initializes a new <see cref="DurableTaskGrpcWorker"/> object with the settings specified in the current
        /// builder object.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A new <see cref="DurableTaskGrpcWorker"/> object.</returns>
        public DurableTaskGrpcWorker Build() => new(this);

        /// <summary>
        /// Explicitly configures the gRPC endpoint to connect to, including the hostname and port.
        /// </summary>
        /// <remarks>
        /// If not specified, the worker creation process will try to resolve the endpoint from configuration (see
        /// the <see cref="UseConfiguration(IConfiguration)"/> method). Otherwise, 127.0.0.0:4001 will be used as the
        /// default gRPC endpoint address.
        /// </remarks>
        /// <param name="hostname">The hostname of the target gRPC endpoint. The default value is "127.0.0.1".</param>
        /// <param name="port">The port number of the target gRPC endpoint. The default value is 4001.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseAddress(string hostname, int? port = null)
        {
            this.hostname = hostname;
            this.port = port;
            return this;
        }

        /// <summary>
        /// Configures a gRPC <see cref="Channel"/> to use for communicating with the sidecar process.
        /// </summary>
        /// <remarks>
        /// This builder method allows you to provide your own gRPC channel for communicating with the Durable Task
        /// sidecar service. Channels provided using this method won't be disposed when the worker is disposed.
        /// Rather, the caller remains responsible for shutting down the channel after disposing the worker.
        /// </remarks>
        /// <param name="channel">The gRPC channel to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseGrpcChannel(Channel channel)
        {
            this.channel = channel;
            return this;
        }

        /// <summary>
        /// Configures a logger factory to be used by the worker.
        /// </summary>
        /// <remarks>
        /// Use this method to configure a logger factory explicitly. Otherwise, the client creation process will try
        /// to discover a logger factory from dependency-injected services (see the 
        /// <see cref="UseServices(IServiceProvider)"/> method).
        /// </remarks>
        /// <param name="loggerFactory">
        /// The logger factory to use or <c>null</c> to rely on default logging configuration.
        /// </param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseLoggerFactory(ILoggerFactory loggerFactory)
        {
            this.loggerFactory = loggerFactory;
            return this;
        }

        /// <summary>
        /// Configures a data converter to use when reading and writing orchestration data payloads.
        /// </summary>
        /// <remarks>
        /// The default behavior is to use the <see cref="JsonDataConverter"/>.
        /// </remarks>
        /// <param name="dataConverter">The data converter to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseDataConverter(DataConverter dataConverter)
        {
            this.dataConverter = dataConverter ?? throw new ArgumentNullException(nameof(dataConverter));
            return this;
        }

        /// <summary>
        /// Configures a dependency-injection service provider to use when constructing the client.
        /// </summary>
        /// <param name="services">
        /// The dependency-injection service provider to configure or <c>null</c> to disable service discovery.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseServices(IServiceProvider services)
        {
            this.services = services ?? throw new ArgumentNullException(nameof(services));
            return this;
        }

        /// <summary>
        /// Configures a configuration source to use when initializing the <see cref="DurableTaskGrpcWorker"/> instance.
        /// </summary>
        /// <param name="configuration">The configuration source to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseConfiguration(IConfiguration configuration)
        {
            this.configuration = configuration;
            return this;
        }

        /// <summary>
        /// Configures timer options for the created <see cref="DurableTaskGrpcWorker"/> to use.
        /// </summary>
        /// <param name="timerOptions">The timer options to use.</param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder UseTimerOptions(TimerOptions timerOptions)
        {
            this.timerOptions = timerOptions;
            return this;
        }

        /// <summary>
        /// Registers orchestrator and activity tasks using a callback delegate.
        /// </summary>
        /// <param name="taskProviderAction">
        /// A callback delegate that registers the orchestrator and activity tasks.
        /// </param>
        /// <returns>Returns the current builder object to enable fluent-like code syntax.</returns>
        public Builder AddTasks(Action<IDurableTaskRegistry> taskProviderAction)
        {
            taskProviderAction(this.taskProvider);
            return this;
        }

        internal sealed class DefaultTaskBuilder : IDurableTaskRegistry
        {
            internal ImmutableDictionary<TaskName, Func<IServiceProvider, ITaskActivity>>.Builder activitiesBuilder =
                ImmutableDictionary.CreateBuilder<TaskName, Func<IServiceProvider, ITaskActivity>>();

            internal ImmutableDictionary<TaskName, Func<ITaskOrchestrator>>.Builder orchestratorsBuilder =
                ImmutableDictionary.CreateBuilder<TaskName, Func<ITaskOrchestrator>>();

            public IDurableTaskRegistry AddOrchestrator(
                TaskName name,
                Func<TaskOrchestrationContext, Task> implementation)
            {
                return this.AddOrchestrator<object?, object?>(name, async (ctx, _) =>
                {
                    await implementation(ctx);
                    return null;
                });
            }

            public IDurableTaskRegistry AddOrchestrator<TOutput>(
                TaskName name,
                Func<TaskOrchestrationContext, Task<TOutput?>> implementation)
            {
                return this.AddOrchestrator<object?, TOutput>(name, (ctx, _) => implementation(ctx));
            }

            public IDurableTaskRegistry AddOrchestrator<TInput, TOutput>(
                TaskName name,
                Func<TaskOrchestrationContext, TInput?, Task<TOutput?>> implementation)
            {
                if (name == default)
                {
                    throw new ArgumentNullException(nameof(name));
                }

                if (implementation == null)
                {
                    throw new ArgumentNullException(nameof(implementation));
                }

                if (this.orchestratorsBuilder.ContainsKey(name))
                {
                    throw new ArgumentException($"A task orchestrator named '{name}' is already added.", nameof(name));
                }

                this.orchestratorsBuilder.Add(
                    name,
                    () => FuncTaskOrchestrator.Create(implementation));

                return this;
            }

            public IDurableTaskRegistry AddOrchestrator<TOrchestrator>() where TOrchestrator : ITaskOrchestrator
            {
                string name = GetTaskName(typeof(TOrchestrator));
                this.orchestratorsBuilder.Add(
                    name,
                    () =>
                    {
                        // Unlike activities, we don't give orchestrators access to the IServiceProvider collection since
                        // injected services are inherently non-deterministic. If an orchestrator needs access to a service,
                        // it should invoke that service through an activity call.
                        return Activator.CreateInstance<TOrchestrator>();
                    });
                return this;
            }

            public IDurableTaskRegistry AddActivity(TaskName name, Action<TaskActivityContext> implementation)
            {
                return this.AddActivity<object?, object?>(name, (context, _) =>
                {
                    implementation(context);
                    return null!;
                });
            }

            public IDurableTaskRegistry AddActivity(TaskName name, Func<TaskActivityContext, Task> implementation)
            {
                return this.AddActivity<object, object?>(name, (context, input) => implementation(context));
            }

            public IDurableTaskRegistry AddActivity<TInput, TOutput>(
                TaskName name,
                Func<TaskActivityContext, TInput?, TOutput?> implementation)
            {
                return this.AddActivity<TInput, TOutput?>(name, (context, input) => Task.FromResult(implementation(context, input)));
            }

            public IDurableTaskRegistry AddActivity<TInput, TOutput>(
                TaskName name,
                Func<TaskActivityContext, TInput?, Task<TOutput?>> implementation)
            {
                if (name == default)
                {
                    throw new ArgumentNullException(nameof(name));
                }

                if (implementation == null)
                {
                    throw new ArgumentNullException(nameof(implementation));
                }

                if (this.activitiesBuilder.ContainsKey(name))
                {
                    throw new ArgumentException($"A task activity named '{name}' is already added.", nameof(name));
                }

                this.activitiesBuilder.Add(
                    name,
                    _ => FuncTaskActivity.Create(implementation));
                return this;
            }

            public IDurableTaskRegistry AddActivity<TActivity>() where TActivity : ITaskActivity
            {
                string name = GetTaskName(typeof(TActivity));
                this.activitiesBuilder.Add(
                    name,
                    sp =>
                    {
                        return ActivatorUtilities.GetServiceOrCreateInstance<TActivity>(sp);
                    });
                return this;
            }

            static TaskName GetTaskName(Type taskDeclarationType)
            {
                // IMPORTANT: This logic needs to be kept consistent with the source generator logic
                DurableTaskAttribute? attribute = (DurableTaskAttribute?)Attribute.GetCustomAttribute(taskDeclarationType, typeof(DurableTaskAttribute));
                if (attribute != null)
                {
                    return attribute.Name;
                }
                else
                {
                    return taskDeclarationType.Name;
                }
            }
        }
    }
}
