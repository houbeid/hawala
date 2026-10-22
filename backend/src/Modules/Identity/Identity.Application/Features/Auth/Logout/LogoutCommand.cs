using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Auth.Logout;

/// <summary>Ferme la session de cet appareil seulement.</summary>
/// <param name="RefreshToken">Jeton en clair, lu par l'API dans le cookie HttpOnly.</param>
public sealed record LogoutCommand(string? RefreshToken) : ICommand;