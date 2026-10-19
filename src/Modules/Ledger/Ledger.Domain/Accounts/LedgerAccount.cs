using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;

namespace Ledger.Domain.Accounts;

/// <summary>
/// Compte du grand livre : une caisse, un tiers (un compte par tiers et par agence) ou un compte système.
/// Il appartient toujours à une agence, et n'a qu'une devise : celle de l'agence.
/// Son solde n'est jamais stocké : il se calcule à partir des lignes d'écriture (débit − crédit).
/// </summary>
public sealed class LedgerAccount
{
    public const int NameMaxLength = 100;

    // Constructeur réservé à EF Core.
    private LedgerAccount()
    {
        Name = null!;
    }

    private LedgerAccount(
        Guid id,
        LedgerAccountKind kind,
        SystemAccountRole? systemRole,
        Guid? ownerId,
        string name,
        Currency currency,
        Guid agencyId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Kind = kind;
        SystemRole = systemRole;
        OwnerId = ownerId;
        Name = name;
        Currency = currency;
        AgencyId = agencyId;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public LedgerAccountKind Kind { get; private set; }

    /// <summary>Rôle d'un compte système ; null pour une caisse ou un tiers.</summary>
    public SystemAccountRole? SystemRole { get; private set; }

    /// <summary>Id de la caisse ou du tiers dans son module ; null pour un compte système.</summary>
    public Guid? OwnerId { get; private set; }

    public string Name { get; private set; }

    public Currency Currency { get; private set; }

    /// <summary>Agence du compte (id du module Organisation). Sert au périmètre et au budget de l'agence.</summary>
    public Guid AgencyId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Compte d'une caisse (Trésorerie) ou d'un tiers (Tiers).</summary>
    public static Result<LedgerAccount> Open(
        LedgerAccountKind kind,
        Guid ownerId,
        string? name,
        Currency currency,
        Guid agencyId,
        DateTimeOffset now)
    {
        if (kind is not (LedgerAccountKind.Treasury or LedgerAccountKind.Party))
        {
            return LedgerErrors.Account.KindInvalid;
        }

        if (ownerId == Guid.Empty)
        {
            return LedgerErrors.Account.OwnerRequired;
        }

        if (agencyId == Guid.Empty)
        {
            return LedgerErrors.Account.AgencyRequired;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return LedgerErrors.Account.NameEmpty;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return LedgerErrors.Account.NameTooLong;
        }

        return new LedgerAccount(IdGenerator.NewId(now), kind, null, ownerId, trimmedName, currency, agencyId, now);
    }

    /// <summary>Compte système d'une agence. Son nom découle de son rôle : il n'est jamais saisi.</summary>
    public static Result<LedgerAccount> CreateSystem(
        SystemAccountRole role,
        Guid agencyId,
        Currency currency,
        DateTimeOffset now)
    {
        if (agencyId == Guid.Empty)
        {
            return LedgerErrors.Account.AgencyRequired;
        }

        return new LedgerAccount(
            IdGenerator.NewId(now), LedgerAccountKind.System, role, null, NameOf(role), currency, agencyId, now);
    }

    /// <summary>Le compte ne reçoit plus d'écriture (ex. caisse désactivée). Son historique et son solde restent.</summary>
    public void Close() => IsActive = false;

    /// <summary>Vérifié pour chaque ligne d'une écriture : compte actif, et montant dans sa devise.</summary>
    public Result EnsureCanReceive(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (!IsActive)
        {
            return LedgerErrors.Account.Inactive;
        }

        return amount.Currency == Currency
            ? Result.Success()
            : LedgerErrors.Line.CurrencyMismatch;
    }

    private static string NameOf(SystemAccountRole role) => role switch
    {
        SystemAccountRole.ExchangePosition => "Position de change",
        SystemAccountRole.TransfersToDeliver => "Transferts à livrer",
        SystemAccountRole.OwnerContributions => "Apports du patron",
        SystemAccountRole.Adjustments => "Écarts et ajustements",
        SystemAccountRole.OpeningBalances => "Soldes d'ouverture",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Rôle de compte système inconnu."),
    };
}
