using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Auth.ChangePassword;

/// <param name="UserId">Compte connecté, lu par l'API dans le JWT (jamais dans le corps de la requête).</param>
public sealed record ChangePasswordCommand(Guid UserId, string? CurrentPassword, string? NewPassword)
    : ICommand<AuthTokens>;