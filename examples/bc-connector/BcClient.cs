using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VestedAI.ConnectorSdk.Errors;
using VestedAI.ConnectorSdk.Tool;

namespace BcConnector;

// ---------------------------------------------------------------------------
// Business Central client for the ASG AI Gateway — a single endpoint that routes
// every operation internally instead of exposing one OData entity per object.
// The AL side (codeunit "ASG AI Gateway" behind API page "ASG AI Gateway API")
// owns field names and business rules; this client just posts {operation, payload}
// and unwraps the {success, data|error} envelope.
//
// Transport is the ODataV4 web-service endpoint (.../ODataV4), where the company
// is addressed by name — Company('ASG') — and the gateway page is exposed as the
// 'aiGateway' entity. (The /api/ namespace is an alternative, but requires API
// Services to be enabled on the BC service instance; ODataV4 only needs OData
// Services.) The bound action is NAV.executeOperation.
//
// The SDK instantiates tool handlers fresh per call via Activator.CreateInstance
// and offers no DI container, so this client is a process-wide singleton built
// from the environment once at startup (see Program.cs -> BcClient.Configure()).
// A single static HttpClient is reused for the lifetime of the process.
//
// AUTHENTICATION IS PER CALLER. The shared HttpClient carries NO default
// Authorization header: every request sets its own from the credential on the
// invocation's ToolContext (see BcCredentials). Sharing the client is still
// correct and desirable — it preserves connection pooling — because
// NavUserPassword is HTTP Basic, a stateless per-request header with no
// connection affinity. That assumption is load-bearing: if this environment is
// ever switched to NTLM or Negotiate, authentication binds to the *connection*,
// and a pooled connection would carry one user's identity into another user's
// request. Such a switch requires per-user HttpClients (or SocketsHttpHandler
// pool partitioning), not just a different header.
// ---------------------------------------------------------------------------

/// <summary>
/// Thin client for the ASG AI Gateway over ODataV4, authenticated per caller
/// with NavUserPassword basic auth. Each call may target a specific BC company
/// (tools expose an optional <c>country</c> arg); when none is given the
/// default from <see cref="BcCompanyRegistry"/> / BC_COMPANY is used.
/// The gateway-record id is taken from BC_COMPANY_MAP when configured,
/// otherwise resolved via OData and cached per company.
/// </summary>
internal static class BcClient
{
    private const string GatewayAction = "NAV.executeOperation";
    private const string GatewayEntity = "aiGateway";

    private static HttpClient? _http;
    private static string _apiBase = "";          // e.g. https://bc-host:8048/BC/ODataV4
    private static string _defaultCompany = "";   // from BC_COMPANY, used when routing falls back

    // Gateway record id per company, resolved lazily and cached for the process lifetime.
    private static readonly ConcurrentDictionary<string, Guid> _gatewayIds = new();
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    // camelCase on the wire: matches the JSON keys the AL gateway reads/writes.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static JsonSerializerOptions JsonOptions => JsonOpts;

    /// <summary>True once <see cref="Configure"/> has run and the endpoint is known.</summary>
    public static bool IsConfigured => _http is not null;

    /// <summary>
    /// Read and validate BC_* environment variables and build the shared HttpClient.
    /// Call once at startup. Throws <see cref="ConnectorException"/> when a required
    /// variable is missing so the process fails fast before connecting to the hub.
    ///
    /// No credentials are read here. Every request authenticates as its own caller
    /// from the sealed per-user credential on the ToolContext; there is no shared
    /// service account to configure.
    /// </summary>
    public static void Configure()
    {
        var baseUrl = Require("BC_BASE_URL");   // e.g. https://bc-host:8048/BC/ODataV4
        var company = Require("BC_COMPANY");    // default company, e.g. ASG

        _apiBase = baseUrl.TrimEnd('/');
        _defaultCompany = company;

        BcCompanyRegistry.Configure(
            company,
            Environment.GetEnvironmentVariable("BC_COMPANY_MAP"));

        // Must be >= the largest DefaultDeadlineMs any tool declares, or that tool's
        // deadline is fiction: this timeout aborts the call first and the hub reports a
        // connector error at ~30s. net_inventory / available_inventory declare 120s
        // (a single-branch, all-items aggregate runs 15-30s), so the floor is 120.
        // Tools with a shorter deadline are still bounded by the hub, not by this.
        var timeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("BC_TIMEOUT_SECONDS"), out var t) ? t : 120;

        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        // Deliberately no DefaultRequestHeaders.Authorization — see the header
        // comment. Authorization is set per request, from the calling user.
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _http = http;

