using System.Globalization;

namespace CactusNeedleSharp;

public sealed record NeedleSystemFacts
{
    public DateTimeOffset? Date { get; init; }
    public string? Locale { get; init; }
    public string? Device { get; init; }
    public string? Battery { get; init; }
    public string? Network { get; init; }
    public string? Location { get; init; }
    public string? User { get; init; }
    public string? Assistant { get; init; }
    public IReadOnlyDictionary<string, string> Raw { get; init; } = new Dictionary<string, string>();

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
