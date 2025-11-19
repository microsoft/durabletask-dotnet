using Xunit;

namespace ExportHistoryTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExportHistoryTestCollection : ICollectionFixture<ExportHistoryTestFixture>
{
    public const string Name = "ExportHistoryE2E";
}

