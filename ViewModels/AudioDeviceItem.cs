namespace SonarTray.ViewModels;

/// <param name="IsMissing">The channel is routed to this device id but Sonar no longer lists it (unplugged).</param>
public sealed record AudioDeviceItem(string Id, string Name, bool IsMissing = false);
