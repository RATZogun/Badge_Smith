using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BadgeSmith.Steam;

public enum SteamGuardType
{
    Unknown = 0,
    None = 1,
    EmailCode = 2,
    DeviceCode = 3,
    DeviceConfirmation = 4,
    EmailConfirmation = 5,
    MachineToken = 6,
}

public sealed record SteamAuthSession(ulong ClientId, byte[] RequestId, TimeSpan Interval, IReadOnlyList<SteamGuardType> Confirmations, ulong SteamId, string? ChallengeUrl);

public sealed record SteamAuthPoll(ulong? NewClientId, string? NewChallengeUrl, string? RefreshToken, string? AccountName);

public sealed class SteamAuthException(int result, string message) : Exception(message)
{
    public int Result { get; } = result;
}

public sealed class SteamAuthApi : IDisposable
{
    private const string DeviceName = "Badge Smith";
    private const int WebBrowserPlatform = 2;
    private const int PersistentSession = 1;
    private const int PermanentRevoke = 1;
    private const string Website = "Community";
    private const string ServiceUrl = "https://api.steampowered.com/IAuthenticationService/";

    private readonly HttpClient _http;

    public SteamAuthApi(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.Add("Origin", "https://steamcommunity.com");
        _http.DefaultRequestHeaders.Add("Referer", "https://steamcommunity.com/");
    }

    public async Task<SteamAuthSession> BeginWithQrAsync(CancellationToken cancellationToken)
    {
        var request = new ProtoWriter()
            .String(1, DeviceName)
            .Varint(2, WebBrowserPlatform)
            .Message(3, DeviceDetails())
            .String(4, Website);
        var response = await CallAsync("BeginAuthSessionViaQR", request, HttpMethod.Post, cancellationToken);
        return new SteamAuthSession(
            response.Varint(1),
            response.Bytes(3),
            Interval(response.Float(4)),
            Confirmations(response.Messages(5)),
            0,
            response.String(2));
    }

