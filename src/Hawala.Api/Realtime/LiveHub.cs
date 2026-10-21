using System.Globalization;
using System.Security.Claims;
using Identity.Contracts;
using Identity.Infrastructure.Sessions;
using Identity.Infrastructure.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Hawala.Api.Realtime;

/// <summary>
/// Hub temps réel (règles validées le 21 octobre 2026) : un groupe par agence et un groupe des Superviseurs.
/// À la connexion, la session est revalidée dans le scope du hub (jeton + SecurityVersion, comme pour toute requête :
/// en long polling, SignalR recrée un scope et le périmètre de la requête d'origine n'y est pas), puis le compte
/// rejoint le groupe de son agence, ou celui des Superviseurs. Un compte sans agence et non Superviseur est refusé.
/// Messages envoyés (légers, sans montant ni nom) : « TransferChanged » et « RatesChanged ».
/// POINT OUVERT : déconnexion en direct d'un compte bloqué (phase 6, avec les appareils connectés) ;
/// en attendant, l'API refuse ses requêtes en 30 secondes au plus.
/// </summary>
[Authorize]
public sealed class LiveHub(SessionValidator sessions, IAgencyScope scope) : Hub
{
    public const string Path = "/hubs/live";
    public const string SupervisorsGroup = "supervisors";
    public const string TransferChangedMessage = "TransferChanged";
    public const string RatesChangedMessage = "RatesChanged";

    public static string AgencyGroup(Guid agencyId) => $"agency:{agencyId:N}";

    public override async Task OnConnectedAsync()
    {
        if (!await IsSessionCurrentAsync(Context.User))
        {
            Context.Abort();
            return;
        }

        if (scope.SeesAllAgencies)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SupervisorsGroup);
        }
        else if (scope.AgencyId is { } agencyId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AgencyGroup(agencyId));
        }
        else
        {
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Compte actif et SecurityVersion inchangée ; remplit le périmètre du scope du hub.</summary>
    private Task<bool> IsSessionCurrentAsync(ClaimsPrincipal? user)
    {
        var hasAccountId = Guid.TryParse(user?.FindFirstValue(HawalaClaimTypes.Subject), out var accountId);
        var hasVersion = int.TryParse(
            user?.FindFirstValue(HawalaClaimTypes.SecurityVersion), NumberStyles.None, CultureInfo.InvariantCulture, out var version);

        return hasAccountId && hasVersion
            ? sessions.IsSessionCurrentAsync(accountId, version, Context.ConnectionAborted)
            : Task.FromResult(false);
    }
}
