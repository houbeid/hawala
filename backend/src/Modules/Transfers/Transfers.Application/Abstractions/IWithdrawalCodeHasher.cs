namespace Transfers.Application.Abstractions;

/// <summary>
/// Hache le code de retrait (règle validée : stocké haché, jamais lisible, même par un agent).
/// Le hash contient son propre sel ; <see cref="Verify"/> servira à la livraison.
/// </summary>
public interface IWithdrawalCodeHasher
{
    string Hash(string code);

    bool Verify(string code, string hash);
}
