using System.Globalization;
using System.Security.Claims;
using Identity.Contracts;
using Identity.Infrastructure.Sessions;
using Identity.Infrastructure.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hawala.Api.Configuration;

public static class AuthenticationSetup
{
    public static IServiceCollection AddHawalaAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configuré à partir de JwtOptions au premier usage (pas à l'enregistrement) :
        // les outils comme « dotnet ef » peuvent ainsi démarrer sans clé JWT.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Garde les noms courts (« sub », « sv ») au lieu des longues URI de .NET.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = HawalaClaimTypes.Subject,
                };

                // Option B : après la signature, on vérifie que le compte est actif et que sa SecurityVersion
                // n'a pas changé depuis l'émission du jeton.
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync,
                    OnMessageReceived = ReadHubTokenFromQuery,
                };
            });

        // Une requête authentifiée avec un mot de passe temporaire n'a droit qu'au changement de mot de passe.
        var standardPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == HawalaClaimTypes.PasswordChangeRequired))
            .Build();

        var authorization = services.AddAuthorizationBuilder()
            .SetDefaultPolicy(standardPolicy)
            // Sûr par défaut : tout endpoint est protégé sauf ceux marqués AllowAnonymous.
            .SetFallbackPolicy(standardPolicy)
            .AddPolicy(AuthorizationPolicies.PasswordChangeAllowed, policy => policy.RequireAuthenticatedUser());

        // Une politique par permission : la politique standard (connecté, mot de passe changé) + la permission.
        foreach (var permission in Permissions.All)
        {
            authorization.AddPolicy(
                PermissionEndpointExtensions.PolicyName(permission),
                policy => policy
                    .Combine(standardPolicy)
                    .AddRequirements(new PermissionRequirement(permission)));
        }

        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// Un WebSocket ne peut pas porter d'en-tête Authorization : pour le hub temps réel SEULEMENT,
    /// le jeton d'accès est lu dans l'URL (« access_token »). Partout ailleurs, seul l'en-tête compte.
    /// </summary>
    private static Task ReadHubTokenFromQuery(MessageReceivedContext context)
    {
        var token = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(Realtime.LiveHub.Path))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;

        var hasAccountId = Guid.TryParse(principal?.FindFirstValue(HawalaClaimTypes.Subject), out var accountId);
        var hasVersion = int.TryParse(
            principal?.FindFirstValue(HawalaClaimTypes.SecurityVersion),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var securityVersion);

        if (!hasAccountId || !hasVersion)
        {
            context.Fail("Jeton incomplet.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
        var sessionId = principal!.GetSessionId();
        if (!await validator.IsSessionCurrentAsync(accountId, securityVersion, sessionId, context.HttpContext.RequestAborted))
        {
            context.Fail("Session périmée : compte bloqué ou modifié, ou session fermée, depuis l'émission du jeton.");
        }
    }
}