        // The company routing table is NOT narrowed at startup any more. It used
        // to be filtered to whatever the one service account could reach, which
        // under per-user auth would apply one user's access to everyone. Company
        // access now differs per caller and BC is the authority: an unreachable
        // company comes back as its own "Access is denied to company" error,
        // which SendAsync surfaces verbatim.
        Console.WriteLine(
            $"[bc] Per-user credentials required. Endpoint {_apiBase}, " +
            $"default company '{_defaultCompany}', countries: {BcCompanyRegistry.SupportedCountries}.");
    }

    /// <summary>
    /// List the BC companies a given sign-in can reach. Used by
    /// <see cref="BcUserCredentialHandler"/> to validate a credential before the
    /// platform stores it — this is the "does this actually work?" check that
    /// only Business Central can answer.
    /// </summary>
    /// <returns>
    /// The HTTP status, the company names (empty unless the status is 200), and
    /// a transport-error description when the request could not be completed
    /// at all (in which case the status is meaningless).
    /// </returns>
    public static async Task<(HttpStatusCode Status, IReadOnlyList<string> Companies, string? TransportError)>
        ProbeCompaniesAsync(AuthenticationHeaderValue auth)
    {
        var http = _http;
        if (http is null)
            return (HttpStatusCode.ServiceUnavailable, Array.Empty<string>(), "connector not configured");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBase}/Company");
        request.Headers.Authorization = auth;

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (HttpStatusCode.ServiceUnavailable, Array.Empty<string>(), ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return (response.StatusCode, Array.Empty<string>(), null);

            var names = new List<string>();
            try
            {
                var text = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("value", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                        if (item.TryGetProperty("Name", out var nameEl) &&
                            nameEl.GetString() is { Length: > 0 } name)
                            names.Add(name);
                }
            }
            catch (JsonException ex)
            {
                return (response.StatusCode, Array.Empty<string>(), $"unreadable response: {ex.Message}");
            }

            return (response.StatusCode, names, null);
        }
    }

    /// <summary>
    /// Invoke a gateway operation and deserialize its <c>data</c> object into
    /// <typeparamref name="TResult"/>. The companion of the no-result
    /// <see cref="ExecuteAsync(string, object, string)"/> for the common case.
    /// </summary>
    public static async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        object args,
        string toolKey,
        ToolContext ctx)
    {
        var data = await ExecuteAsync(operation, args, toolKey, ctx);
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
        string toolKey,
        ToolContext ctx)
    {
        var http = Ready(toolKey);

        // Resolved once per call, up front: a missing credential must fail before
        // any work is done, and every request below carries this same caller.
        var credential = BcCredentials.For(ctx, toolKey);

        var (company, payloadJson) = await BuildPayloadAsync(args, toolKey, ctx, resolveStores: true, operation);
        var gatewayId = await ResolveGatewayIdAsync(company, toolKey, credential);

        // callerContext carries the authenticated caller identity (from ToolContext,
        // not from args) in its own action field, kept separate from the payload so
        // model-supplied args can never reach or spoof it.
        var callerContext = BcCallerContext.Serialize(ctx);

        // payload is a JSON string parameter on the AL bound action, so the args
        // object is serialized to a string and nested inside the action body.
        var body = JsonSerializer.Serialize(new { operation, payload = payloadJson, callerContext });
        var url = $"{_apiBase}/{CompanySegment(company)}/{GatewayEntity}({gatewayId:D})/{GatewayAction}";

        using var response = await SendAsync(http, HttpMethod.Post, url, body, toolKey, credential);
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
            throw new ToolValidationException(toolKey, DescribeGatewayError(operation, error));
        }

        return root.TryGetProperty("data", out var data) ? data.Clone() : default;
    }

    /// <summary>
    /// Gateway call used internally (e.g. store resolution) without re-entering store lookup.
    /// </summary>
    internal static async Task<JsonElement> ExecuteGatewayAsync(
        string operation,
        object args,
        string company,
        string toolKey,
        ToolContext ctx)
    {
        var http = Ready(toolKey);
        var credential = BcCredentials.For(ctx, toolKey);
        var (_, payloadJson) = await BuildPayloadAsync(
            args, toolKey, ctx, resolveStores: false, operation, companyOverride: company);
        var gatewayId = await ResolveGatewayIdAsync(company, toolKey, credential);
        var callerContext = BcCallerContext.Serialize(ctx);
        var body = JsonSerializer.Serialize(new { operation, payload = payloadJson, callerContext });
        var url = $"{_apiBase}/{CompanySegment(company)}/{GatewayEntity}({gatewayId:D})/{GatewayAction}";

        using var response = await SendAsync(http, HttpMethod.Post, url, body, toolKey, credential);
        var outer = await ReadJsonAsync(response, toolKey);

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
            throw new ToolValidationException(toolKey, DescribeGatewayError(operation, error));
        }

        return root.TryGetProperty("data", out var data) ? data.Clone() : default;
    }

    private static async Task<(string company, string payloadJson)> BuildPayloadAsync(
        object args,
        string toolKey,
        ToolContext ctx,
        bool resolveStores,
        string operation,
        string? companyOverride = null)
    {
        var node = JsonSerializer.SerializeToNode(args ?? new { }, JsonOpts)?.AsObject()
                   ?? new JsonObject();

        var company = companyOverride ?? ResolveCompanyFromRouting(node, toolKey);

        if (resolveStores &&
            !operation.Equals("FindStores", StringComparison.OrdinalIgnoreCase))
        {
            await BcStoreResolver.ResolveInPayloadAsync(node, company, toolKey, ctx);
        }

        RemoveRoutingKey(node, "country");
        RemoveRoutingKey(node, "company");

        return (company, node.ToJsonString(JsonOpts));
    }

    // Turn a raw gateway error into an actionable tool message. When the gateway
    // does not implement the requested operation, the failure is permanent for this
    // environment (the AL app would need to be updated), so we say so plainly and
    // tell the caller not to retry — otherwise the model burns repeated calls (and
    // eventually a timeout) re-invoking a tool that can never succeed here.
    private static string DescribeGatewayError(string operation, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return $"Business Central rejected operation '{operation}'.";

        var trimmed = error.Trim();
        if (trimmed.Contains("Unknown operation", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("not implemented", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("not supported", StringComparison.OrdinalIgnoreCase))
        {
            return $"This capability is not available in the connected Business Central " +
                   $"environment — the gateway does not implement operation '{operation}'. " +
                   $"This is a permanent limitation, not a transient error: do not retry. " +
                   $"Tell the user the requested lookup is not supported here. " +
                   $"(Gateway said: {trimmed})";
        }

        return trimmed;
    }

    // ---------------------------------------------------------------------------
    // Company routing + id resolution. The company is addressed by name in the URL;
    // the singleton gateway-record id is resolved once per company and cached.
    // ---------------------------------------------------------------------------

    private static string ResolveCompanyFromRouting(JsonObject node, string toolKey)
    {
        var inferredCountry = BcCountryInference.InferFromPayload(node);

        var countryKey = node.FirstOrDefault(
            p => string.Equals(p.Key, "country", StringComparison.OrdinalIgnoreCase)).Key;
        if (countryKey is not null)
        {
            var country = node[countryKey]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(country))
            {
                if (BcCountryInference.ShouldOverrideExplicitCountry(country, inferredCountry))
                {
                    Console.WriteLine(
                        $"[bc] Routing {toolKey}: country '{country}' overridden to " +
                        $"'{inferredCountry}' from entity number.");
                    country = inferredCountry!;
                }

                if (BcCompanyRegistry.TryResolveByCountry(country, out var entry))
                    return entry.CompanyName;

                throw new ToolValidationException(
                    toolKey,
                    $"Unknown country '{country}'. Supported codes: {BcCompanyRegistry.SupportedCountries}.");
            }
        }

        var companyKey = node.FirstOrDefault(
            p => string.Equals(p.Key, "company", StringComparison.OrdinalIgnoreCase)).Key;
        if (companyKey is not null)
        {
            var legacyCompany = node[companyKey]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(legacyCompany))
            {
                if (BcCompanyRegistry.TryResolveByCompany(legacyCompany, out var entry))
                    return entry.CompanyName;
                return legacyCompany;
            }
        }

        if (inferredCountry is not null &&
            BcCompanyRegistry.TryResolveByCountry(inferredCountry, out var inferredEntry))
            return inferredEntry.CompanyName;

        if (BcCompanyRegistry.TryResolveByCompany(_defaultCompany, out var defaultEntry))
            return defaultEntry.CompanyName;

        return _defaultCompany;
    }

    private static void RemoveRoutingKey(JsonObject node, string name)
    {
        var key = node.FirstOrDefault(
            p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Key;
        if (key is not null)
            node.Remove(key);
    }

    // The gateway record id is a per-company singleton seeded by the AL app's
    // install/upgrade codeunit — identical for every user and not user data — so
    // the cache is shared across callers. Only the lookup that fills it runs as
    // the caller, which is why the credential is a parameter here.
    private static async Task<Guid> ResolveGatewayIdAsync(
        string company, string toolKey, BcCredential credential)
    {
        if (_gatewayIds.TryGetValue(company, out var cached))
            return cached;

        var configured = BcCompanyRegistry.GetConfiguredGatewayId(company);
        if (configured is Guid preconfigured)
        {
            _gatewayIds[company] = preconfigured;
            return preconfigured;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_gatewayIds.TryGetValue(company, out cached))
                return cached;

            configured = BcCompanyRegistry.GetConfiguredGatewayId(company);
            if (configured is Guid lockedPreconfigured)
            {
                _gatewayIds[company] = lockedPreconfigured;
                return lockedPreconfigured;
            }

            var http = Ready(toolKey);
            var url = $"{_apiBase}/{CompanySegment(company)}/{GatewayEntity}?$top=1";

            using var response = await SendAsync(http, HttpMethod.Get, url, body: null, toolKey, credential);
            var json = await ReadJsonAsync(response, toolKey);

            if (TryFirstId(json, out var id))
            {
                _gatewayIds[company] = id;
                return id;
            }

            throw new ToolValidationException(
                toolKey,
                $"The ASG AI Gateway record was not found in company '{company}'. Ensure the " +
                "ASG Customization app is installed there (its install/upgrade codeunit seeds " +
                "the record), and that the company name is exposed over ODataV4.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    // OData company key segment, addressing the company by name: Company('ASG').
    // The name is percent-encoded; an embedded apostrophe is OData-escaped ('').
    private static string CompanySegment(string company) =>
        $"Company('{Uri.EscapeDataString(company.Replace("'", "''"))}')";

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
        string toolKey,
        BcCredential credential)
    {
        using var request = new HttpRequestMessage(method, url);

        // Per request, never on the shared client: this is what keeps concurrent
        // callers isolated on one pooled HttpClient.
        request.Headers.Authorization = credential.Header;

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

        var bcMessage = TryExtractBcErrorMessage(detail);
        var reason = statusCode switch
        {
            HttpStatusCode.Unauthorized => "Business Central rejected your sign-in — your stored " +
                                           "Business Central user name or Web Service Access Key is " +
                                           "wrong or has been regenerated. Update it in your " +
                                           "integration settings",
            HttpStatusCode.Forbidden    => "Business Central refused this operation for your user — " +
                                           "your BC permissions do not allow it. Ask your BC " +
                                           "administrator if you need access",
            HttpStatusCode.NotFound     => "endpoint not found — check BC_BASE_URL (.../ODataV4) and that OData Services are enabled on the BC instance",
            HttpStatusCode.ServiceUnavailable => "service unavailable — the BC ODataV4 endpoint is not responding (OData Services may be disabled or the instance is restarting)",
            HttpStatusCode.BadRequest when bcMessage.Contains("Access is denied to company", StringComparison.OrdinalIgnoreCase)
                => bcMessage.TrimEnd('.'),
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

    private static string TryExtractBcErrorMessage(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(detail);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString() ?? "";
        }
        catch (JsonException)
        {
        }

        return "";
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
