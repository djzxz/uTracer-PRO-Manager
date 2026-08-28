namespace uTracerProManager.Services;

/// <summary>
/// Wynik wykrywania wraz z tym samym, nadal otwartym transportem. Dzięki temu
/// przycisk "Znajdź uTracer" nie zwalnia portu i nie wymusza ponownego otwarcia.
/// </summary>
public sealed record ConnectedPortProbeResult(PortProbeResult Probe, SerialTracerTransport Transport);
