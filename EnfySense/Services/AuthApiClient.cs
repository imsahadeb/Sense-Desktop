using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EnfyLiveScreenClient.Services;

public sealed class AuthApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<AuthSession> LoginWithMicrosoftAsync(
        string backendUrl,
        string keycloakIssuer,
        string clientId,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var normalizedIssuer = NormalizeRequiredUrl(keycloakIssuer, "Keycloak issuer");
        var normalizedRedirectUri = NormalizeRequiredUrl(redirectUri, "SSO redirect URI");
        var normalizedClientId = (clientId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            throw new InvalidOperationException("Keycloak client ID is required.");
        }

        var callbackUri = new Uri(normalizedRedirectUri, UriKind.Absolute);
        if (!callbackUri.IsLoopback)
        {
            throw new InvalidOperationException("Desktop SSO redirect URI must use localhost or loopback.");
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(BuildLoopbackListenerPrefix(callbackUri));

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Unable to start local SSO callback listener on {callbackUri.GetLeftPart(UriPartial.Authority)}. {ex.Message}",
                ex);
        }

        var authorizationUrl = BuildAuthorizationUrl(
            normalizedIssuer,
            normalizedClientId,
            normalizedRedirectUri);
        OpenSystemBrowser(authorizationUrl);

        var callbackContext = await WaitForCallbackAsync(
            listener,
            callbackUri.AbsolutePath,
            cancellationToken);

        var error = callbackContext.Request.QueryString["error_description"] ??
                    callbackContext.Request.QueryString["error"];
        if (!string.IsNullOrWhiteSpace(error))
        {
            await WriteBrowserResponseAsync(
                callbackContext.Response,
                "Microsoft sign-in failed. You can close this tab and return to the desktop app.");
            throw new InvalidOperationException(error);
        }

        var code = callbackContext.Request.QueryString["code"];
        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteBrowserResponseAsync(
                callbackContext.Response,
                "No authorization code was returned. You can close this tab and return to the desktop app.");
            throw new InvalidOperationException("Microsoft sign-in did not return an authorization code.");
        }

        await WriteBrowserResponseAsync(
            callbackContext.Response,
            "Microsoft sign-in completed. You can close this tab and return to EnfySense.");

        return await ExchangeSsoCodeAsync(
            backendUrl,
            code,
            normalizedRedirectUri,
            cancellationToken);
    }

    public async Task<AuthSession> RefreshTokenAsync(
        string backendUrl,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
        }

        using var client = CreateClient(backendUrl);
        using var response = await client.PostAsJsonAsync(
            "auth/refresh",
            new RefreshTokenRequest(refreshToken),
            JsonOptions,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ExtractErrorMessage(responseText) ??
                $"Token refresh failed with status {(int)response.StatusCode}.");
        }

        var payload = JsonSerializer.Deserialize<LoginResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("Backend returned an empty token refresh response.");

        return new AuthSession(
            payload.AccessToken,
            payload.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(payload.ExpiresIn, 1)),
            payload.User!);
    }

    public async Task LogoutAsync(
        string backendUrl,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        using var client = CreateClient(backendUrl);
        using var response = await client.PostAsJsonAsync(
            "auth/logout",
            new LogoutRequest(refreshToken),
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                ExtractErrorMessage(responseText) ??
                $"Logout failed with status {(int)response.StatusCode}.");
        }
    }

    public async Task<bool> AcceptTermsAsync(
        string backendUrl,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(backendUrl);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.PostAsync("auth/accept-terms", null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<T?> GetAsync<T>(string url, string? accessToken = null, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        if (!string.IsNullOrEmpty(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }
        
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    public async Task<List<string>> FetchAdminSecretsAsync(
        string backendUrl,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(backendUrl);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.GetAsync("auth/admin-secrets", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<string>();
        }

        var secrets = await response.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken);
        return secrets ?? new List<string>();
    }

    private async Task<AuthSession> ExchangeSsoCodeAsync(
        string backendUrl,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(backendUrl);
        using var response = await client.PostAsJsonAsync(
            "auth/sso-login",
            new SsoLoginRequest(code, redirectUri),
            JsonOptions,
            cancellationToken);

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                ExtractErrorMessage(responseText) ??
                $"Microsoft sign-in failed with status {(int)response.StatusCode}.");
        }

        // Detect HTML responses (usually standard error pages or redirects)
        if (responseText.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            AppLogger.Log($"Received HTML instead of JSON from backend: {responseText.Substring(0, Math.Min(200, responseText.Length))}...", LogLevel.Error);
            throw new InvalidOperationException("The backend returned an HTML page instead of JSON. This often means the Backend URL is incorrect or pointing to a web server instead of the API.");
        }

        var payload = JsonSerializer.Deserialize<LoginResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("Backend returned an empty SSO login response.");

        if (string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken) ||
            payload.User is null)
        {
            throw new InvalidOperationException("Backend returned an incomplete SSO login response.");
        }

        return new AuthSession(
            payload.AccessToken,
            payload.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(payload.ExpiresIn, 1)),
            payload.User);
    }

    private static HttpClient CreateClient(string backendUrl)
    {
        var normalizedBaseUrl = NormalizeRequiredUrl(backendUrl, "Backend URL");
        return new HttpClient
        {
            BaseAddress = new Uri($"{normalizedBaseUrl}/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    private static string BuildAuthorizationUrl(
        string keycloakIssuer,
        string clientId,
        string redirectUri)
    {
        var authUrl = new Uri($"{keycloakIssuer.TrimEnd('/')}/protocol/openid-connect/auth");
        var builder = new UriBuilder(authUrl)
        {
            Query =
                $"client_id={Uri.EscapeDataString(clientId)}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString("openid profile email offline_access")}" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&kc_idp_hint=microsoft",
        };
        return builder.Uri.ToString();
    }

    private static string BuildLoopbackListenerPrefix(Uri callbackUri)
    {
        var portSegment = callbackUri.IsDefaultPort ? string.Empty : $":{callbackUri.Port}";
        return $"{callbackUri.Scheme}://{callbackUri.Host}{portSegment}/";
    }

    private static async Task<HttpListenerContext> WaitForCallbackAsync(
        HttpListener listener,
        string expectedPath,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5), timeoutCts.Token);

        while (true)
        {
            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Timed out waiting for the Microsoft sign-in callback.");
            }

            var context = await contextTask;
            var requestPath = context.Request.Url?.AbsolutePath ?? "/";
            if (string.Equals(requestPath, expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                timeoutCts.Cancel();
                return context;
            }

            context.Response.StatusCode = 404;
            await WriteBrowserResponseAsync(
                context.Response,
                "This endpoint is reserved for the EnfySense client sign-in callback.");
        }
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerResponse response,
        string message)
    {
        var html = $"""
            <html>
              <head>
                <meta charset="utf-8" />
                <title>EnfySense Sign-In</title>
              </head>
              <body style="font-family: Segoe UI, sans-serif; background: #0b1220; color: #f8fafc; padding: 32px;">
                <h2>EnfySense</h2>
                <p>{WebUtility.HtmlEncode(message)}</p>
              </body>
            </html>
            """;

        response.ContentType = "text/html; charset=utf-8";
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(html);
        await writer.FlushAsync();
        response.Close();
    }

    private static void OpenSystemBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to open the Microsoft sign-in browser window. Open this URL manually: {url}",
                ex);
        }
    }

    private static string NormalizeRequiredUrl(string rawUrl, string fieldName)
    {
        var normalized = (rawUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"{fieldName} is not a valid URL.");
        }

        return normalized;
    }

    private static string? ExtractErrorMessage(string? jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            if (!document.RootElement.TryGetProperty("message", out var messageElement))
            {
                return null;
            }

            return messageElement.ValueKind switch
            {
                JsonValueKind.String => messageElement.GetString(),
                JsonValueKind.Array when messageElement.GetArrayLength() > 0 =>
                    messageElement[0].GetString(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed record RefreshTokenRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record SsoLoginRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("redirectUri")] string RedirectUri);

    private sealed record LogoutRequest(
        [property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed class LoginResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("user")]
        public AuthUserInfo? User { get; init; }
    }
}

public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    AuthUserInfo User);

public sealed class AuthUserInfo
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();
    public bool TermsAccepted { get; set; }


    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Email : FullName!;
}
