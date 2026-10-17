using Testcontainers.PostgreSql;

namespace Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Un seul PostgreSQL (Docker) et une seule API pour tous les tests de la collection.
/// Chaque test crée ses propres comptes (emails uniques), donc aucun nettoyage n'est nécessaire.
/// Démarrage paresseux, compatible xUnit v2 et v3.
/// </summary>
public sealed class ApiFixture : IDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    private readonly Lazy<Task<HawalaApiFactory>> _factory;

    public ApiFixture() => _factory = new Lazy<Task<HawalaApiFactory>>(StartAsync);

    public Task<HawalaApiFactory> GetFactoryAsync() => _factory.Value;

    public void Dispose()
    {
        if (_factory.IsValueCreated && _factory.Value.IsCompletedSuccessfully)
        {
            _factory.Value.Result.Dispose();
        }

        _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task<HawalaApiFactory> StartAsync()
    {
        await _postgres.StartAsync();

        var factory = new HawalaApiFactory(_postgres.GetConnectionString());

        // Force le démarrage : migrations + seed du Superviseur sur la base vide.
        _ = factory.Server;

        return factory;
    }
}