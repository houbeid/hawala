using System.Globalization;
using System.Security.Claims;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.Tokens;

internal sealed class JwtTokenService : IAccessTokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly AuthSettings _settings;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock, AuthSettings settings)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _clock = clock;
        _settings = settings;
        _signingCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Issue(UserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var now = _clock.UtcNow;
        var expiresAt = now + _settings.AccessTokenLifetime;

        var claims = new List<Claim>
        {
            new(HawalaClaimTypes.Subject, account.Id.ToString()),
            new(HawalaClaimTypes.SecurityVersion, account.SecurityVersion.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (account.MustChangePassword)
        {
            claims.Add(new Claim(HawalaClaimTypes.PasswordChangeRequired, "true"));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
        };

        return new AccessToken(_handler.CreateToken(descriptor), expiresAt);
    }
}