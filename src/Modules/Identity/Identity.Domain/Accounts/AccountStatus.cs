namespace Identity.Domain.Accounts;

/// <summary>
/// Le compte peut-il se connecter ? Le « mot de passe à changer » est une information séparée
/// (<see cref="UserAccount.MustChangePassword"/>) : un compte bloqué peut aussi avoir un mot de passe temporaire.
/// </summary>
public enum AccountStatus
{
    Active,
    Blocked,
}