    public async Task<SteamAuthSession> BeginWithCredentialsAsync(string accountName, string password, CancellationToken cancellationToken)
    {
        var key = await CallAsync("GetPasswordRSAPublicKey", new ProtoWriter().String(1, accountName), HttpMethod.Get, cancellationToken);
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Convert.FromHexString(key.String(1)),
            Exponent = Convert.FromHexString(key.String(2)),
        });
        var encrypted = Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.Pkcs1));

        var request = new ProtoWriter()
            .String(1, DeviceName)
            .String(2, accountName)
            .String(3, encrypted)
            .Varint(4, key.Varint(3))
            .Bool(5, true)
            .Varint(6, WebBrowserPlatform)
            .Varint(7, PersistentSession)
            .String(8, Website)
            .Message(9, DeviceDetails());
        var response = await CallAsync("BeginAuthSessionViaCredentials", request, HttpMethod.Post, cancellationToken);
        return new SteamAuthSession(
            response.Varint(1),
            response.Bytes(2),
            Interval(response.Float(3)),
            Confirmations(response.Messages(4)),
            response.Varint(5),
            null);
    }

    public async Task SubmitGuardCodeAsync(SteamAuthSession session, string code, SteamGuardType type, CancellationToken cancellationToken)
    {
        var request = new ProtoWriter()
            .Varint(1, session.ClientId)
            .Fixed64(2, session.SteamId)
            .String(3, code)
            .Varint(4, (ulong)type);
        await CallAsync("UpdateAuthSessionWithSteamGuardCode", request, HttpMethod.Post, cancellationToken);
    }

    public async Task<SteamAuthPoll> PollAsync(ulong clientId, byte[] requestId, CancellationToken cancellationToken)
    {
        var request = new ProtoWriter().Varint(1, clientId).Bytes(2, requestId);
        var response = await CallAsync("PollAuthSessionStatus", request, HttpMethod.Post, cancellationToken);
        return new SteamAuthPoll(
            response.Varint(1) is var id and > 0 ? id : null,
            NullIfEmpty(response.String(2)),
            NullIfEmpty(response.String(3)),
            NullIfEmpty(response.String(6)));
    }

    public async Task<string> GetLoginCookieAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var sessionId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        using var finalize = await _http.PostAsync(
            "https://login.steampowered.com/jwt/finalizelogin",
            Form(new()
            {
                ["nonce"] = refreshToken,
                ["sessionid"] = sessionId,
                ["redir"] = "https://steamcommunity.com/login/home/?goto=",
            }),
            cancellationToken);
        finalize.EnsureSuccessStatusCode();

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(await finalize.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            throw new SteamAuthException(0, "A Steam devolveu uma resposta inesperada ao abrir a sessão. Tente de novo.");
        }

        using var document = json;
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("transfer_info", out var transfers)
            || transfers.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("steamID", out var steamId))
        {
            var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number) ? number : 0;
            throw new SteamAuthException(error, "A Steam não aceitou a sessão salva. Faça login de novo.");
        }

        foreach (var transfer in transfers.EnumerateArray())
        {
            var url = new Uri(transfer.GetProperty("url").GetString()!);
            if (url.Host != "steamcommunity.com")
            {
                continue;
            }

            var fields = transfer.GetProperty("params").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ToString());
            fields["steamID"] = steamId.GetString()!;
            using var response = await _http.PostAsync(url, Form(fields), cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies) && LoginCookie(cookies) is { } cookie)
            {
                return cookie;
            }
        }

        throw new SteamAuthException(0, "A Steam não devolveu o cookie de sessão da comunidade.");
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var cookie = WebUtility.UrlDecode(await GetLoginCookieAsync(refreshToken, cancellationToken));
        var accessToken = cookie[(cookie.IndexOf("||", StringComparison.Ordinal) + 2)..];
        var request = new ProtoWriter().String(1, refreshToken).Varint(2, PermanentRevoke);
        await CallAsync("RevokeToken", request, HttpMethod.Post, cancellationToken, accessToken);
    }

    public void Dispose() => _http.Dispose();

    public static ulong? SteamIdFromToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("sub", out var sub) && ulong.TryParse(sub.GetString(), out var steamId) ? steamId : null;
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? LoginCookie(IEnumerable<string> setCookieHeaders) =>
        setCookieHeaders
            .Select(header => header.Split(';', 2)[0].Trim())
            .Where(pair => pair.StartsWith("steamLoginSecure=", StringComparison.Ordinal))
            .Select(pair => pair["steamLoginSecure=".Length..])
            .FirstOrDefault(value => value.Length > 0 && value != "deleted");

    private async Task<ProtoMessage> CallAsync(string method, ProtoWriter request, HttpMethod httpMethod, CancellationToken cancellationToken, string? accessToken = null)
    {
        var encoded = Convert.ToBase64String(request.ToArray());
        var url = $"{ServiceUrl}{method}/v1";
        if (accessToken is not null)
        {
            url += $"?access_token={Uri.EscapeDataString(accessToken)}";
        }

        using var message = httpMethod == HttpMethod.Get
            ? new HttpRequestMessage(HttpMethod.Get, $"{url}?input_protobuf_encoded={Uri.EscapeDataString(encoded)}")
            : new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent([new("input_protobuf_encoded", encoded)]) };
        using var response = await _http.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = response.Headers.TryGetValues("x-eresult", out var values) && int.TryParse(values.First(), out var r) ? r : 1;
        if (result != 1)
        {
            var detail = response.Headers.TryGetValues("x-error_message", out var errors) ? errors.First() : null;
            throw new SteamAuthException(result, Describe(result, detail));
        }

        return ProtoMessage.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private static string Describe(int result, string? detail) => result switch
    {
        5 => "Usuário ou senha incorretos.",
        9 or 27 => "O pedido de login expirou. Comece de novo.",
        65 or 88 => "Código do Steam Guard incorreto.",
        84 => "Muitas tentativas de login. Espere alguns minutos e tente de novo.",
        _ => $"A Steam recusou o login (EResult {result}{(string.IsNullOrEmpty(detail) ? "" : $": {detail}")}).",
    };

    private static ProtoWriter DeviceDetails() => new ProtoWriter().String(1, DeviceName).Varint(2, WebBrowserPlatform);

    private static TimeSpan Interval(float seconds) => TimeSpan.FromSeconds(seconds > 0 ? seconds : 5);

    private static List<SteamGuardType> Confirmations(IEnumerable<ProtoMessage> messages) =>
        messages.Select(m => (SteamGuardType)m.Varint(1)).ToList();

    private static MultipartFormDataContent Form(Dictionary<string, string> fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, value) in fields)
        {
            var part = new StringContent(value);
            part.Headers.ContentType = null;
            form.Add(part, name);
        }

        return form;
    }

    private static string? NullIfEmpty(string value) => value.Length > 0 ? value : null;
}
