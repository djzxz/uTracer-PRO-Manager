using System;
using System.Collections.Generic;
using System.Linq;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Safety;

public sealed class SafetyValidator
{
	public const double UTracer3PlusAbsoluteVoltageLimit = 425.0;

	public SafetyCheckResult ValidateProfile(TubeProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		List<string> list = new List<string>();
		List<string> list2 = new List<string>();
		if (!profile.ApprovedForHardware)
		{
			list.Add("Profil jest oznaczony jako specjalny lub wymagający dodatkowego połączenia. Nie może zostać wysłany do uTracera bez osobnego zatwierdzenia.");
		}
		if (profile.IsUserDefined)
		{
			list2.Add("To profil wpisany ręcznie. Program sprawdził limity liczbowe, ale nie potwierdził pinoutu ani wartości z kartą producenta.");
		}
		if (profile.CountsForConditionPercent && (profile.NominalAnodeCurrentMa <= 0.0 || profile.NominalGmMaV <= 0.0))
		{
			list.Add("Profil liczący kondycję musi zawierać dodatnie wartości katalogowe Ia i gm.");
		}
		if (!profile.CountsForConditionPercent)
		{
			list2.Add("Profil służy wyłącznie do porównania wyników i nie wylicza procentowej kondycji lampy.");
		}
		if (profile.HeaterVoltage <= 0.0)
		{
			list.Add("Napięcie żarzenia musi być większe od zera.");
		}
		if (profile.AnodeVoltage < 0.0 || profile.ScreenVoltage < 0.0)
		{
			list.Add("Napięcia anody i siatki ekranowej nie mogą być ujemne.");
		}
		if (profile.AnodeVoltage > profile.MaxAnodeVoltage)
		{
			list.Add("Punkt pracy przekracza katalogowe maksymalne napięcie anody.");
		}
		if (profile.ScreenVoltage > profile.MaxScreenVoltage && profile.MaxScreenVoltage > 0.0)
		{
			list.Add("Punkt pracy przekracza katalogowe maksymalne napięcie g2.");
		}
		if (profile.AnodeVoltage > 425.0 || profile.ScreenVoltage > 425.0)
		{
			list.Add("Punkt przekracza granicę 425 V ustawioną dla uTracera 3+.");
		}
		if (profile.AnodeComplianceMa <= 0.0)
		{
			list.Add("Ograniczenie prądu anody nie może być wyłączone.");
		}
		if (profile.ScreenVoltage > 0.0 && profile.ScreenComplianceMa <= 0.0)
		{
			list.Add("Ograniczenie prądu g2 nie może być wyłączone.");
		}
		double num = profile.AnodeVoltage * profile.AnodeComplianceMa / 1000.0;
		if (profile.MaxAnodePowerW > 0.0 && num > profile.MaxAnodePowerW * 2.0)
		{
			list2.Add($"Va × limit Ia = {num:F1} W. " + "To pomiar impulsowy, ale limit może wymagać obniżenia.");
		}
		if (!string.IsNullOrWhiteSpace(profile.CriticalWarning))
		{
			list2.Add(profile.CriticalWarning);
		}
		return new SafetyCheckResult(list.Count == 0, list, list2);
	}

	public SafetyCheckResult ValidateHardwareDiagnostic(NoTubeDiagnosticRequest request, CalibrationProfile calibration)
	{
		List<string> list = calibration.Validate().ToList();
		List<string> list2 = new List<string>();
		try
		{
			request.Validate();
		}
		catch (Exception ex)
		{
			list.Add(ex.Message);
		}
		if (request.AnodeVoltage > calibration.MaxAnodeVoltage)
		{
			list.Add("Napięcie diagnostyczne Va przekracza VaMax kalibracji.");
		}
		if (request.ScreenVoltage > calibration.MaxAnodeVoltage)
		{
			list.Add("Napięcie diagnostyczne Vs przekracza VaMax kalibracji.");
		}
		list2.Add("Test sprzętowy wolno wykonać wyłącznie bez lampy, z limitem 7 mA i żarzeniem ustawionym na 0 V.");
		return new SafetyCheckResult(list.Count == 0, list, list2);
	}
}
