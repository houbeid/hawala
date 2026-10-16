using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.SharedKernel.Messaging;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.ChangePassword;
using Identity.Application.Features.Auth.Login;
using Identity.Application.Features.Auth.Logout;
using Identity.Application.Features.Auth.Refresh;
using Identity.Infrastructure.Tokens;

namespace Hawala.Api.Endpoints.Identity;

public static class AuthEndpoints
{
    public const string RefreshCookieName = "hawala_refresh";

    // Le cookie n'est envoyé qu'aux endpoints d'authentification, jamais aux autres requêtes.
    private const string RefreshCookiePath = "/api/v1/auth";

    public sealed record LoginRequest(string? Login, string? Password);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    /// <summary>Le refresh token n'est jamais dans le corps : il est dans le cookie HttpOnly.</summary>
    public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, bool MustChangePassword);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/refresh", Refresh).AllowAnonymous();
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapPost("/change-password", ChangePassword)
            .RequireAuthorization(AuthorizationPolicies.PasswordChangeAllowed);

        return app;
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        ICommandHandler<LoginCommand, AuthTokens> handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new LoginCommand(request.Login, request.Password), cancellationToken);

        return result.IsSuccess ? SignedIn(http, result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> Refresh(
        ICommandHandler<RefreshCommand, AuthTokens> handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var token = http.Request.Cookies[RefreshCookieName];
        var result = await handler.Handle(new RefreshCommand(token), cancellationToken);

        if (result.IsFailure)
        {
            DeleteRefreshCookie(http);
            return result.Error.ToProblem();
        }

        return SignedIn(http, result.Value);
    }

    private static async Task<IResult> Logout(
        ICommandHandler<LogoutCommand> handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var token = http.Request.Cookies[RefreshCookieName];
        await handler.Handle(new LogoutCommand(token), cancellationToken);

        DeleteRefreshCookie(http);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        ChangePasswordRequest request,
        ClaimsPrincipal user,
        ICommandHandler<ChangePasswordCommand, AuthTokens> handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        // L'identité vient uniquement du JWT, jamais du corps de la requête.
        if (!Guid.TryParse(user.FindFirstValue(HawalaClaimTypes.Subject), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var command = new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword);
        var result = await handler.Handle(command, cancellationToken);

        return result.IsSuccess ? SignedIn(http, result.Value) : result.Error.ToProblem();
    }

    private static IResult SignedIn(HttpContext http, AuthTokens tokens)
    {
        http.Response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = tokens.RefreshTokenExpiresAt,
            IsEssential = true,
        });

        return TypedResults.Ok(new AuthResponse(
            tokens.AccessToken.Value,
            tokens.AccessToken.ExpiresAt,
            tokens.MustChangePassword));
    }

    private static void DeleteRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
        });
}