using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Ledger;

/// <summary>
/// Décorateur de transaction commun (option A) : une commande = une transaction, même avec plusieurs
/// SaveChanges. Éprouvé ici avec le grand livre, seul module d'argent existant ; le test sur deux schémas
/// (caisse + compte du grand livre) viendra avec la Trésorerie.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SharedTransactionTests(ApiFixture fixture)
{
    private static readonly Guid Author = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    [Fact]
    public async Task Successful_command_keeps_everything_it_wrote()
    {
        var factory = await fixture.GetFactoryAsync();
        var command = new OpenAndPostCommand(Guid.NewGuid(), Fail: false, Throw: false);

        var result = await RunAsync(factory, command);

        Assert.True(result.IsSuccess);
        Assert.True(await WasCommittedAsync(factory, command.AgencyId));
    }

    [Fact]
    public async Task Failed_command_writes_nothing_even_after_several_save_changes()
    {
        var factory = await fixture.GetFactoryAsync();
        var command = new OpenAndPostCommand(Guid.NewGuid(), Fail: true, Throw: false);

        var result = await RunAsync(factory, command);

        Assert.True(result.IsFailure);
        Assert.False(await WasCommittedAsync(factory, command.AgencyId));
    }

    [Fact]
    public async Task Exception_writes_nothing()
    {
        var factory = await fixture.GetFactoryAsync();
        var command = new OpenAndPostCommand(Guid.NewGuid(), Fail: false, Throw: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(factory, command));

        Assert.False(await WasCommittedAsync(factory, command.AgencyId));
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<Result<Guid>> RunAsync(HawalaApiFactory factory, OpenAndPostCommand command)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var handler = new SharedTransactionCommandHandler<OpenAndPostCommand, Guid>(
            new OpenAndPostHandler(services.GetRequiredService<ILedger>()),
            services.GetRequiredService<SharedConnection>(),
            services.GetServices<ISharedTransactionParticipant>());

        return await handler.Handle(command, CancellationToken.None);
    }

    /// <summary>Vu depuis une autre requête : l'écriture de 250 MRO existe-t-elle ? Seule une transaction validée est visible.</summary>
    private static async Task<bool> WasCommittedAsync(HawalaApiFactory factory, Guid agencyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();

        // Le compte système est créé à la demande : s'il existe déjà, c'est que la commande a été validée.
        var probe = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.Adjustments, agencyId, Currency.MRO, CancellationToken.None);
        var balance = await ledger.GetBalancesAsync([probe.Value], asOf: null, CancellationToken.None);

        // Le compte système vient d'être recréé si la commande a été annulée : son solde est alors nul.
        return balance.Value.Single().Balance.Amount == -250m;
    }

    /// <summary>Commande de test : ouvre une caisse, crée un compte système, passe une écriture (trois SaveChanges).</summary>
    private sealed record OpenAndPostCommand(Guid AgencyId, bool Fail, bool Throw) : ICommand<Guid>;

    private sealed class OpenAndPostHandler(ILedger ledger) : ICommandHandler<OpenAndPostCommand, Guid>
    {
        public async Task<Result<Guid>> Handle(OpenAndPostCommand command, CancellationToken cancellationToken)
        {
            var cash = await ledger.OpenAccountAsync(
                LedgerAccountKind.Treasury, Guid.NewGuid(), "Caisse de test", Currency.MRO, command.AgencyId, cancellationToken);
            var adjustments = await ledger.GetOrCreateSystemAccountAsync(
                SystemAccountRole.Adjustments, command.AgencyId, Currency.MRO, cancellationToken);
            var amount = Money.Create(250m, Currency.MRO).Value;

            var entry = await ledger.PostAsync(
                new PostEntryRequest(
                    new DateOnly(2026, 10, 19),
                    "Tests.SharedTransaction",
                    "Tests",
                    null,
                    Author,
                    "Écriture dans la transaction commune",
                    [
                        new PostEntryLine(cash.Value, EntrySide.Debit, amount),
                        new PostEntryLine(adjustments.Value, EntrySide.Credit, amount),
                    ]),
                cancellationToken);

            if (command.Throw)
            {
                throw new InvalidOperationException("Panne simulée après les écritures.");
            }

            return command.Fail
                ? Error.Failure("Tests.Simulated", "Échec simulé après les écritures.")
                : entry;
        }
    }
}
