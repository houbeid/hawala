using System.Globalization;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace Identity.Infrastructure.Credentials;

/// <summary>
/// Mots de passe et verrouillage via ASP.NET Identity (UserManager).
/// Attention : chaque appel qui modifie l'utilisateur enregistre le DbContext du module.
/// Les handlers sont donc exécutés dans une transaction (voir l'API) pour rester atomiques.
/// </summary>
internal sealed class CredentialStore(
    UserManager<AppUser> userManager,
    IPasswordHasher<AppUser> passwordHasher)
    : ICredentialStore
{
    // Hash d'un mot de passe aléatoire, calculé une fois : sert à simuler une vérification.
    private static readonly AppUser DummyUser = new() { Id = Guid.Empty, UserName = "dummy" };
    private static string? _dummyHash;

    public async Task<Result> CreateAsync(Guid userId, string temporaryPassword, CancellationToken cancellationToken)
    {
        var user = new AppUser
        {
            Id = userId,
            UserName = userId.ToString("N", CultureInfo.InvariantCulture),
            LockoutEnabled = true,
        };

        var result = await userManager.CreateAsync(user, temporaryPassword);
        return ToResult(result);
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return PasswordCheckResult.Invalid;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return PasswordCheckResult.LockedOut;
        }

        if (await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.ResetAccessFailedCountAsync(user);
            return PasswordCheckResult.Success;
        }

        // Compte l'échec ; au 5e, Identity pose la date de fin de verrouillage.
        await userManager.AccessFailedAsync(user);

        return await userManager.IsLockedOutAsync(user)
            ? PasswordCheckResult.LockedOut
            : PasswordCheckResult.Invalid;
    }

    public Task SimulatePasswordCheckAsync(string password, CancellationToken cancellationToken)
    {
        _dummyHash ??= passwordHasher.HashPassword(DummyUser, Guid.NewGuid().ToString());
        _ = passwordHasher.VerifyHashedPassword(DummyUser, _dummyHash, password);
        return Task.CompletedTask;
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return ToResult(result);
    }

    private static Result ToResult(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return Result.Success();
        }

        var codes = result.Errors.Select(e => e.Code).ToList();

        if (codes.Contains(nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            return IdentityErrors.Password.Incorrect;
        }

        if (codes.Exists(code => code.StartsWith("Password", StringComparison.Ordinal)))
        {
            return IdentityErrors.Password.TooWeak;
        }

        return Error.Failure("Identity.Credentials.Failure", string.Join(", ", codes));
    }
}