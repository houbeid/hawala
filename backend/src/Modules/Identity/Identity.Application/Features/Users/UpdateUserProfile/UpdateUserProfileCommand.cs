using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.UpdateUserProfile;

/// <summary>
/// Le Superviseur corrige le nom, l'email ou le téléphone d'un compte, y compris le sien (règle validée).
/// Les trois champs sont envoyés à chaque fois : c'est l'état complet du profil, pas une modification partielle.
/// </summary>
/// <param name="ActorId">Compte connecté, lu par l'API dans le JWT : Superviseur, ou compte de la même agence avec users.manage.</param>
/// <param name="Phone">Vide ou absent = téléphone retiré.</param>
public sealed record UpdateUserProfileCommand(
    Guid ActorId,
    Guid UserId,
    string? Email,
    string? Phone,
    string? FullName) : ICommand;
