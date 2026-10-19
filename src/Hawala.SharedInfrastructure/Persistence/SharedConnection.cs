using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Hawala.SharedInfrastructure.Persistence;

/// <summary>
/// Une seule connexion PostgreSQL par requête, partagée par les DbContext des modules d'argent
/// (Ledger, puis Treasury, Parties, Transfers, Cash). Décision validée (option A) : une commande = une transaction,
/// même quand elle écrit dans plusieurs schémas ; deux DbContext sur deux connexions ne le permettraient pas.
/// La connexion appartient à ce composant (scoped) : les DbContext l'utilisent sans la fermer définitivement.
/// La transaction elle-même sera ouverte par un décorateur commun, ajouté avec son premier utilisateur (bloc Trésorerie).
/// </summary>
public sealed class SharedConnection(NpgsqlDataSource dataSource) : IAsyncDisposable, IDisposable
{
    public NpgsqlConnection Connection { get; } = dataSource.CreateConnection();

    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    public void Dispose() => Connection.Dispose();
}

public static class SharedConnectionRegistration
{
    /// <summary>
    /// Enregistre la source de connexions (une pour toute l'app) et la connexion partagée (une par requête).
    /// Appelable par chaque module d'argent : seul le premier appel enregistre réellement.
    /// </summary>
    public static IServiceCollection AddSharedConnection(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.TryAddScoped<SharedConnection>();
        return services;
    }
}
