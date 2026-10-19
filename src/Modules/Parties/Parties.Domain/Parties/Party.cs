using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Results;

namespace Parties.Domain.Parties;

/// <summary>
/// Tiers : client, fournisseur, personnel… Règles validées :
/// - commun aux deux agences (une seule fiche), avec un compte au grand livre par agence (<see cref="AgencyAccounts"/>) ;
/// - nom (30 caractères) et type obligatoires ; téléphone, NNI, adresse, note facultatifs ;
/// - téléphone et NNI uniques quand ils sont renseignés (vérifié par l'Application et par la base) ; nom non unique ;
/// - « dans le bilan » vrai par défaut ; jamais supprimé, seulement archivé (à solde nul dans les deux agences) ;
/// - le solde initial n'est jamais saisi à la création.
/// </summary>
public sealed class Party
{
    public const int NameMaxLength = 30;

    // POINT OUVERT : longueurs proposées, non fixées par le patron.
    public const int NniMaxLength = 20;
    public const int AddressMaxLength = 200;
    public const int NoteMaxLength = 500;

    private readonly List<PartyAgencyAccount> _agencyAccounts = [];

    // Constructeur réservé à EF Core.
    private Party()
    {
        Name = null!;
    }

    private Party(
        Guid id,
        string name,
        PartyType type,
        PartyPhone? phone,
        string? nni,
        string? address,
        string? note,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Type = type;
        Phone = phone;
        Nni = nni;
        Address = address;
        Note = note;
        CreatedAt = createdAt;
        InBalanceSheet = true;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public PartyType Type { get; private set; }

    /// <summary>Compte comptable, déduit du type (41, 40, 42 ; aucun pour « Autre »).</summary>
    public string? AccountCode => Type.AccountCode();

    public PartyPhone? Phone { get; private set; }

    /// <summary>
    /// Numéro d'identité, sans contrôle de format (Mauritanie et Sénégal diffèrent).
    /// POINT OUVERT : stocké en clair ; chiffrement au repos + empreinte en phase 6.
    /// </summary>
    public string? Nni { get; private set; }

    public string? Address { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Seuls les tiers « dans le bilan » comptent dans le budget de la caisse (règle validée).</summary>
    public bool InBalanceSheet { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<PartyAgencyAccount> AgencyAccounts => _agencyAccounts;

    /// <param name="phone">Déjà validé par l'Application ; null si non renseigné.</param>
    public static Result<Party> Create(
        string? name,
        PartyType type,
        PartyPhone? phone,
        string? nni,
        string? address,
        string? note,
        DateTimeOffset now)
    {
        var details = Validate(name, type, nni, address, note);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var d = details.Value;
        return new Party(IdGenerator.NewId(now), d.Name, type, phone, d.Nni, d.Address, d.Note, now);
    }

    /// <summary>
    /// Le Superviseur corrige la fiche (état complet : un téléphone, un NNI, une adresse ou une note absents sont retirés).
    /// Mêmes règles qu'à la création ; l'unicité du téléphone et du NNI est vérifiée par l'Application.
    /// Tout est validé avant toute affectation : un refus ne change rien.
    /// POINT OUVERT : un tiers archivé reste modifiable, faute de règle contraire.
    /// </summary>
    /// <param name="phone">Déjà validé par l'Application ; null = téléphone retiré.</param>
    public Result Update(
        string? name,
        PartyType type,
        PartyPhone? phone,
        string? nni,
        string? address,
        string? note,
        bool inBalanceSheet)
    {
        var details = Validate(name, type, nni, address, note);
        if (details.IsFailure)
        {
            return details.Error;
        }

        var d = details.Value;
        Name = d.Name;
        Type = type;
        Phone = phone;
        Nni = d.Nni;
        Address = d.Address;
        Note = d.Note;
        InBalanceSheet = inBalanceSheet;
        return Result.Success();
    }

    private static Result<ValidDetails> Validate(string? name, PartyType type, string? nni, string? address, string? note)
    {
        if (!Enum.IsDefined(type))
        {
            return PartiesErrors.Party.TypeInvalid;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return PartiesErrors.Party.NameEmpty;
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > NameMaxLength)
        {
            return PartiesErrors.Party.NameTooLong;
        }

        var cleanNni = Optional(nni);
        if (cleanNni?.Length > NniMaxLength)
        {
            return PartiesErrors.Party.NniTooLong;
        }

        var cleanAddress = Optional(address);
        if (cleanAddress?.Length > AddressMaxLength)
        {
            return PartiesErrors.Party.AddressTooLong;
        }

        var cleanNote = Optional(note);
        if (cleanNote?.Length > NoteMaxLength)
        {
            return PartiesErrors.Party.NoteTooLong;
        }

        return new ValidDetails(trimmedName, cleanNni, cleanAddress, cleanNote);
    }

    private sealed record ValidDetails(string Name, string? Nni, string? Address, string? Note);

    /// <summary>
    /// Rattache le compte au grand livre ouvert pour ce tiers dans une agence. Une seule fois par agence :
    /// un second appel est un bug de programmation.
    /// </summary>
    public void AttachAgencyAccount(Guid agencyId, Guid ledgerAccountId)
    {
        if (agencyId == Guid.Empty || ledgerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Agence ou compte du grand livre vide.");
        }

        if (_agencyAccounts.Exists(a => a.AgencyId == agencyId))
        {
            throw new InvalidOperationException("Ce tiers a déjà son compte dans cette agence.");
        }

        _agencyAccounts.Add(new PartyAgencyAccount(agencyId, ledgerAccountId));
    }

    /// <summary>
    /// Le Superviseur fixe le solde initial du tiers dans une agence, à tout moment (règle validée, comme l'ancienne app).
    /// Renvoie la différence avec le solde initial déjà passé : c'est elle seule que l'Application écrit au grand livre,
    /// en écriture d'ouverture contre « Soldes d'ouverture ». La devise est vérifiée par l'Application (Money).
    /// Refusé sur un tiers archivé : il a été archivé parce que son solde était nul dans les deux agences.
    /// </summary>
    public Result<decimal> ChangeOpeningBalance(Guid agencyId, decimal newOpeningBalance)
    {
        if (IsArchived)
        {
            return PartiesErrors.Party.Archived;
        }

        var account = _agencyAccounts.Find(a => a.AgencyId == agencyId);
        if (account is null)
        {
            return PartiesErrors.Party.AgencyAccountNotFound;
        }

        return account.ChangeOpeningBalance(newOpeningBalance);
    }

    /// <summary>
    /// Le Superviseur fixe le plafond de crédit du tiers dans une agence, dans la devise de l'agence (vérifiée par l'Application).
    /// Règles validées : positif ou nul ; 0 = aucun crédit autorisé ; null = pas de plafond. Bloquant en phase 3 (transferts).
    /// Aucune écriture : le plafond ne déplace pas d'argent.
    /// </summary>
    public Result SetCreditLimit(Guid agencyId, decimal? creditLimit)
    {
        if (creditLimit < 0m)
        {
            return PartiesErrors.Party.CreditLimitNegative;
        }

        var account = _agencyAccounts.Find(a => a.AgencyId == agencyId);
        if (account is null)
        {
            return PartiesErrors.Party.AgencyAccountNotFound;
        }

        account.SetCreditLimit(creditLimit);
        return Result.Success();
    }

    /// <summary>Texte facultatif : espaces retirés, vide = absent.</summary>
    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
