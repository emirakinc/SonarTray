namespace SonarTray.Models;

public enum ChannelKind { Master, Game, Chat, Media, Mic, Aux }

/// <summary>
/// Static mapping between a UI row and the Sonar API identifiers.
/// Volume ids (chatRender/chatCapture) differ from redirection ids (chat/mic).
///
/// <paramref name="LabelKey"/> is a resource key rather than a display string: this table is a
/// static initialiser, so baking the text in would capture whatever culture happened to be active
/// the first time the type was touched.
/// </summary>
public sealed record ChannelSpec(
    ChannelKind Kind,
    string LabelKey,
    string VolumeId,
    string? RedirectionId,
    string? DataFlow,
    string Glyph,
    string AccentHex,
    bool Visible)
{
    public static readonly IReadOnlyList<ChannelSpec> All = new[]
    {
        //             Kind                LabelKey            VolumeId       RedirectionId DataFlow   Glyph     Accent     Visible
        new ChannelSpec(ChannelKind.Master, "Channel_Master",   "master",      null,         null,      "", "#6366F1", true),
        new ChannelSpec(ChannelKind.Game,   "Channel_Game",     "game",        "game",       "render",  "", "#2DD4BF", true),
        new ChannelSpec(ChannelKind.Chat,   "Channel_Chat",     "chatRender",  "chat",       "render",  "", "#3B82F6", true),
        new ChannelSpec(ChannelKind.Media,  "Channel_Media",    "media",       "media",      "render",  "", "#EC4899", true),
        new ChannelSpec(ChannelKind.Mic,    "Channel_Mic",      "chatCapture", "mic",        "capture", "", "#F97316", true),
        new ChannelSpec(ChannelKind.Aux,    "Channel_Aux",      "aux",         "aux",        "render",  "", "#A78BFA", false),
    };

    public static IEnumerable<ChannelSpec> VisibleSpecs => All.Where(s => s.Visible);
}
