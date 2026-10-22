using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Identity.Domain.Roles;

namespace Identity.Domain.Accounts;

/// <summary>
/// Compte d'un utilisateur de l'app : son état métier (actif / bloqué, rôle, agence, mot de passe à changer).
/// Le mot de passe et le verrouillage après 5 échecs sont gérés par ASP.NET Identity en Infrastructure,
/// sur le même identifiant.
/// Règle d'agence : un agent appartient à exactement une agence ; un Superviseur à aucune (il voit les deux).
/// </summary>
public sealed class UserAccount
{
    public const int FullNameMaxLength = 100;

    // Ajouts / retraits par rapport au modèle du rôle (phase 6). Rempli par EF Core au chargement.
    private readonly List<PermissionOverride> _permissionOverrides = [];

    // Constructeur réservé à EF Core (les value objects ne peuvent pas être liés par constructeur).
    private UserAccount()
    {
        Email = null!;
        FullName = null!;
    }

    private UserAccount(
        Guid id,
        Email email,
        PhoneNumber? phone,
        string fullName,
        RoleTemplate role,
        Guid? agencyId,
        DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        Phone = phone;
        FullName = fullName;
        Role = role;
        AgencyId = agencyId;
        CreatedAt = createdAt;
        Status = AccountStatus.Active;
        MustChangePassword = true;
        SecurityVersion = 1;
    }

    public Guid Id { get; private set; }

