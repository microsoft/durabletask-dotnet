// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.DurableTask.Converters;
using Microsoft.DurableTask.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.DurableTask.Worker.Tests;

public class DefaultDurableTaskWorkerBuilderTests
{
    [Fact]
    public void BuildTarget_InvalidType_Throws()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(BadBuildTarget);
        act.Should().ThrowExactly<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void BuildTarget_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.BuildTarget = typeof(GoodBuildTarget);
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));

        builder.BuildTarget = null;
        builder.BuildTarget.Should().BeNull();
    }

    [Fact]
    public void UseBuildTargetT_ValidType_Sets()
    {
        DefaultDurableTaskWorkerBuilder builder = new("test", new ServiceCollection());
        Action act = () => builder.UseBuildTarget<GoodBuildTarget>();
        act.Should().NotThrow();
        builder.BuildTarget.Should().Be(typeof(GoodBuildTarget));
    }

    [Fact]
    public void UseBuildTargetT_ValidTypeWithOptions_Sets()
    {
        JsonDataConverter converter = new();
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        builder.Configure(opt => opt.DataConverter = converter);
        builder.UseBuildTarget<GoodBuildTarget, GoodBuildTargetOptions>();
        IHostedService client = builder.Build(services.BuildServiceProvider());

        GoodBuildTarget target = client.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
    }

    [Fact]
    public void Build_NoTarget_Throws()
    {
        ServiceCollection services = new();
        DefaultDurableTaskWorkerBuilder builder = new("test", services);
        Action act = () => builder.Build(services.BuildServiceProvider());
        act.Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Build_Target_Built()
    {
        CustomDataConverter converter = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.Configure<DurableTaskWorkerOptions>("test", x => x.DataConverter = converter);
        DefaultDurableTaskWorkerBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };

        IHostedService service = builder.Build(services.BuildServiceProvider());
        GoodBuildTarget target = service.Should().BeOfType<GoodBuildTarget>().Subject;
        target.Name.Should().Be("test");
        target.Factory.Should().NotBeNull();
        target.Options.Should().NotBeNull();
        target.Options.DataConverter.Should().BeSameAs(converter);
    }

    [Fact]
    public void Build_WithOrchestratorUnversionedFallback_LogsOrchestratorWarning()
    {
        // Arrange
        CapturingLoggerFactory loggerFactory = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        DefaultDurableTaskWorkerBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };
        builder.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
        {
            OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
        });

        // Act
        builder.Build(services.BuildServiceProvider());

        // Assert
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Orchestrator unversioned fallback", StringComparison.OrdinalIgnoreCase)
            && log.Message.Contains("replay", StringComparison.OrdinalIgnoreCase)
            && log.Message.Contains("non-determinism", StringComparison.OrdinalIgnoreCase)
            && log.Message.Contains("deserialization", StringComparison.OrdinalIgnoreCase));
        loggerFactory.Logs.Should().NotContain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Activity unversioned fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WithActivityUnversionedFallback_LogsActivityWarning()
    {
        // Arrange
        CapturingLoggerFactory loggerFactory = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        DefaultDurableTaskWorkerBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };
        builder.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
        {
            ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
        });

        // Act
        builder.Build(services.BuildServiceProvider());

        // Assert
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Activity unversioned fallback", StringComparison.OrdinalIgnoreCase)
            && log.Message.Contains("input shapes", StringComparison.OrdinalIgnoreCase));
        loggerFactory.Logs.Should().NotContain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Orchestrator unversioned fallback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WithBothFallbacksEnabled_LogsBothWarnings()
    {
        // Arrange
        CapturingLoggerFactory loggerFactory = new();
        ServiceCollection services = new();
        services.AddOptions();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        DefaultDurableTaskWorkerBuilder builder = new("test", services)
        {
            BuildTarget = typeof(GoodBuildTarget),
        };
        builder.UseVersioning(new DurableTaskWorkerOptions.VersioningOptions
        {
            OrchestratorUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
            ActivityUnversionedFallback = DurableTaskWorkerOptions.UnversionedFallbackMode.CatchAll,
        });

        // Act
        builder.Build(services.BuildServiceProvider());

        // Assert
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Orchestrator unversioned fallback", StringComparison.OrdinalIgnoreCase));
        loggerFactory.Logs.Should().Contain(log =>
            log.Level == LogLevel.Warning
            && log.Message.Contains("Activity unversioned fallback", StringComparison.OrdinalIgnoreCase));
    }

    class BadBuildTarget : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }

    class GoodBuildTarget : DurableTaskWorker
    {
        public GoodBuildTarget(
            string name, DurableTaskFactory factory, IOptionsMonitor<DurableTaskWorkerOptions> options)
            : base(name, factory)
        {
            this.Options = options.Get(name);
        }

        public new string Name => base.Name;

        public new IDurableTaskFactory Factory => base.Factory;

        public DurableTaskWorkerOptions Options { get; }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }

    class CustomDataConverter : DataConverter
    {
        public override object? Deserialize(string? data, Type targetType)
        {
            throw new NotImplementedException();
        }

        public override string? Serialize(object? value)
        {
            throw new NotImplementedException();
        }
    }

    class GoodBuildTargetOptions : DurableTaskWorkerOptions
    {
    }
}
