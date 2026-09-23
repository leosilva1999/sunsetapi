namespace Sunset.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<SunsetApiFactory>
{
    public const string Name = "Integration";
}