    public Email Email { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public string FullName { get; private set; }

    public RoleTemplate Role { get; private set; }

    /// <summary>
    /// Agence de l'agent (Nouakchott ou Dakar). Toujours null pour un Superviseur.
    /// Identifiant d'une agence du module Organisation : son existence est vérifiée par l'Application.
    /// </summary>
    public Guid? AgencyId { get; private set; }

    public AccountStatus Status { get; private set; }

    /// <summary>Vrai tant que l'agent n'a pas remplacé le mot de passe temporaire donné par le Superviseur.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// Augmente à chaque changement de sécurité (blocage, rôle, agence, mot de passe).
    /// Le JWT porte ce numéro : s'il ne correspond plus, la requête est refusée tout de suite (option B).
    /// </summary>
    public int SecurityVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Dernière connexion réussie (phase 6) ; null si le compte ne s'est jamais connecté.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Ajustements du compte par rapport au modèle de son rôle ; toujours vide pour un Superviseur.</summary>
    public IReadOnlyList<PermissionOverride> PermissionOverrides => _permissionOverrides;

    /// <summary>Plafond par transfert, dans la devise de l'agence ; null = pas de limite.</summary>
    public decimal? LimitPerTransfer { get; private set; }

    /// <summary>Plafond des transferts enregistrés dans la journée ; null = pas de limite.</summary>
    public decimal? LimitPerDay { get; private set; }

    /// <summary>Plafond des parts à crédit accordées dans la journée ; null = pas de limite.</summary>
    public decimal? CreditLimitPerDay { get; private set; }

    public AccountLimits Limits => new(LimitPerTransfer, LimitPerDay, CreditLimitPerDay);

    /// <summary>Permissions effectives : modèle du rôle + ajouts − retraits (tout pour un Superviseur).</summary>
    public IReadOnlySet<string> EffectivePermissions => AccountPermissions.Effective(Role, _permissionOverrides);

    /// <summary>
    /// Création par un Superviseur, avec un mot de passe temporaire :
    /// le compte est actif mais doit changer son mot de passe à la première connexion.
    /// </summary>
    /// <param name="agencyId">Obligatoire pour un agent, interdit (null) pour un Superviseur.</param>
    public static Result<UserAccount> CreateWithTemporaryPassword(
        Email email,
        PhoneNumber? phone,
        string? fullName,
        RoleTemplate role,
        Guid? agencyId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);

        var name = ValidateFullName(fullName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        var agencyCheck = ValidateAgency(role, agencyId);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        return new UserAccount(IdGenerator.NewId(now), email, phone, name.Value, role, agencyId, now);
    }

    /// <summary>
    /// Le Superviseur corrige le nom, l'email ou le téléphone (null = téléphone retiré).
    /// L'unicité de l'email et du téléphone est vérifiée par l'Application, comme à la création.
    /// Règle validée : les sessions ne sont pas coupées (SecurityVersion inchangée) ; ce ne sont pas
    /// des secrets, seul l'identifiant de la prochaine connexion change.
    /// </summary>
    public Result UpdateProfile(Email email, PhoneNumber? phone, string? fullName)
    {
        ArgumentNullException.ThrowIfNull(email);

        var name = ValidateFullName(fullName);
        if (name.IsFailure)
        {
            return name.Error;
        }

        Email = email;
        Phone = phone;
        FullName = name.Value;
        return Result.Success();
    }

    /// <summary>Bloque le compte : ses sessions doivent être révoquées par l'appelant.</summary>
    public Result Block(int activeSupervisorCount)
    {
        if (Status == AccountStatus.Blocked)
        {
            return IdentityErrors.Account.AlreadyBlocked;
        }

        if (SupervisorGuard.WouldRemoveLastActiveSupervisor(this, activeSupervisorCount))
        {
            return IdentityErrors.Account.LastActiveSupervisor;
        }

        Status = AccountStatus.Blocked;
        SecurityVersion++;
        return Result.Success();
    }

    public Result Unblock()
    {
        if (Status != AccountStatus.Blocked)
        {
            return IdentityErrors.Account.NotBlocked;
        }

        Status = AccountStatus.Active;
        SecurityVersion++;
        return Result.Success();
    }

    /// <summary>
    /// Change le modèle de rôle, et l'agence avec lui dans la même opération :
    /// promouvoir en Superviseur retire l'agence (passer null) ; rétrograder un Superviseur exige une agence.
    /// Un agent sans agence ne peut donc jamais exister. Rétrograder le dernier Superviseur actif est refusé.
    /// </summary>
    /// <param name="agencyId">Agence du compte avec son nouveau rôle : obligatoire pour un agent, null pour un Superviseur.</param>
    public Result ChangeRole(RoleTemplate newRole, Guid? agencyId, int activeSupervisorCount)
    {
        if (newRole == Role)
        {
            return Result.Success();
        }

        var agencyCheck = ValidateAgency(newRole, agencyId);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        if (Role == RoleTemplate.Supervisor
            && SupervisorGuard.WouldRemoveLastActiveSupervisor(this, activeSupervisorCount))
        {
            return IdentityErrors.Account.LastActiveSupervisor;
        }

        Role = newRole;
        AgencyId = agencyId;

        // Règle validée le 21 octobre 2026 : un nouveau rôle repart de son modèle, sans les anciens ajustements.
        _permissionOverrides.Clear();

        // Règle validée le 22 octobre 2026 : un nouveau rôle efface aussi les plafonds.
        ClearLimits();
        SecurityVersion++;
        return Result.Success();
    }

    /// <summary>
    /// Le Superviseur fixe la liste complète des permissions du compte (les cases cochées) ;
    /// on en déduit les ajouts et retraits par rapport au modèle du rôle.
    /// Refusé pour un Superviseur, qui garde toujours tout. Sans changement : rien ne bouge (sessions gardées).
    /// L'appelant vérifie que l'auteur est bien un Superviseur.
    /// </summary>
    /// <returns>Vrai si les permissions ont changé (sessions à jour à la prochaine requête).</returns>
    public Result<bool> SetPermissions(IEnumerable<string?> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (Role == RoleTemplate.Supervisor)
        {
            return IdentityErrors.Permissions.SupervisorHasAll;
        }

        var desired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var permission in permissions)
        {
            if (permission is null || !Permissions.All.Contains(permission))
            {
                return IdentityErrors.Permissions.Unknown;
            }

            desired.Add(permission);
        }

        var target = AccountPermissions.OverridesFor(Role, desired);
        if (target.Count == _permissionOverrides.Count && target.All(_permissionOverrides.Contains))
        {
            return false;
        }

        // On garde les ajustements inchangés (mêmes objets, suivis par EF) et on remplace seulement les autres.
        _permissionOverrides.RemoveAll(existing => !target.Contains(existing));
        _permissionOverrides.AddRange(target.Where(wanted => !_permissionOverrides.Contains(wanted)));
        SecurityVersion++;
        return true;
    }

