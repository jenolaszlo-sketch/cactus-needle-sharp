using System.Globalization;

namespace CactusNeedleSharp;

/// <summary>Carries ambient facts formatted into the system prompt for a session.</summary>
public sealed record NeedleSystemFacts
{
    /// <summary>Gets the reference date and time.</summary>
    public DateTimeOffset? Date { get; init; }
    /// <summary>Gets the locale, such as language or region.</summary>
    public string? Locale { get; init; }
    /// <summary>Gets the device description.</summary>
    public string? Device { get; init; }
    /// <summary>Gets the battery state.</summary>
    public string? Battery { get; init; }
    /// <summary>Gets the network state.</summary>
    public string? Network { get; init; }
    /// <summary>Gets the location.</summary>
    public string? Location { get; init; }
    /// <summary>Gets the user description.</summary>
    public string? User { get; init; }
    /// <summary>Gets the assistant description.</summary>
    public string? Assistant { get; init; }
    /// <summary>Gets additional custom facts appended after the well-known ones.</summary>
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();

    /// <summary>Formats the set facts as a semicolon-separated key-value string.</summary>
    public override string ToString()
    {
        var facts = new List<string>();
        Add("date", Date?.ToString("yyyy-MM-dd ddd HH:mm", CultureInfo.InvariantCulture));
        Add("locale", Locale); Add("device", Device); Add("battery", Battery); Add("network", Network);
        Add("location", Location); Add("user", User); Add("assistant", Assistant);
        foreach (var pair in Raw) Add(pair.Key, pair.Value);
        return string.Join("; ", facts);
        void Add(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) facts.Add($"{key}: {value}"); }
    }
}
