namespace SonarTray.ViewModels;

/// <summary>One entry in a channel's audio-profile menu. Sonar calls these "configs".</summary>
public sealed record AudioProfileItem(string Id, string Name);