    /// <summary>
    /// Le Superviseur déplace un agent vers une autre agence (ex. un employé qui part travailler à Dakar).
    /// L'agent est déconnecté tout de suite et revient avec son nouveau périmètre.
    /// Ses anciennes opérations restent attachées à l'agence où il les a faites.
    /// </summary>
    public Result ChangeAgency(Guid agencyId)
    {
        if (Role == RoleTemplate.Supervisor)
        {
            return IdentityErrors.Account.AgencyNotAllowedForSupervisor;
        }

        if (agencyId == Guid.Empty)
        {
            return IdentityErrors.Account.AgencyRequired;
        }

        if (agencyId == AgencyId)
        {
            return Result.Success();
        }

        AgencyId = agencyId;

        // Règle validée le 22 octobre 2026 : un plafond en MRO n'a pas de sens en FCFA ; le Superviseur les refixe.
        ClearLimits();
        SecurityVersion++;
        return Result.Success();
    }

    /// <summary>
    /// Le Superviseur fixe les plafonds du compte (état complet, null = pas de limite), dans la devise de son agence :
    /// positifs ou nuls, FCFA entier, MRO à 2 décimales au plus. Jamais de plafond pour un Superviseur.
    /// Les plafonds sont relus à chaque transfert : pas de SecurityVersion, les sessions continuent.
    /// L'appelant vérifie que l'auteur est un Superviseur.
    /// </summary>
    /// <param name="currency">Devise de l'agence du compte.</param>
    /// <returns>Vrai si un plafond a changé.</returns>
    public Result<bool> SetLimits(AccountLimits limits, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (Role == RoleTemplate.Supervisor)
        {
            return IdentityErrors.Limits.SupervisorHasNone;
        }

        foreach (var value in new[] { limits.PerTransfer, limits.PerDay, limits.CreditPerDay })
        {
            if (value is not { } amount)
            {
                continue;
            }

            if (amount < 0)
            {
                return IdentityErrors.Limits.Negative;
            }

            if (Money.Create(amount, currency).IsFailure)
            {
                return IdentityErrors.Limits.TooPrecise;
            }
        }

        if (limits == Limits)
        {
            return false;
        }

        LimitPerTransfer = limits.PerTransfer;
        LimitPerDay = limits.PerDay;
        CreditLimitPerDay = limits.CreditPerDay;
        return true;
    }

    /// <summary>Connexion réussie : seulement l'heure, sans toucher à la SecurityVersion (les autres sessions continuent).</summary>
    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    /// <summary>L'agent a choisi son propre mot de passe (première connexion ou changement volontaire).</summary>
    public void PasswordChanged()
    {
        MustChangePassword = false;
        SecurityVersion++;
    }

    /// <summary>Le Superviseur a donné un nouveau mot de passe temporaire (ex. mot de passe oublié).</summary>
    public void PasswordResetBySupervisor()
    {
        MustChangePassword = true;
        SecurityVersion++;
    }

    private void ClearLimits()
    {
        LimitPerTransfer = null;
        LimitPerDay = null;
        CreditLimitPerDay = null;
    }

    private static Result<string> ValidateFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return IdentityErrors.Account.FullNameEmpty;
        }

        var trimmed = fullName.Trim();
        return trimmed.Length > FullNameMaxLength
            ? IdentityErrors.Account.FullNameTooLong
            : trimmed;
    }

    private static Result ValidateAgency(RoleTemplate role, Guid? agencyId)
    {
        if (role == RoleTemplate.Supervisor)
        {
            return agencyId is null
                ? Result.Success()
                : IdentityErrors.Account.AgencyNotAllowedForSupervisor;
        }

        return agencyId is null || agencyId == Guid.Empty
            ? IdentityErrors.Account.AgencyRequired
            : Result.Success();
    }
}