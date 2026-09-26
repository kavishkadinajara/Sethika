namespace Sethika.Api.IntegrationTests.Persistence;

/// <summary>
/// Every test class that touches the Sethika_Test database belongs to this one collection.
/// </summary>
/// <remarks>
/// Two things matter here and both were learned the hard way.
///
/// ICollectionFixture, not IClassFixture: IClassFixture builds a separate fixture per class,
/// so each class captured its own institution ids - and the ids are IDENTITY values that
/// change on every reseed. One class reseeding silently invalidated another class's captured
/// ids, and the symptom was a tenant-isolation test finding no rows.
///
/// DisableParallelization: xunit runs distinct collections in parallel, so three classes were
/// reseeding the same database concurrently. Sharing one collection serializes them.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SethikaDatabaseCollection : ICollectionFixture<SethikaTestDatabase>
{
    public const string Name = "sethika-database";
}
