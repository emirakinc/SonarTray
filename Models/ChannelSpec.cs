namespace SonarTray.Models;

public enum ChannelKind { Master, Game, Chat, Media, Mic, Aux }

/// <summary>
/// Static mapping between a UI row and the Sonar API identifiers.
/// Volume ids (chatRender/chatCapture) differ from redirection ids (chat/mic).
/// </summary>
public sealed record ChannelSpec(
    ChannelKind Kind,
    string Label,
    string VolumeId,
    string? RedirectionId,
    string? DataFlow,
    string Glyph,
    string AccentHex,
    bool Visible)
{
    public static readonly IReadOnlyList<ChannelSpec> All = new[]
    {
        //             Kind                Label       VolumeId       RedirectionId DataFlow   Glyph     Accent     Visible
        new ChannelSpec(ChannelKind.Master, "Ana Ses",  "master",      null,         null,      "", "#6366F1", true),
        new ChannelSpec(ChannelKind.Game,   "Oyun",     "game",        "game",       "render",  "", "#2DD4BF", true),
        new ChannelSpec(ChannelKind.Chat,   "Sohbet",   "chatRender",  "chat",       "render",  "", "#3B82F6", true),
        new ChannelSpec(ChannelKind.Media,  "Medya",    "media",       "media",      "render",  "", "#EC4899", true),
        new ChannelSpec(ChannelKind.Mic,    "Mikrofon", "chatCapture", "mic",        "capture", "", "#F97316", true),
        new ChannelSpec(ChannelKind.Aux,    "Aux",      "aux",         "aux",        "render",  "", "#A78BFA", false),
    };

    public static IEnumerable<ChannelSpec> VisibleSpecs => All.Where(s => s.Visible);
}
