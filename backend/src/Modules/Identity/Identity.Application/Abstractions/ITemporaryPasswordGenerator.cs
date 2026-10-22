namespace Identity.Application.Abstractions;

/// <summary>
/// Génère le mot de passe temporaire d'un nouveau compte. Il est montré une seule fois au Superviseur,
/// qui le transmet à l'agent ; l'agent doit le changer à sa première connexion.
/// Il respecte toujours la politique de mot de passe (8 caractères minimum, dont un chiffre).
/// </summary>
public interface ITemporaryPasswordGenerator
{
    string Generate();
}
