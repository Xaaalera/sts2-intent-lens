using System.Resources;
using System.Text.Json;

namespace IntentLens;

public static class TextCatalog
{
    private static readonly IReadOnlyDictionary<string, string> English;
    public static Func<string, string, string>? Localize { private get; set; }

    static TextCatalog()
    {
        using var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("IntentLens.Localization.eng.json")
            ?? throw new MissingManifestResourceException("IntentLens.Localization.eng.json");
        English = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new MissingManifestResourceException("IntentLens.Localization.eng.json");
    }

    public static string Get(string key)
    {
        if (!English.TryGetValue(key, out var fallback)) { throw new MissingManifestResourceException(key); }
        return Localize?.Invoke(key, fallback) ?? fallback;
    }
}
