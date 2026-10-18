using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Results;
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
        SecurityVersion++;
        return Result.Success();
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
        SecurityVersion++;
        return Result.Success();
    }

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