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