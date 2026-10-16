using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Results;
using Identity.Domain.Roles;

namespace Identity.Domain.Accounts;

/// <summary>
/// Compte d'un utilisateur de l'app : son état métier (actif / bloqué, rôle, mot de passe à changer).
/// Le mot de passe et le verrouillage après 5 échecs sont gérés par ASP.NET Identity en Infrastructure,
/// sur le même identifiant.
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

    private UserAccount(Guid id, Email email, PhoneNumber? phone, string fullName, RoleTemplate role, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        Phone = phone;
        FullName = fullName;
        Role = role;
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

    public AccountStatus Status { get; private set; }

    /// <summary>Vrai tant que l'agent n'a pas remplacé le mot de passe temporaire donné par le Superviseur.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// Augmente à chaque changement de sécurité (blocage, rôle, mot de passe).
    /// Le JWT porte ce numéro : s'il ne correspond plus, la requête est refusée tout de suite (option B).
    /// </summary>
    public int SecurityVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Création par un Superviseur, avec un mot de passe temporaire :
    /// le compte est actif mais doit changer son mot de passe à la première connexion.
    /// </summary>
    public static Result<UserAccount> CreateWithTemporaryPassword(
        Email email,
        PhoneNumber? phone,
        string? fullName,
        RoleTemplate role,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (string.IsNullOrWhiteSpace(fullName))
        {
            return IdentityErrors.Account.FullNameEmpty;
        }

        var trimmedName = fullName.Trim();
        if (trimmedName.Length > FullNameMaxLength)
        {
            return IdentityErrors.Account.FullNameTooLong;
        }

        return new UserAccount(IdGenerator.NewId(now), email, phone, trimmedName, role, now);
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

    /// <summary>Change le modèle de rôle. Rétrograder le dernier Superviseur actif est refusé.</summary>
    public Result ChangeRole(RoleTemplate newRole, int activeSupervisorCount)
    {
        if (newRole == Role)
        {
            return Result.Success();
        }

        if (Role == RoleTemplate.Supervisor
            && SupervisorGuard.WouldRemoveLastActiveSupervisor(this, activeSupervisorCount))
        {
            return IdentityErrors.Account.LastActiveSupervisor;
        }

        Role = newRole;
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
}