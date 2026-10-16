using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Auth.Refresh;

/// <param name="RefreshToken">Jeton en clair, lu par l'API dans le cookie HttpOnly.</param>
public sealed record RefreshCommand(string? RefreshToken) : ICommand<AuthTokens>;