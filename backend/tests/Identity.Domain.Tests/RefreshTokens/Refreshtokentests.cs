using Identity.Domain;
using Identity.Domain.RefreshTokens;

namespace Identity.Domain.Tests.RefreshTokens;

public class RefreshTokenTests
{
    private static readonly Guid UserId = Guid.Parse("0199f0a0-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    // --- Émission ----------------------------------------------------------

    [Fact]
    public void New_session_token_is_active_and_expires_after_lifetime()
    {
        var issued = RefreshToken.IssueForNewSession(UserId, Now, Lifetime);
        var token = issued.Token;

        Assert.Equal(UserId, token.UserId);
        Assert.Equal(Now, token.CreatedAt);
        Assert.Equal(Now + Lifetime, token.ExpiresAt);
        Assert.True(token.IsActive(Now));
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void Only_the_hash_is_kept_on_the_token()
    {
        var issued = RefreshToken.IssueForNewSession(UserId, Now, Lifetime);

        Assert.NotEqual(issued.PlainValue, issued.Token.TokenHash);
        Assert.Equal(RefreshToken.HashOf(issued.PlainValue), issued.Token.TokenHash);
    }

    [Fact]
    public void Each_issue_produces_a_different_secret_and_a_new_family()
    {
        var a = RefreshToken.IssueForNewSession(UserId, Now, Lifetime);
        var b = RefreshToken.IssueForNewSession(UserId, Now, Lifetime);

        Assert.NotEqual(a.PlainValue, b.PlainValue);
        Assert.NotEqual(a.Token.FamilyId, b.Token.FamilyId);
    }

    [Fact]
    public void Issue_without_user_is_refused()
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.IssueForNewSession(Guid.Empty, Now, Lifetime));
    }

    [Fact]
    public void Issue_with_non_positive_lifetime_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RefreshToken.IssueForNewSession(UserId, Now, TimeSpan.Zero));
    }

    // --- Rotation ----------------------------------------------------------

    [Fact]
    public void Rotation_revokes_old_token_and_issues_new_one_in_same_family()
    {
        var first = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;
        var later = Now.AddMinutes(15);

        var result = first.Rotate(later, Lifetime);

        Assert.True(result.IsSuccess);
        var next = result.Value.Token;

        Assert.Equal(first.FamilyId, next.FamilyId);
        Assert.Equal(UserId, next.UserId);
        Assert.Equal(later + Lifetime, next.ExpiresAt);
        Assert.True(next.IsActive(later));

        Assert.True(first.IsRevoked);
        Assert.Equal(RefreshTokenRevocationReason.Rotated, first.RevocationReason);
        Assert.Equal(later, first.RevokedAt);
        Assert.Equal(next.Id, first.ReplacedByTokenId);
    }

    [Fact]
    public void Replaying_an_already_rotated_token_is_detected_as_reuse()
    {
        var first = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;
        _ = first.Rotate(Now.AddMinutes(15), Lifetime);

        var replay = first.Rotate(Now.AddMinutes(20), Lifetime);

        Assert.Equal(IdentityErrors.RefreshToken.Reused, replay.Error);
    }

    [Fact]
    public void Token_revoked_by_logout_cannot_be_rotated()
    {
        var token = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;
        token.Revoke(Now.AddMinutes(5), RefreshTokenRevocationReason.Logout);

        var result = token.Rotate(Now.AddMinutes(10), Lifetime);

        Assert.Equal(IdentityErrors.RefreshToken.Revoked, result.Error);
    }

    [Fact]
    public void Expired_token_cannot_be_rotated_and_is_left_untouched()
    {
        var token = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;

        var result = token.Rotate(token.ExpiresAt, Lifetime);

        Assert.Equal(IdentityErrors.RefreshToken.Expired, result.Error);
        Assert.False(token.IsRevoked);
    }

    // --- Révocation et état ------------------------------------------------

    [Fact]
    public void Revoking_twice_keeps_the_first_reason_and_date()
    {
        var token = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;
        var firstRevocation = Now.AddMinutes(5);

        token.Revoke(firstRevocation, RefreshTokenRevocationReason.ReuseDetected);
        token.Revoke(Now.AddMinutes(10), RefreshTokenRevocationReason.Logout);

        Assert.Equal(RefreshTokenRevocationReason.ReuseDetected, token.RevocationReason);
        Assert.Equal(firstRevocation, token.RevokedAt);
    }

    [Fact]
    public void Token_is_no_longer_active_at_expiry()
    {
        var token = RefreshToken.IssueForNewSession(UserId, Now, Lifetime).Token;

        Assert.True(token.IsActive(token.ExpiresAt.AddTicks(-1)));
        Assert.False(token.IsActive(token.ExpiresAt));
    }

    [Fact]
    public void Hash_is_deterministic()
    {
        Assert.Equal(RefreshToken.HashOf("valeur-fictive"), RefreshToken.HashOf("valeur-fictive"));
        Assert.NotEqual(RefreshToken.HashOf("valeur-fictive"), RefreshToken.HashOf("autre-valeur"));
    }
}