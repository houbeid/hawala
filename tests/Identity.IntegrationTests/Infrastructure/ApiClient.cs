using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Identity.IntegrationTests.Infrastructure;

/// <summary>
/// Appels HTTP vers l'API. Le cookie de rafraîchissement est géré à la main :
/// il est Secure et le serveur de test est en http, un client classique ne le renverrait pas.
/// </summary>
public static class ApiClient
{
    public const string RefreshCookieName = "hawala_refresh";
    private const string Auth = "/api/v1/auth";

    public static HttpClient CreateApiClient(this HawalaApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string login, string password) =>
        client.SendAsync(HttpMethod.Post, $"{Auth}/login", body: new { login, password });

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string? refreshCookie) =>
        client.SendAsync(HttpMethod.Post, $"{Auth}/refresh", refreshCookie: refreshCookie);

    public static Task<HttpResponseMessage> LogoutAsync(this HttpClient client, string? refreshCookie) =>
        client.SendAsync(HttpMethod.Post, $"{Auth}/logout", refreshCookie: refreshCookie);

    public static Task<HttpResponseMessage> ChangePasswordAsync(
        this HttpClient client, string accessToken, string currentPassword, string newPassword) =>
        client.SendAsync(
            HttpMethod.Post,
            $"{Auth}/change-password",
            body: new { currentPassword, newPassword },
            accessToken: accessToken);

    public static Task<HttpResponseMessage> MeAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(HttpMethod.Get, $"{Auth}/me", accessToken: accessToken);

    public static Task<HttpResponseMessage> GetAgenciesAsync(this HttpClient client, string? accessToken) =>
        client.SendAsync(HttpMethod.Get, "/api/v1/agencies", accessToken: accessToken);

    /// <param name="body">Objet anonyme : email, phone, fullName, role, agencyId.</param>
    public static Task<HttpResponseMessage> CreateUserAsync(this HttpClient client, string? accessToken, object body) =>
        client.SendAsync(HttpMethod.Post, "/api/v1/users", body: body, accessToken: accessToken);

    /// <param name="queryString">Filtres déjà encodés, sans « ? » (ex. « role=Agent&amp;search=abc »), ou null.</param>
    public static Task<HttpResponseMessage> ListUsersAsync(this HttpClient client, string? accessToken, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            string.IsNullOrEmpty(queryString) ? "/api/v1/users" : $"/api/v1/users?{queryString}",
            accessToken: accessToken);

    public static Task<HttpResponseMessage> GetUserAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Get, $"/api/v1/users/{id}", accessToken: accessToken);

    public static Task<HttpResponseMessage> BlockUserAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/block", accessToken: accessToken);

    public static Task<HttpResponseMessage> UnblockUserAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/unblock", accessToken: accessToken);

    /// <param name="body">Objet anonyme : role, agencyId.</param>
    public static Task<HttpResponseMessage> ChangeUserRoleAsync(this HttpClient client, string? accessToken, Guid id, object body) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/change-role", body: body, accessToken: accessToken);

    /// <param name="body">Objet anonyme : agencyId.</param>
    public static Task<HttpResponseMessage> ChangeUserAgencyAsync(this HttpClient client, string? accessToken, Guid id, object body) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/change-agency", body: body, accessToken: accessToken);

    public static Task<HttpResponseMessage> ResetUserPasswordAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/reset-password", accessToken: accessToken);

    /// <param name="body">Objet anonyme : email, phone, fullName (état complet du profil).</param>
    public static Task<HttpResponseMessage> UpdateUserProfileAsync(this HttpClient client, string? accessToken, Guid id, object body) =>
        client.SendAsync(HttpMethod.Put, $"/api/v1/users/{id}", body: body, accessToken: accessToken);

    public static Task<HttpResponseMessage> ChangeAgencyColorAsync(this HttpClient client, string? accessToken, Guid agencyId, string? color) =>
        client.SendAsync(
            HttpMethod.Post, $"/api/v1/agencies/{agencyId}/change-color", body: new { color }, accessToken: accessToken);

    /// <param name="body">Objet anonyme : name, type, agencyId.</param>
    public static Task<HttpResponseMessage> CreateTreasuryAccountAsync(this HttpClient client, string? accessToken, object body) =>
        client.SendAsync(HttpMethod.Post, "/api/v1/treasury-accounts", body: body, accessToken: accessToken);

    /// <param name="queryString">Filtres déjà encodés, sans « ? » (ex. « type=Cash&amp;includeInactive=true »), ou null.</param>
    public static Task<HttpResponseMessage> ListTreasuryAccountsAsync(this HttpClient client, string? accessToken, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            string.IsNullOrEmpty(queryString) ? "/api/v1/treasury-accounts" : $"/api/v1/treasury-accounts?{queryString}",
            accessToken: accessToken);

    public static Task<HttpResponseMessage> GetTreasuryAccountAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Get, $"/api/v1/treasury-accounts/{id}", accessToken: accessToken);

    public static Task<HttpResponseMessage> RenameTreasuryAccountAsync(this HttpClient client, string? accessToken, Guid id, string? name) =>
        client.SendAsync(HttpMethod.Put, $"/api/v1/treasury-accounts/{id}", body: new { name }, accessToken: accessToken);

    public static Task<HttpResponseMessage> DeactivateTreasuryAccountAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/treasury-accounts/{id}/deactivate", accessToken: accessToken);

    public static Task<HttpResponseMessage> ReactivateTreasuryAccountAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/treasury-accounts/{id}/reactivate", accessToken: accessToken);

    /// <param name="body">Objet anonyme : name, type, phone, nni, address, gender, note.</param>
    public static Task<HttpResponseMessage> CreatePartyAsync(this HttpClient client, string? accessToken, object body) =>
        client.SendAsync(HttpMethod.Post, "/api/v1/parties", body: body, accessToken: accessToken);

    /// <param name="queryString">Filtres déjà encodés, sans « ? », ou null.</param>
    public static Task<HttpResponseMessage> ListPartiesAsync(this HttpClient client, string? accessToken, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            string.IsNullOrEmpty(queryString) ? "/api/v1/parties" : $"/api/v1/parties?{queryString}",
            accessToken: accessToken);

    public static Task<HttpResponseMessage> GetPartyAsync(this HttpClient client, string? accessToken, Guid id, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            string.IsNullOrEmpty(queryString) ? $"/api/v1/parties/{id}" : $"/api/v1/parties/{id}?{queryString}",
            accessToken: accessToken);

    /// <param name="body">Objet anonyme : name, type, phone, nni, address, note, inBalanceSheet (état complet).</param>
    public static Task<HttpResponseMessage> UpdatePartyAsync(this HttpClient client, string? accessToken, Guid id, object body) =>
        client.SendAsync(HttpMethod.Put, $"/api/v1/parties/{id}", body: body, accessToken: accessToken);

    /// <param name="body">Objet anonyme : amount, operationDate (aaaa-mm-jj, facultatif).</param>
    public static Task<HttpResponseMessage> SetOpeningBalanceAsync(
        this HttpClient client, string? accessToken, Guid partyId, Guid agencyId, object body) =>
        client.SendAsync(
            HttpMethod.Put, $"/api/v1/parties/{partyId}/agencies/{agencyId}/opening-balance", body: body, accessToken: accessToken);

    public static Task<HttpResponseMessage> SetCreditLimitAsync(
        this HttpClient client, string? accessToken, Guid partyId, Guid agencyId, decimal? creditLimit) =>
        client.SendAsync(
            HttpMethod.Put, $"/api/v1/parties/{partyId}/agencies/{agencyId}/credit-limit", body: new { creditLimit }, accessToken: accessToken);

    public static Task<HttpResponseMessage> ArchivePartyAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/parties/{id}/archive", accessToken: accessToken);

    public static Task<HttpResponseMessage> UnarchivePartyAsync(this HttpClient client, string? accessToken, Guid id) =>
        client.SendAsync(HttpMethod.Post, $"/api/v1/parties/{id}/unarchive", accessToken: accessToken);

    /// <param name="queryString">agencyId, from, to (aaaa-mm-jj), déjà encodés, sans « ? », ou null.</param>
    public static Task<HttpResponseMessage> GetPartyStatementAsync(this HttpClient client, string? accessToken, Guid id, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            string.IsNullOrEmpty(queryString) ? $"/api/v1/parties/{id}/statement" : $"/api/v1/parties/{id}/statement?{queryString}",
            accessToken: accessToken);

    public static Task<HttpResponseMessage> SetDailyRateAsync(
        this HttpClient client, string? accessToken, DateOnly date, decimal? purchaseRate, decimal? saleRate) =>
        client.SendAsync(
            HttpMethod.Put,
            $"/api/v1/rates/{date:yyyy-MM-dd}",
            body: new { purchaseRate, saleRate },
            accessToken: accessToken);

    /// <param name="path">« today », une date aaaa-mm-jj, ou vide pour la liste (avec queryString).</param>
    public static Task<HttpResponseMessage> GetRatesAsync(this HttpClient client, string? accessToken, string path, string? queryString = null) =>
        client.SendAsync(
            HttpMethod.Get,
            $"/api/v1/rates{(string.IsNullOrEmpty(path) ? string.Empty : "/" + path)}{(string.IsNullOrEmpty(queryString) ? string.Empty : "?" + queryString)}",
            accessToken: accessToken);

    /// <param name="idempotencyKey">En-tête Idempotency-Key ; null pour ne pas l'envoyer.</param>
    public static async Task<HttpResponseMessage> CreateTransferAsync(
        this HttpClient client, string? accessToken, object body, string? idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers")
        {
            Content = JsonContent.Create(body),
        };

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Connexion qui doit réussir : renvoie le jeton d'accès et la valeur du cookie.</summary>
    public static async Task<(string AccessToken, string RefreshCookie)> LoginOkAsync(
        this HttpClient client, string login, string password)
    {
        using var response = await client.LoginAsync(login, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadSessionAsync();
    }

    /// <summary>Lit le jeton d'accès (corps) et le refresh token (cookie) d'une réponse réussie.</summary>
    public static async Task<(string AccessToken, string RefreshCookie)> ReadSessionAsync(this HttpResponseMessage response)
    {
        var body = await response.ReadJsonAsync();
        var accessToken = body.GetProperty("accessToken").GetString();
        var refreshCookie = response.GetRefreshCookie();

        Assert.False(string.IsNullOrEmpty(accessToken));
        Assert.False(string.IsNullOrEmpty(refreshCookie));

        return (accessToken!, refreshCookie!);
    }

    /// <summary>Valeur du cookie hawala_refresh posé par la réponse, ou null (absent ou supprimé).</summary>
    public static string? GetRefreshCookie(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var header = response.GetRefreshCookieHeader();
        if (header is null)
        {
            return null;
        }

        var start = RefreshCookieName.Length + 1;
        var end = header.IndexOf(';', StringComparison.Ordinal);
        var value = end < 0 ? header[start..] : header[start..end];

        return value.Length == 0 ? null : value;
    }

    /// <summary>En-tête Set-Cookie complet du refresh token (pour vérifier ses attributs).</summary>
    public static string? GetRefreshCookieHeader(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal))
            : null;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Lu comme texte (mis en mémoire par HttpContent) : le corps peut être relu plusieurs fois
        // dans un même test, contrairement à ReadFromJsonAsync qui ferme le flux après lecture.
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>Champ « code » d'une réponse d'erreur (ProblemDetails).</summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response) =>
        (await response.ReadJsonAsync()).GetProperty("code").GetString();

    private static async Task<HttpResponseMessage> SendAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        object? body = null,
        string? accessToken = null,
        string? refreshCookie = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        }

        return await client.SendAsync(request);
    }
}
