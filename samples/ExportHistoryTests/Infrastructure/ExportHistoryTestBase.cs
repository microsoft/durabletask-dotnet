using ExportHistoryTests.Utilities;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.ExportHistory;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ExportHistoryTests.Infrastructure;

[Collection(ExportHistoryTestCollection.Name)]
public abstract class ExportHistoryTestBase
{
    protected ExportHistoryTestBase(ExportHistoryTestFixture fixture)
    {
        this.Fixture = fixture;
        this.ScenarioRunner = new ExportHistoryScenarioRunner(fixture);
    }

    protected ExportHistoryTestFixture Fixture { get; }

    protected ExportHistoryScenarioRunner ScenarioRunner { get; }

    protected ExportHistoryTestEnvironment Environment => this.Fixture.Environment;

    protected DurableTaskClient DurableTaskClient =>
        this.Fixture.DurableTaskClient ?? throw new InvalidOperationException("DurableTaskClient is not initialized.");

    protected ExportHistoryClient ExportHistoryClient =>
        this.Fixture.ExportHistoryClient ?? throw new InvalidOperationException("ExportHistoryClient is not initialized.");

    protected ILogger Logger =>
        this.Fixture.Logger ?? throw new InvalidOperationException("Logger is not initialized.");

    protected void SkipIfNotConfigured() => this.Fixture.SkipIfNotConfigured();
}

