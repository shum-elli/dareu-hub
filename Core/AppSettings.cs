namespace DareuLocal.Core;

public sealed record AppProfileRule(string ProcessName, string FamilyKey, int Profile);

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; }
    public bool LowBatteryAlertEnabled { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 15;
    public bool OsdEnabled { get; set; } = true;
    public bool AutoSwitchEnabled { get; set; }
    public Dictionary<string, Dictionary<int, string>> ProfileAliases { get; set; } = new();
    public Dictionary<string, int> DefaultProfiles { get; set; } = new();
    public List<AppProfileRule> AppRules { get; set; } = new();

    public static string NormalizeProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        string trimmed = name.Trim();
        int slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
            trimmed = trimmed[(slash + 1)..];
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        return trimmed.Trim().ToLowerInvariant();
    }

    public string GetProfileDisplayName(string? familyKey, int profile)
    {
        if (!string.IsNullOrWhiteSpace(familyKey) &&
            ProfileAliases.TryGetValue(familyKey, out var map) &&
            map.TryGetValue(profile, out var alias) &&
            !string.IsNullOrWhiteSpace(alias))
        {
            return $"Profile {profile} · {alias.Trim()}";
        }
        return $"Profile {profile}";
    }

    public void SetProfileAlias(string familyKey, int profile, string? alias)
    {
        if (string.IsNullOrWhiteSpace(familyKey) || profile is < 1 or > 4)
            return;
        string trimmed = alias?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            if (ProfileAliases.TryGetValue(familyKey, out var existing))
            {
                existing.Remove(profile);
                if (existing.Count == 0)
                    ProfileAliases.Remove(familyKey);
            }
            return;
        }
        if (!ProfileAliases.TryGetValue(familyKey, out var map))
        {
            map = new Dictionary<int, string>();
            ProfileAliases[familyKey] = map;
        }
        map[profile] = trimmed;
    }

    public AppProfileRule? MatchRule(string processName, string familyKey)
    {
        if (string.IsNullOrWhiteSpace(familyKey))
            return null;
        string normalized = NormalizeProcessName(processName);
        if (normalized.Length == 0)
            return null;
        return AppRules.FirstOrDefault(r =>
            r.FamilyKey == familyKey &&
            r.Profile is >= 1 and <= 4 &&
            NormalizeProcessName(r.ProcessName) == normalized);
    }

    public void Normalize()
    {
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 5, 50);

        ProfileAliases ??= new();
        var cleanedAliases = new Dictionary<string, Dictionary<int, string>>();
        foreach (var (family, map) in ProfileAliases)
        {
            if (string.IsNullOrWhiteSpace(family) || map == null)
                continue;
            var entries = new Dictionary<int, string>();
            foreach (var (profile, alias) in map)
            {
                if (profile is >= 1 and <= 4 && !string.IsNullOrWhiteSpace(alias))
                    entries[profile] = alias.Trim();
            }
            if (entries.Count > 0)
                cleanedAliases[family.Trim()] = entries;
        }
        ProfileAliases = cleanedAliases;

        DefaultProfiles ??= new();
        var cleanedDefaults = new Dictionary<string, int>();
        foreach (var (family, profile) in DefaultProfiles)
        {
            if (!string.IsNullOrWhiteSpace(family) && profile is >= 1 and <= 4)
                cleanedDefaults[family.Trim()] = profile;
        }
        DefaultProfiles = cleanedDefaults;

        AppRules ??= new();
        var seen = new HashSet<(string Family, string Proc)>();
        var cleanedRules = new List<AppProfileRule>();
        foreach (var rule in AppRules)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.FamilyKey) || rule.Profile is < 1 or > 4)
                continue;
            string proc = NormalizeProcessName(rule.ProcessName);
            string family = rule.FamilyKey.Trim();
            if (proc.Length == 0)
                continue;
            if (seen.Add((family, proc)))
                cleanedRules.Add(new AppProfileRule(proc, family, rule.Profile));
        }
        AppRules = cleanedRules;
    }
}
