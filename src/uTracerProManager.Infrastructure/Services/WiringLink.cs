namespace uTracerProManager.Services;

public sealed record WiringLink(string Id, string From, string To, string ColorHex, string Description, WiringLinkKind Kind = WiringLinkKind.Cable);
