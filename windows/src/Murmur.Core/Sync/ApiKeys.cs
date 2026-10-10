namespace Murmur.Core.Sync;

/// <summary>
/// Which key each AI client is given, read afresh on every call: the one typed in Settings if
/// there is one, else the one from Sidgrove Intelligence, else null, which each client reads
/// as "use my environment variable" (docs/sync.md, "Keys").
/// </summary>
/// <remarks>
/// Nothing is cached, so a key that arrives from a sign-in or a refresh, or is pasted into
/// Settings, is the one the next dictation uses with no restart.
/// </remarks>
/// <param name="settings">The settings, for the keys typed in by hand.</param>
/// <param name="managed">The keys from Sidgrove Intelligence.</param>
public sealed class ApiKeys(AppSettings settings, ManagedKeys managed)
{
    /// <summary>The Gemini key: clean-up, and cloud hearing when Gemini is the provider.</summary>
    public string? Gemini() => ManagedKeys.Prefer(settings.Data.GeminiApiKey, managed.Gemini);

    /// <summary>The ElevenLabs key: cloud hearing.</summary>
    public string? ElevenLabs() => ManagedKeys.Prefer(settings.Data.ElevenLabsApiKey, managed.ElevenLabs);

    /// <summary>The Anthropic key: clean-up with a Claude model.</summary>
    public string? Anthropic() => ManagedKeys.Prefer(settings.Data.AnthropicApiKey, managed.Anthropic);

    /// <summary>The Vercel AI Gateway key: Jev.</summary>
    public string? Jev() => ManagedKeys.Prefer(settings.Data.JevApiKey, managed.AiGateway);
}
