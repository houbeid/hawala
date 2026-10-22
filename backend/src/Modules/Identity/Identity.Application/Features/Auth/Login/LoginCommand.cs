using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Auth.Login;

/// <param name="Login">Contenu du champ unique « Email ou téléphone ».</param>
public sealed record LoginCommand(string? Login, string? Password) : ICommand<AuthTokens>;