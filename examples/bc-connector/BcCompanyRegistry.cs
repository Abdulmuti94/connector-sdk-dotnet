namespace BcConnector;

/// <summary>
/// Maps country codes to BC company names and optional pre-configured gateway IDs.
/// Loaded once from built-in defaults and the optional <c>BC_COMPANY_MAP</c> env var.
/// </summary>
internal sealed record BcCompanyEntry(string Country, string CompanyName, Guid? GatewayId);

internal static class BcCompanyRegistry
{
    private static readonly BcCompanyEntry[] BuiltInDefaults =
    [
        new("SA", "ASG",       null),
        new("KW", "ASG - KWT", null),
        new("OM", "ASG - OM",  null),
        new("QA", "ASG - QAR", null),
        new("AE", "ASG - UAE", null),
    ];

    private static Dictionary<string, BcCompanyEntry> _byCountry =
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, BcCompanyEntry> _byCompany =
        new(StringComparer.OrdinalIgnoreCase);

    private static string _defaultCountry = "SA";

    /// <summary>
    /// Two-letter country code used when a tool call omits <c>country</c>.
    /// </summary>
    public static string DefaultCountry => _defaultCountry;

    /// <summary>
    /// Comma-separated list of supported country codes (for error messages).
    /// </summary>
    public static string SupportedCountries =>
        string.Join(", ", _byCountry.Keys.OrderBy(c => c));

    public static void Configure(string defaultCompanyName, string? companyMapEnv)
    {
        var entries = new List<BcCompanyEntry>(BuiltInDefaults);

        companyMapEnv = companyMapEnv?.Trim().Trim('"').Trim('\'');

        if (!string.IsNullOrWhiteSpace(companyMapEnv))
        {
            foreach (var segment in companyMapEnv.Split(
                         ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var cleaned = segment.Trim().Trim('"').Trim('\'');
                var parts = cleaned.Split('|', 3);
                if (parts.Length < 2)
                    continue;

                var country = parts[0].Trim().Trim('"').Trim('\'');
                var company = parts[1].Trim().Trim('"').Trim('\'');
                if (string.IsNullOrWhiteSpace(country) || string.IsNullOrWhiteSpace(company))
                    continue;

                Guid? gatewayId = null;
                if (parts.Length >= 3 &&
                    !string.IsNullOrWhiteSpace(parts[2]) &&
                    Guid.TryParse(parts[2].Trim(), out var parsed))
                    gatewayId = parsed;

                var entry = new BcCompanyEntry(country, company, gatewayId);
                var idx = entries.FindIndex(
                    e => string.Equals(e.Country, country, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    entries[idx] = entry;
                else
                    entries.Add(entry);
            }
        }

        RebuildIndexes(entries);

        _defaultCountry = _byCompany.TryGetValue(defaultCompanyName.Trim(), out var match)
            ? match.Country
            : "SA";
    }

    /// <summary>
    /// Limits routing to BC companies the authenticated user can reach via OData.
    /// Returns human-readable labels for countries that were removed.
    /// </summary>
    public static IReadOnlyList<string> RestrictToAccessibleCompanies(
        IReadOnlySet<string> accessibleCompanyNames)
    {
        if (accessibleCompanyNames.Count == 0)
            return Array.Empty<string>();

        var entries = _byCountry.Values.ToList();
        var kept = entries
            .Where(e => accessibleCompanyNames.Contains(e.CompanyName))
            .ToList();
        var removed = entries
            .Where(e => !accessibleCompanyNames.Contains(e.CompanyName))
            .Select(e => $"{e.Country} ({e.CompanyName})")
            .ToList();

        if (kept.Count == 0)
            return removed;

        RebuildIndexes(kept);

        if (_byCountry.TryGetValue(_defaultCountry, out _))
            return removed;

        _defaultCountry = kept[0].Country;
        return removed;
    }

    private static void RebuildIndexes(List<BcCompanyEntry> entries)
    {
        _byCountry = entries.ToDictionary(e => e.Country, e => e, StringComparer.OrdinalIgnoreCase);
        _byCompany = entries
            .GroupBy(e => e.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public static BcCompanyEntry GetDefault() =>
        _byCountry.TryGetValue(_defaultCountry, out var entry) ? entry : BuiltInDefaults[0];

    public static bool TryResolveByCountry(string country, out BcCompanyEntry entry) =>
        _byCountry.TryGetValue(country.Trim(), out entry!);

    public static bool TryResolveByCompany(string companyName, out BcCompanyEntry entry) =>
        _byCompany.TryGetValue(companyName.Trim(), out entry!);

    public static Guid? GetConfiguredGatewayId(string companyName) =>
        TryResolveByCompany(companyName, out var entry) ? entry.GatewayId : null;
}
