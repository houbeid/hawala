using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure;

/// <summary>
/// Identifiants techniques d'un compte, gérés par ASP.NET Identity : hash du mot de passe,
/// compteur d'échecs, verrouillage, security stamp. Même Id que <see cref="Identity.Domain.Accounts.UserAccount"/>.
/// Les règles métier (statut, rôle, Superviseur…) ne sont jamais ici.
/// UserName contient l'Id du compte : la connexion par email / téléphone passe par UserAccount.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>
{
}