namespace Hawala.SharedKernel.Messaging;

/// <summary>
/// Requête : une lecture, qui ne change jamais l'état (liste des comptes, relevé d'un tiers…).
/// Peut être servie par EF Core ou par Dapper pour les rapports lourds.
/// </summary>
/// <typeparam name="TResponse">Type de la donnée lue.</typeparam>
public interface IQuery<TResponse>;