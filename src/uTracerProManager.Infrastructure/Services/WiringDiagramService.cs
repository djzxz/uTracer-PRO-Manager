using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public static class WiringDiagramService
{
	public static WiringDiagramDefinition Create(TubeProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		string primaryTubeType = GetPrimaryTubeType(profile);
		string text = primaryTubeType.ToUpperInvariant();
		if (profile.IsDualTriode)
		{
			return CreateDualTriode(profile, primaryTubeType, text);
		}
		if (text.Contains("EL34") || profile.TubeTypes.Contains("6CA7", StringComparison.OrdinalIgnoreCase))
		{
			return CreateEl34(profile, primaryTubeType);
		}
		if (text.Contains("EF86") || text.Contains("EF806") || profile.TubeTypes.Contains("6267", StringComparison.OrdinalIgnoreCase))
		{
			return CreateEf86(profile, primaryTubeType);
		}
		return CreateGeneric(profile, primaryTubeType);
	}

	public static string GetPrimaryTubeType(TubeProfile profile)
	{
		string text = (string.IsNullOrWhiteSpace(profile.TubeTypes) ? profile.DisplayName : profile.TubeTypes).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? profile.DisplayName;
		int num = text.IndexOf('—');
		if (num >= 0)
		{
			text = text.Substring(0, num);
		}
		return text.Trim();
	}

	private static WiringDiagramDefinition CreateDualTriode(TubeProfile profile, string tubeType, string normalized)
	{
		if (normalized.Contains("6SL7") || normalized.Contains("6SN7") || normalized.Contains("5691") || normalized.Contains("5692") || profile.TubeTypes.Contains("6SL7", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6SN7", StringComparison.OrdinalIgnoreCase))
		{
			return CreateOctalDualTriode(profile, tubeType);
		}
		bool flag = normalized.Contains("ECC88") || normalized.Contains("E88CC") || normalized.Contains("E188CC") || normalized.Contains("PCC88") || normalized.Contains("6CG7") || normalized.Contains("6FQ7") || normalized.Contains("6N2P") || normalized.Contains("6H2P") || profile.TubeTypes.Contains("6DJ8", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6922", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("7308", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6CG7", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6FQ7", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6N2", StringComparison.OrdinalIgnoreCase) || profile.TubeTypes.Contains("6H2", StringComparison.OrdinalIgnoreCase);
		List<WiringLink> list = new List<WiringLink>
		{
			new WiringLink("A-1", "A", "1", "#C62828", "Czerwony przewód: A → pin 1 (anoda połówki A)"),
			new WiringLink("G-2", "G", "2", "#2E7D32", "Zielony przewód: G → pin 2 (siatka połówki A)"),
			new WiringLink("C-3", "C", "3", "#F9A825", "Żółty przewód: C → pin 3 (katoda połówki A)"),
			new WiringLink("S-6", "S", "6", "#1565C0", "Niebieski przewód: S → pin 6 (druga anoda / połówka B)"),
			new WiringLink("2-7", "2", "7", "#2E7D32", "Zielony mostek: pin 2 ↔ pin 7 (wspólne sterowanie siatek)", WiringLinkKind.Bridge),
			new WiringLink("3-8", "3", "8", "#F9A825", "Żółty mostek: pin 3 ↔ pin 8 (wspólny powrót katod)", WiringLinkKind.Bridge)
		};
		IReadOnlyList<int> heaterPins;
		string heaterInstruction;
		if (flag)
		{
			heaterPins = new int[2] { 4, 5 };
			heaterInstruction = "Żarzenie: wyłącznie piny 4 i 5. Nie zwieraj ich ze sobą. Pin 9 jest ekranem wewnętrznym, a nie odczepem grzejnika.";
		}
		else
		{
			list.Add(new WiringLink("4-5", "4", "5", "#202124", "Czarny mostek: pin 4 ↔ pin 5 (połączenie połówek grzejnika dla 6,3 V)", WiringLinkKind.Bridge));
			heaterPins = new int[3] { 4, 5, 9 };
			heaterInstruction = "Żarzenie 6,3 V: zewrzyj piny 4 i 5. Jeden koniec żarzenia podłącz do 4+5, drugi do pinu 9. NIE zwieraj 4+5 z pinem 9.";
		}
		return new WiringDiagramDefinition(tubeType + "|DUAL|" + (flag ? "45" : "45-9"), tubeType, tubeType + " — obie połówki jednocześnie", "Va/Ia mierzy połówkę A, a Vs/Is mierzy połówkę B. Adapter nie jest wymagany.", list, heaterPins, heaterInstruction, profile.Pinout, "Przed potwierdzeniem porównaj każdą wtyczkę z panelem testera. Test nie uruchomi się bez zatwierdzenia schematu.", IsExactLayout: true);
	}

	private static WiringDiagramDefinition CreateOctalDualTriode(TubeProfile profile, string tubeType)
	{
		List<WiringLink> links = new List<WiringLink>
		{
			new WiringLink("A-2", "A", "2", "#C62828", "Czerwony przewód: A → pin 2 (anoda połówki A)"),
			new WiringLink("G-1", "G", "1", "#2E7D32", "Zielony przewód: G → pin 1 (siatka połówki A)"),
			new WiringLink("C-3", "C", "3", "#F9A825", "Żółty przewód: C → pin 3 (katoda połówki A)"),
			new WiringLink("S-5", "S", "5", "#1565C0", "Niebieski przewód: S → pin 5 (anoda połówki B)"),
			new WiringLink("1-4", "1", "4", "#2E7D32", "Zielony mostek: pin 1 ↔ pin 4 (wspólne sterowanie siatek)", WiringLinkKind.Bridge),
			new WiringLink("3-6", "3", "6", "#F9A825", "Żółty mostek: pin 3 ↔ pin 6 (wspólny powrót katod)", WiringLinkKind.Bridge)
		};
		return new WiringDiagramDefinition(tubeType + "|DUAL-OCTAL|78", tubeType, tubeType + " — obie połówki jednocześnie", "Va/Ia mierzy połówkę A, a Vs/Is mierzy połówkę B. Układ dotyczy podwójnych triod oktalowych.", links, new int[2] { 7, 8 }, "Żarzenie: piny 7 i 8. Nie zwieraj ich ze sobą.", profile.Pinout, "Nie stosuj schematu rodziny ECC. Przed testem porównaj numery wszystkich sześciu elektrod.", IsExactLayout: true);
	}

	private static WiringDiagramDefinition CreateEl34(TubeProfile profile, string tubeType)
	{
		List<WiringLink> links = new List<WiringLink>
		{
			new WiringLink("A-3", "A", "3", "#C62828", "Czerwony przewód: A → pin 3 (anoda)"),
			new WiringLink("G-5", "G", "5", "#2E7D32", "Zielony przewód: G → pin 5 (g1)"),
			new WiringLink("C-8", "C", "8", "#F9A825", "Żółty przewód: C → pin 8 (katoda)"),
			new WiringLink("S-4", "S", "4", "#1565C0", "Niebieski przewód: S → pin 4 (g2)"),
			new WiringLink("1-8", "1", "8", "#202124", "Czarny mostek: pin 1 (g3) ↔ pin 8 (katoda)", WiringLinkKind.Bridge)
		};
		return new WiringDiagramDefinition(tubeType + "|EL34", tubeType, tubeType + " — podłączenie pentody", "A = anoda, S = siatka druga, G = siatka sterująca, C = katoda.", links, new int[2] { 2, 7 }, "Żarzenie: piny 2 i 7. Nie zwieraj ich ze sobą.", profile.Pinout, "Zweryfikuj limit prądu i napięcia przed uruchomieniem lampy mocy.", IsExactLayout: true);
	}

	private static WiringDiagramDefinition CreateEf86(TubeProfile profile, string tubeType)
	{
		List<WiringLink> links = new List<WiringLink>
		{
			new WiringLink("A-6", "A", "6", "#C62828", "Czerwony przewód: A → pin 6 (anoda)"),
			new WiringLink("G-9", "G", "9", "#2E7D32", "Zielony przewód: G → pin 9 (g1)"),
			new WiringLink("C-3", "C", "3", "#F9A825", "Żółty przewód: C → pin 3 (katoda)"),
			new WiringLink("S-1", "S", "1", "#1565C0", "Niebieski przewód: S → pin 1 (g2)"),
			new WiringLink("3-8", "3", "8", "#202124", "Mostek: pin 3 (katoda) ↔ pin 8 (g3)", WiringLinkKind.Bridge),
			new WiringLink("2-3", "2", "3", "#6D6D6D", "Ekran pin 2 → katoda pin 3", WiringLinkKind.Bridge),
			new WiringLink("7-3", "7", "3", "#6D6D6D", "Ekran pin 7 → katoda pin 3", WiringLinkKind.Bridge)
		};
		return new WiringDiagramDefinition(tubeType + "|EF86", tubeType, tubeType + " — podłączenie pentody", "Połączenie wygenerowane z pinoutu profilu pomiarowego.", links, new int[2] { 4, 5 }, "Żarzenie: piny 4 i 5. Nie zwieraj ich ze sobą.", profile.Pinout, "Przed testem porównaj ekranowanie pinów 2 i 7 z kartą konkretnej lampy.", IsExactLayout: true);
	}

	private static WiringDiagramDefinition CreateGeneric(TubeProfile profile, string tubeType)
	{
		List<WiringLink> list = new List<WiringLink>();
		string? text = AddParsedLink(list, profile.Pinout, "anoda", "A", "#C62828", "anoda");
		string text2 = AddParsedLink(list, profile.Pinout, "g1", "G", "#2E7D32", "siatka sterująca g1");
		string text3 = AddParsedLink(list, profile.Pinout, "katoda", "C", "#F9A825", "katoda");
		string text4 = AddParsedLink(list, profile.Pinout, "g2", "S", "#1565C0", "siatka druga g2");
		string text5 = FindPin(profile.Pinout, "g3");
		if (text5 != null && text3 != null && text5 != text3)
		{
			list.Add(new WiringLink(text5 + "-" + text3 + "-g3", text5, text3, "#202124", $"Mostek: pin {text5} (g3) ↔ pin {text3} (katoda)", WiringLinkKind.Bridge));
		}
		string text6 = FindPin(profile.Pinout, "ekran");
		if (text6 != null && text3 != null && text6 != text3)
		{
			list.Add(new WiringLink(text6 + "-" + text3 + "-shield", text6, text3, "#6D6D6D", $"Mostek: pin {text6} (ekran) ↔ pin {text3} (katoda)", WiringLinkKind.Bridge));
		}
		IReadOnlyList<int> readOnlyList = ParseHeaterPins(profile.Pinout);
		bool flag = text != null && text2 != null && text3 != null && (profile.ScreenVoltage <= 0.0 || text4 != null) && readOnlyList.Count == 2;
		string heaterInstruction = ((readOnlyList.Count == 2) ? $"Żarzenie: piny {readOnlyList[0]} i {readOnlyList[1]}. Nie zwieraj ich ze sobą." : "Żarzenie i dodatkowe elektrody podłącz dokładnie według tekstu pinoutu poniżej.");
		return new WiringDiagramDefinition(tubeType + "|GENERIC|" + profile.Pinout, tubeType, tubeType + " — " + (flag ? "zweryfikowane połączenie" : "podgląd pinoutu"), flag ? "Schemat wygenerowano ze strukturalnego pinoutu zweryfikowanego profilu." : "Dla tego profilu wyświetlany jest schemat pomocniczy wygenerowany z opisu pinów.", list, readOnlyList, heaterInstruction, profile.Pinout, flag ? "Przed potwierdzeniem porównaj każdą wtyczkę z panelem testera i tekstem pinoutu." : "Schemat ogólny wymaga ręcznego porównania z kartą katalogową. Nie zatwierdzaj, jeżeli jakiekolwiek połączenie jest niejasne.", flag);
	}

	private static string? AddParsedLink(ICollection<WiringLink> links, string pinout, string token, string terminal, string color, string description)
	{
		string text = FindPin(pinout, token);
		if (text == null)
		{
			return null;
		}
		links.Add(new WiringLink(terminal + "-" + text, terminal, text, color, $"{terminal} → pin {text} ({description})"));
		return text;
	}

	private static string? FindPin(string pinout, string token)
	{
		Match match = Regex.Match(pinout, Regex.Escape(token) + "[^;=:\\d]*(?:=|:)?\\s*(?:pin\\s*)?(?<pin>[1-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups["pin"].Value;
	}

	private static IReadOnlyList<int> ParseHeaterPins(string pinout)
	{
		Match match = Regex.Match(pinout, "żarzenie\\s*(?:=|:)?\\s*(?<first>[1-9])\\s*[-–]\\s*(?<second>[1-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if (!match.Success)
		{
			return Array.Empty<int>();
		}
		return new int[2]
		{
			int.Parse(match.Groups["first"].Value),
			int.Parse(match.Groups["second"].Value)
		};
	}
}
