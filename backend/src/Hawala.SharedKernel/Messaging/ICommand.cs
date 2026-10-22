namespace Hawala.SharedKernel.Messaging;

/// <summary>
/// Commande : une intention qui change l'état (créer un compte, livrer un transfert…).
/// Une commande = une transaction. Elle renvoie un <see cref="Results.Result"/> :
/// succès, ou erreur métier attendue.
/// </summary>
public interface ICommand;

/// <summary>Commande qui renvoie une valeur en cas de succès (ex. Login → jetons).</summary>
/// <typeparam name="TResponse">Type de la valeur renvoyée.</typeparam>
public interface ICommand<TResponse>;