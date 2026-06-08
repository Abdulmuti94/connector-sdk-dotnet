using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VestedAI.ConnectorSdk.Errors;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Business Central client for the ASG AI Gateway — a single custom API endpoint
// that routes every operation internally instead of exposing one OData entity
// per object. The AL side (codeunit "ASG AI Gateway" behind API page
// "ASG AI Gateway API") owns field names and business rules; this client just
// posts {operation, payload} and unwraps the {success, data|error} envelope.
//
// The SDK instantiates tool handlers fresh per call via Activator.CreateInstance
// and offers no DI container, so this client is a process-wide singleton built
// from the environment once at startup (see Program.cs -> BcClient.Configure()).
// A single static HttpClient is reused for the lifetime of the process.
// ---------------------------------------------------------------------------

/// <summary>
/// Thin client for the ASG AI Gateway custom API, authenticated with
/// NavUserPassword basic auth. Resolves the company and gateway-record ids once,
/// then invokes the bound action for each operation.
/// </summary>
internal static class BcClient
{
    private const string GatewayAction = "Microsoft.NAV.executeOperation";

    private static HttpClient? _http;
    private static string _apiBase = "";   // e.g. http://bc-host:7048/BC/api/asg/ai/v1.0
    private static string _company = "";

    // Resolved lazily on first use and cached for the process lifetime.
    private static Guid _companyId = Guid.Empty;
    private static Guid _gatewayId = Guid.Empty;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    // camelCase on the wire: matches the JSON keys the AL gateway reads/writes.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Read and validate BC_* environment variables and build the shared HttpClient.
    /// Call once at startup. Throws <see cref="ConnectorException"/> when a required
    /// variable is missing so the process fails fast before connecting to the hub.
    /// </summary>
    public static void Configure()
    {
        var baseUrl  = Require("BC_BASE_URL");   // e.g. http://bc-host:7048/BC/api/asg/ai/v1.0
        var company  = Require("BC_COMPANY");    // e.g. CRONUS International Ltd.
        var username = Require("BC_USERNAME");
        var password = Require("BC_PASSWORD");

        _apiBase = baseUrl.TrimEnd('/');
        _company = company;

        var timeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("BC_TIMEOUT_SECONDS"), out var t) ? t : 30;

        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{username}:{password}"));
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _http = http;
    }

    /// <summary>
    /// Invoke a gateway operation and deserialize its <c>data</c> object into
    /// <typeparamref name="TResult"/>. The companion of the no-result
    /// <see cref="ExecuteAsync(string, object, string)"/> for the common case.
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        object args,
        string toolKey)
    {
        var data = await ExecuteAsync(operation, args, toolKey);
        var result = data.Deserialize<TResult>(JsonOpts);
        if (result is null)
            throw new ToolValidationException(
                toolKey, $"Business Central returned an empty result for operation '{operation}'.");
        return result;
    }

    /// <summary>
    /// Invoke a gateway operation and return its <c>data</c> element. <paramref name="args"/>
    /// is serialized (camelCase) as the operation payload. Throws
    /// <see cref="ToolValidationException"/> when the gateway reports failure
    /// (<c>success:false</c>) or the call cannot be completed.
    /// </summary>
    public static async Task<JsonElement> ExecuteAsync(
        string operation,
        object args,
        string toolKey)
    {
        var http = Ready(toolKey);
        await EnsureIdsAsync(toolKey);

        // payload is a JSON string parameter on the AL bound action, so the args
        // object is serialized to a string and nested inside the action body.
        var payloadJson = JsonSerializer.Serialize(args ?? new { }, JsonOpts);
        var body = JsonSerializer.Serialize(new { operation, payload = payloadJson });
        var url = $"{_apiBase}/companies({_companyId:D})/aiGateways({_gatewayId:D})/{GatewayAction}";

        using var response = await SendAsync(http, HttpMethod.Post, url, body, toolKey);
        var outer = await ReadJsonAsync(response, toolKey);

        // The bound action returns the envelope as a JSON string in OData's "value".
        if (!outer.TryGetProperty("value", out var valueEl) ||
            valueEl.ValueKind != JsonValueKind.String)
            throw new ToolValidationException(
                toolKey, "Unexpected response from the BC gateway (missing string 'value').");

        using var inner = JsonDocument.Parse(valueEl.GetString()!);
        var root = inner.RootElement;

        var success = root.TryGetProperty("success", out var s) &&
                      s.ValueKind == JsonValueKind.True;
        if (!success)
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            throw new ToolValidationException(
                toolKey,
                string.IsNullOrWhiteSpace(error)
                    ? $"Business Central rejected operation '{operation}'."
                    : error!);
        }

        return root.TryGetProperty("data", out var data) ? data.Clone() : default;
    }

    // ---------------------------------------------------------------------------
    // Id resolution (company + singleton gateway record), cached for the process.
    // ---------------------------------------------------------------------------

    private static async Task EnsureIdsAsync(string toolKey)
    {
        if (_companyId != Guid.Empty && _gatewayId != Guid.Empty)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_companyId == Guid.Empty)
                _companyId = await ResolveCompanyIdAsync(toolKey);
            if (_gatewayId == Guid.Empty)
                _gatewayId = await ResolveGatewayIdAsync(toolKey);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task<Guid> ResolveCompanyIdAsync(string toolKey)
    {
        var http = Ready(toolKey);
        var filter = Uri.EscapeDataString($"name eq '{_company.Replace("'", "''")}'");
        var url = $"{_apiBase}/companies?$filter={filter}&$top=1";

        using var response = await SendAsync(http, HttpMethod.Get, url, body: null, toolKey);
        var json = await ReadJsonAsync(response, toolKey);

        if (TryFirstId(json, out var id))
            return id;

        throw new ToolValidationException(
            toolKey,
            $"No Business Central company named '{_company}' is exposed on the API. " +
            "Check BC_COMPANY and that the company is API-enabled.");
    }

    private static async Task<Guid> ResolveGatewayIdAsync(string toolKey)
    {
        var http = Ready(toolKey);
        var url = $"{_apiBase}/companies({_companyId:D})/aiGateways?$top=1";

        using var response = await SendAsync(http, HttpMethod.Get, url, body: null, toolKey);
        var json = await ReadJsonAsync(response, toolKey);

        if (TryFirstId(json, out var id))
            return id;

        throw new ToolValidationException(
            toolKey,
            "The ASG AI Gateway record was not found in Business Central. Ensure the ASG " +
            "Customization app is installed (its install/upgrade codeunit seeds the record).");
    }

    // Read value[0].id from an OData collection response as a Guid.
    private static bool TryFirstId(JsonElement json, out Guid id)
    {
        id = Guid.Empty;
        return json.TryGetProperty("value", out var arr) &&
               arr.ValueKind == JsonValueKind.Array &&
               arr.GetArrayLength() > 0 &&
               arr[0].TryGetProperty("id", out var idEl) &&
               Guid.TryParse(idEl.GetString(), out id);
    }

    // ---------------------------------------------------------------------------
    // Internal HTTP helpers
    // ---------------------------------------------------------------------------

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpMethod method,
        string url,
        string? body,
        string toolKey)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network failure or timeout — surface as a tool error, not a crash.
            throw new ToolValidationException(
                toolKey,
                $"Could not reach Business Central at {url}: {ex.Message}",
                ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        var detail = await SafeReadAsync(response);
        var statusCode = response.StatusCode;
        response.Dispose();

        var reason = statusCode switch
        {
            HttpStatusCode.Unauthorized => "authentication failed — check BC_USERNAME / BC_PASSWORD",
            HttpStatusCode.NotFound     => "endpoint not found — check BC_BASE_URL and that the ASG AI Gateway API is published",
            _                           => $"HTTP {(int)statusCode} {response.ReasonPhrase}",
        };

        throw new ToolValidationException(
            toolKey,
            $"Business Central request failed ({reason}). {detail}".TrimEnd());
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        string toolKey)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
            return default;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ToolValidationException(
                toolKey, "Business Central returned a non-JSON response.", ex);
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            return text.Length > 500 ? text[..500] : text;
        }
        catch
        {
            return "";
        }
    }

    private static HttpClient Ready(string toolKey) =>
        _http ?? throw new ToolValidationException(
            toolKey, "BC client is not configured — BcClient.Configure() was not called.");

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new ConnectorException(
                $"Missing required environment variable '{name}'. " +
                "See .env.example for the full list of BC_* settings.");
        return value;
    }
}
