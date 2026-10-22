using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Treasury.Domain.Accounts;

/// <summary>
/// Compte de trésorerie d'une agence : caisse, banque ou wallet. Son argent est suivi par son compte au grand livre,
/// ouvert dans la même transaction ; le solde n'est jamais stocké ici.
/// Règles validées : géré par le Superviseur seul ; nom unique dans l'agence (vérifié par l'Application) ;
/// devise = celle de l'agence ; affichage toujours en MRO à Nouakchott (le MRU ne sert qu'à la saisie) ;
/// désactivation à solde nul seulement, réactivation possible.
/// </summary>
public sealed class TreasuryAccount
{
    /// <summary>POINT OUVERT : longueur proposée, non fixée par le patron (le nom d'un tiers fait 30 au plus dans l'app actuelle).</summary>
    public const int NameMaxLength = 50;

    // Constructeur réservé à EF Core.
    private TreasuryAccount()
    {
        Name = null!;
    }

    private TreasuryAccount(
        Guid id, string name, TreasuryAccountType type, Guid agencyId, Currency currency, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Type = type;
        AgencyId = agencyId;
        Currency = currency;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public TreasuryAccountType Type { get; private set; }

    /// <summary>Agence du compte (id du module Organisation). Sert au périmètre : un agent ne voit que la sienne.</summary>
    public Guid AgencyId { get; private set; }

    /// <summary>Celle de l'agence, lue par l'Application dans Organisation : jamais saisie.</summary>
    public Currency Currency { get; private set; }

    /// <summary>Compte au grand livre qui porte le solde. Rattaché juste après la création, dans la même transaction.</summary>
    public Guid LedgerAccountId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<TreasuryAccount> Create(
        string? name,
        TreasuryAccountType type,
        Guid agencyId,
        Currency currency,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(type))
        {
            return TreasuryErrors.Account.TypeInvalid;
        }

        if (agencyId == Guid.Empty)
        {
            return TreasuryErrors.Account.AgencyRequired;
        }

        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        return new TreasuryAccount(IdGenerator.NewId(now), validName.Value, type, agencyId, currency, now);
    }

    /// <summary>
    /// Rattache le compte du grand livre ouvert pour ce compte de trésorerie.
    /// Appelé une seule fois, à la création : un second appel est un bug de programmation.
    /// </summary>
    public void AttachLedgerAccount(Guid ledgerAccountId)
    {
        if (ledgerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Id de compte du grand livre vide.", nameof(ledgerAccountId));
        }

        if (LedgerAccountId != Guid.Empty)
        {
            throw new InvalidOperationException("Ce compte de trésorerie a déjà son compte au grand livre.");
        }

        LedgerAccountId = ledgerAccountId;
    }

    /// <summary>Le nom change ; l'unicité dans l'agence est vérifiée par l'Application.</summary>
    public Result Rename(string? name)
    {
        var validName = ValidateName(name);
        if (validName.IsFailure)
        {
            return validName.Error;
        }

        Name = validName.Value;
        return Result.Success();
    }

    /// <param name="balance">Solde actuel lu au grand livre, dans la devise du compte.</param>
    public Result Deactivate(Money balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (balance.Currency != Currency)
        {
            throw new ArgumentException($"Solde en {balance.Currency} pour un compte en {Currency}.", nameof(balance));
        }

        if (!IsActive)
        {
            return TreasuryErrors.Account.AlreadyInactive;
        }

        if (!balance.IsZero)
        {
            return TreasuryErrors.Account.BalanceNotZero;
        }

        IsActive = false;
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (IsActive)
        {
            return TreasuryErrors.Account.AlreadyActive;
        }

        IsActive = true;
        return Result.Success();
    }

    private static Result<string> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return TreasuryErrors.Account.NameEmpty;
        }

        var trimmed = name.Trim();
        return trimmed.Length > NameMaxLength
            ? TreasuryErrors.Account.NameTooLong
            : trimmed;
    }
}
