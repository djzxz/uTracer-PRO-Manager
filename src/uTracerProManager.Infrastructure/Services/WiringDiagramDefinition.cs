using System.Collections.Generic;

namespace uTracerProManager.Services;

public sealed record WiringDiagramDefinition(string ConfirmationKey, string TubeType, string Title, string Subtitle, IReadOnlyList<WiringLink> Links, IReadOnlyList<int> HeaterPins, string HeaterInstruction, string PinoutText, string SafetyNote, bool IsExactLayout);
