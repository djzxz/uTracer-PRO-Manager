using System;
using System.Collections.Generic;
using System.Linq;

namespace uTracerProManager.Core.Models;

public static class TubeMeasurementCapabilityClassifier
{
	private static readonly HashSet<string> CathodeRayAndImagingSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"CDC", "CRB", "CRC", "CRM", "CRO", "CRP", "CRR", "CRS", "CRT", "CRX",
		"DST", "FSS", "ICT", "IIT", "IMG", "IMT", "ORT", "PLC", "PLU", "RST",
		"VIC", "VID"
	};

	private static readonly HashSet<string> MicrowaveAndRfSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"ATR", "BWA", "BWC", "BWO", "CFT", "DXT", "FTO", "FWA", "KLY", "MAG",
		"MFS", "OSC", "PTR", "RCY", "TRC", "TRT", "TWT", "VMO"
	};

	private static readonly HashSet<string> PhotoAndOpticalSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LDR", "PEC", "PHD", "PHM", "PHT", "PMT" };

	private static readonly HashSet<string> DisplayAndCountingSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BST", "CDT", "CHR", "CNT", "DCT", "DRT", "IDT", "STR" };

	private static readonly HashSet<string> GasAndSwitchingSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"ADT", "GCR", "GDT", "GFI", "GMT", "GST", "IGN", "PDT", "RGC", "SAR",
		"SGM", "SGT", "TAC", "TGT", "THY", "TRI", "TVG"
	};

	private static readonly HashSet<string> ComponentsAndSpecialSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"AMT", "BAR", "BDM", "BDT", "BOL", "CPR", "CRY", "CTD", "EIP", "EMT",
		"EXC", "FET", "FLT", "FUS", "GRD", "INJ", "MCM", "MET", "MOD", "NCT",
		"NEL", "NET", "NOD", "NOG", "NOS", "OBH", "PHA", "PML", "PPC", "PPT",
		"PS", "PSO", "RCT", "REL", "RYV", "SAD", "SBT", "SEM", "SEN", "SET",
		"SKT", "SLS", "SMT", "SWD", "TBC", "TBT", "THC", "VAC", "VGH", "VGR",
		"VGT", "VMC", "VMT", "VSW", "XRT", "CPU"
	};

	public static TubeMeasurementDecision Classify(FrankCatalogEntry entry)
	{
		if (entry.HasApprovedMeasurementProfile)
		{
			return new TubeMeasurementDecision(TubeMeasurementAvailability.VerifiedReady, "ZWERYFIKOWANY — PROFIL GOTOWY", "Dla tego oznaczenia istnieje ręcznie sprawdzony profil dopuszczony do pracy z uTracer3+.");
		}
		if (entry.HasBlockedMeasurementProfile)
		{
			return new TubeMeasurementDecision(TubeMeasurementAvailability.VerifiedBlocked, "ZWERYFIKOWANY — POMIAR ZABLOKOWANY", "Istniejący profil został celowo zablokowany dla sprzętu. Może służyć wyłącznie do porównania lub dokumentacji.");
		}
		string text = (entry.SystemCode ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return Pending("Frank’s nie podaje kodu systemu. Typ wymaga sprawdzenia pinoutu, żarzenia, limitów i punktów pracy.");
		}
		if (text.Contains('~'))
		{
			return NotMeasurable("Kod Frank’s oznacza lampę nadawczą. Pełny bezpieczny test wymaga napięć, mocy żarzenia, sterowania lub osprzętu wykraczającego poza bieżącą konfigurację uTracer3+.");
		}
		if (text.Contains('°') || text.Contains('`') || text.Contains('^') || text.Contains('\''))
		{
			return NotMeasurable("Kod Frank’s oznacza element gazowany albo zimnokatodowy. Nie wolno uruchamiać go ze standardowym profilem lampy odbiorczej uTracer3+.");
		}
		if (string.Equals(text, "R", StringComparison.Ordinal) || string.Equals(text, "RR", StringComparison.Ordinal))
		{
			return NotMeasurable("Kod Frank’s oznacza prostownik wysokonapięciowy. Jego pełny test wymaga napięcia co najmniej około 1 kV, czyli poza zakresem uTracer3+.");
		}
		string item = LettersOnly(text);
		if (CathodeRayAndImagingSystems.Contains(item))
		{
			return NotMeasurable("To kineskop albo specjalna lampa obrazowa. Wymaga układów odchylania, ogniskowania lub bardzo wysokiego napięcia, których uTracer3+ nie zapewnia.");
		}
		if (MicrowaveAndRfSystems.Contains(item))
		{
			return NotMeasurable("To specjalna lampa mikrofalowa/RF. Do sprawdzenia potrzebuje rezonatora, pola magnetycznego lub toru wysokiej częstotliwości.");
		}
		if (PhotoAndOpticalSystems.Contains(item))
		{
			return NotMeasurable("To element fotoelektryczny. Wiarygodny test wymaga kontrolowanego źródła światła i dedykowanego układu pomiarowego.");
		}
		if (DisplayAndCountingSystems.Contains(item))
		{
			return NotMeasurable("To lampa wskaźnikowa, licząca albo pamięciowa. Wymaga wielokanałowego układu sterującego, którego uTracer3+ nie ma.");
		}
		if (GasAndSwitchingSystems.Contains(item))
		{
			return NotMeasurable("To gazowana lampa przełączająca lub wyładowcza. Jej zapłon i prąd wymagają dedykowanego, ograniczonego układu testowego.");
		}
		if (ComponentsAndSpecialSystems.Contains(item))
		{
			return NotMeasurable("To element specjalny albo podzespół, a nie standardowa lampa odbiorcza możliwa do bezpiecznego pomiaru uTracer3+.");
		}
		return Pending("Typ może nadawać się do pomiaru, ale nie ma jeszcze ręcznie zweryfikowanego profilu. Pomiar pozostaje zablokowany.");
	}

	public static IReadOnlySet<string> BuildDesignationSet(IEnumerable<TubeProfile> profiles)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (TubeProfile profile in profiles)
		{
			AddProfileDesignations(hashSet, profile.TubeTypes, profile.Aliases);
		}
		return hashSet;
	}

	public static void AddProfileDesignations(ISet<string> target, string tubeTypes, IEnumerable<string> aliases)
	{
		foreach (string alias in aliases)
		{
			AddDesignation(target, alias);
		}
		string[] array = tubeTypes.Split(new char[5] { '/', ',', ';', '(', ')' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string value in array)
		{
			AddDesignation(target, value);
		}
	}

	public static bool MatchesDesignation(string tubeType, IReadOnlySet<string> designations)
	{
		foreach (string item in ExtractDesignations(tubeType))
		{
			if (designations.Contains(item))
			{
				return true;
			}
		}
		return false;
	}

	private static IEnumerable<string> ExtractDesignations(string value)
	{
		string text = NormalizeDesignation(value);
		if (text.Length > 0)
		{
			yield return text;
		}
		string[] array = value.Split(new char[10] { ' ', '\t', '/', ',', ';', '(', ')', '[', ']', '=' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = NormalizeDesignation(array[i]);
			if (text2.Length > 0)
			{
				yield return text2;
			}
		}
	}

	private static void AddDesignation(ISet<string> target, string value)
	{
		string text = NormalizeDesignation(value);
		if (text.Length > 0)
		{
			target.Add(text);
		}
	}

	private static string NormalizeDesignation(string value)
	{
		return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
	}

	private static string LettersOnly(string code)
	{
		return new string(code.Where(char.IsLetter).Select(char.ToUpperInvariant).ToArray());
	}

	private static TubeMeasurementDecision NotMeasurable(string reason)
	{
		return new TubeMeasurementDecision(TubeMeasurementAvailability.NotMeasurable, "NIE MOŻNA ZMIERZYĆ uTracer3+", reason);
	}

	private static TubeMeasurementDecision Pending(string reason)
	{
		return new TubeMeasurementDecision(TubeMeasurementAvailability.AwaitingVerification, "NIE MOŻNA ZMIERZYĆ — BRAK ZWERYFIKOWANEGO PROFILU", reason);
	}
}
