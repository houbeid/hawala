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

                // POINT OUVERT : comparer le claim « sv » à la SecurityVersion du compte (avec cache mémoire)
                // pour refuser tout de suite un compte bloqué ou modifié (option B). Prochaine étape.
            });

        // Une requête authentifiée avec un mot de passe temporaire n'a droit qu'au changement de mot de passe.
        var standardPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => !ctx.User.HasClaim(c => c.Type == HawalaClaimTypes.PasswordChangeRequired))
            .Build();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(standardPolicy)
            // Sûr par défaut : tout endpoint est protégé sauf ceux marqués AllowAnonymous.
            .SetFallbackPolicy(standardPolicy)
            .AddPolicy(AuthorizationPolicies.PasswordChangeAllowed, policy => policy.RequireAuthenticatedUser());

        return services;
    }
}