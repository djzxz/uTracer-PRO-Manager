using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;

namespace uTracerProManager.Core.Services;

public sealed class SinglePointMeasurementController
{
	public const int HeaterRampMilliseconds = 10000;

	public const int HeaterRampStepMilliseconds = 500;

	public const int HardwareMaximumComplianceMaV03 = 25;

	private readonly SafetyValidator _safetyValidator;

	public SinglePointMeasurementController(SafetyValidator safetyValidator)
	{
		_safetyValidator = safetyValidator;
	}

	public async Task<MeasurementSessionResult> RunAsync(TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, SinglePointMeasurementOptions options, IProgress<MeasurementProgress>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		ArgumentNullException.ThrowIfNull(transport, "transport");
		ArgumentNullException.ThrowIfNull(calibration, "calibration");
		ArgumentNullException.ThrowIfNull(options, "options");
		if (!transport.IsConnected)
		{
			throw new InvalidOperationException("Transport nie jest połączony.");
		}
		options.Validate(transport.IsEmulator);
		SafetyCheckResult safetyCheckResult = _safetyValidator.ValidateProfile(profile);
		if (!safetyCheckResult.IsSafe)
		{
			throw new InvalidOperationException("Profil nie przeszedł kontroli bezpieczeństwa:\n- " + string.Join("\n- ", safetyCheckResult.Errors));
		}
		ValidateV03HardwareScope(profile, transport);
		IReadOnlyList<string> readOnlyList = calibration.Validate();
		if (readOnlyList.Count > 0)
		{
			throw new InvalidOperationException("Kalibracja jest nieprawidłowa:\n- " + string.Join("\n- ", readOnlyList));
		}
		DateTimeOffset startedAt = DateTimeOffset.Now;
		bool measurementConfigured = false;
		try
		{
			Report(progress, MeasurementState.Preflight, "Kontrola profilu, kalibracji i limitów.", 2.0);
			cancellationToken.ThrowIfCancellationRequested();
			Report(progress, MeasurementState.ReadingSupply, "Odczyt napięcia zasilającego Vsu.", 5.0);
			MeasurementResult measurementResult = await transport.ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken);
			double supplyVoltage = ((!transport.IsEmulator) ? (measurementResult.Engineering ?? throw new InvalidOperationException("Nie udało się przeliczyć napięcia Vsu.")).SupplyVoltage : (measurementResult.Engineering?.SupplyVoltage ?? 19.0));
			if ((supplyVoltage < 10.0 || supplyVoltage > 25.0) ? true : false)
			{
				throw new InvalidOperationException($"Vsu={supplyVoltage:F2} V jest poza zakresem 10–25 V.");
			}
			int num = ResolveCompliance(profile);
			byte complianceCode = CurrentLimitCodes.ForMilliAmps(num);
			byte averageCode = AverageCode(options.AveragingIndex);
			ushort anodeCode = CommandCodeConverter.AnodeCode(profile.AnodeVoltage, supplyVoltage, calibration);
			ushort screenCode = (ushort)((profile.ScreenVoltage > 0.0) ? CommandCodeConverter.ScreenCode(profile.ScreenVoltage, supplyVoltage, calibration) : 0);
			ushort gridCode = (ushort)((!transport.IsEmulator) ? CommandCodeConverter.GridCode(profile.GridVoltage, calibration) : 0);
			ushort fullHeaterCode = CommandCodeConverter.HeaterCode(profile.HeaterVoltage, supplyVoltage);
			Report(progress, MeasurementState.Configuring, $"Konfiguracja pomiaru; compliance {num} mA.", 8.0);
			await transport.SendFilamentCodeAsync(0, cancellationToken);
			await transport.SendStartMeasurementAsync(complianceCode, averageCode, 8, 8, cancellationToken);
			measurementConfigured = true;
			await RunHeaterRampAsync(profile, supplyVoltage, transport, options, progress, cancellationToken);
			await RunWarmupAsync(options, transport.IsEmulator, progress, cancellationToken);
			Report(progress, MeasurementState.Measuring, "Wykonywanie pojedynczego impulsu pomiarowego.", 85.0);
			if (options.HoldMilliseconds > 0)
			{
				await DelayScaledAsync(options.HoldMilliseconds, transport.IsEmulator, options, cancellationToken);
			}
			MeasurementResult measurementResult2 = ((!transport.IsEmulator) ? (await transport.ExecuteMeasurementAsync(anodeCode, screenCode, gridCode, fullHeaterCode, calibration, new AdcConversionOptions(options.AveragingIndex, profile.AnodeVoltage, profile.ScreenVoltage, profile.GridVoltage), cancellationToken)) : (await transport.RunEmulatedMeasurementAsync(profile, cancellationToken)));
			MeasurementResult finalMeasurement = measurementResult2;
			if (finalMeasurement.CurrentLimitHit)
			{
				throw new CurrentLimitException("uTracer zgłosił status 11 — zadziałało ograniczenie prądowe.");
			}
			ValidateResultAgainstProfile(profile, finalMeasurement);
			int dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
			await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
			measurementConfigured = false;
			Report(progress, MeasurementState.Completed, "Pomiar zakończony bez zadziałania limitu.", 100.0);
			return new MeasurementSessionResult(profile, finalMeasurement, startedAt, DateTimeOffset.Now, options.WarmupSeconds, dischargeSeconds, transport.IsEmulator);
		}
		catch (OperationCanceledException)
		{
			Report(progress, MeasurementState.Aborted, "Pomiar przerwany przez operatora.", 0.0);
			if (measurementConfigured)
			{
				int dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
				await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
			}
			throw;
		}
		catch
		{
			Report(progress, MeasurementState.Faulted, "Błąd pomiaru — uruchamianie bezpiecznego wyłączenia.", 0.0);
			if (measurementConfigured)
			{
				int dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage));
				await SafeShutdownAsync(transport, dischargeSeconds, options, progress, CancellationToken.None);
			}
			throw;
		}
	}

	private static void ValidateV03HardwareScope(TubeProfile profile, ITracerTransport transport)
	{
		if (transport.IsEmulator)
		{
			return;
		}
		throw new NotSupportedException("Prawdziwy tryb pojedynczego punktu jest celowo zablokowany. Sekwencja działa w emulatorze, ale przed wysłaniem niezerowego Vg trzeba potwierdzić kody siatki z oficjalnego GUI konkretnego uTracera. Na sprzęcie nadal dostępne są PING, odczyt ADC i diagnostyka 20 V bez lampy.");
	}

	private static int ResolveCompliance(TubeProfile profile)
	{
		int requested = (int)Math.Ceiling(Math.Max(profile.AnodeComplianceMa, profile.ScreenComplianceMa));
		int num = new int[9] { 7, 12, 25, 50, 100, 125, 150, 175, 200 }.FirstOrDefault((int value) => value >= requested);
		if (num == 0)
		{
			throw new InvalidOperationException($"Wymagany limit {requested} mA przekracza 200 mA.");
		}
		return num;
	}

	private static byte AverageCode(int index)
	{
		return index switch
		{
			0 => 64, 
			1 => 1, 
			2 => 2, 
			3 => 4, 
			4 => 8, 
			5 => 16, 
			6 => 32, 
			7 => 64, 
			_ => throw new ArgumentOutOfRangeException("index"), 
		};
	}

	private static async Task RunHeaterRampAsync(TubeProfile profile, double supplyVoltage, ITracerTransport transport, SinglePointMeasurementOptions options, IProgress<MeasurementProgress>? progress, CancellationToken cancellationToken)
	{
		int steps = 20;
		for (int step = 1; step <= steps; step++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			double targetVoltage = profile.HeaterVoltage * (double)step / (double)steps;
			ushort filamentCode = CommandCodeConverter.HeaterCode(targetVoltage, supplyVoltage);
			await transport.SendFilamentCodeAsync(filamentCode, cancellationToken);
			double percent = 10.0 + 25.0 * (double)step / (double)steps;
			Report(progress, MeasurementState.HeaterRamp, $"Rampa żarzenia: {targetVoltage:F2}/{profile.HeaterVoltage:F2} V.", percent, Remaining(10000 - step * 500, transport.IsEmulator, options));
			await DelayScaledAsync(500, transport.IsEmulator, options, cancellationToken);
		}
	}

	private static async Task RunWarmupAsync(SinglePointMeasurementOptions options, bool emulator, IProgress<MeasurementProgress>? progress, CancellationToken cancellationToken)
	{
		int logicalSeconds = options.WarmupSeconds;
		for (int elapsed = 0; elapsed < logicalSeconds; elapsed++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int value = logicalSeconds - elapsed;
			double percent = 35.0 + 45.0 * (double)elapsed / (double)logicalSeconds;
			Report(progress, MeasurementState.Warmup, $"Rozgrzewanie lampy: pozostało {value} s.", percent, value);
			await DelayScaledAsync(1000, emulator, options, cancellationToken);
		}
	}

	private static async Task SafeShutdownAsync(ITracerTransport transport, int dischargeSeconds, SinglePointMeasurementOptions options, IProgress<MeasurementProgress>? progress, CancellationToken cancellationToken)
	{
		Report(progress, MeasurementState.HeaterOff, "Wyłączanie żarzenia.", 90.0);
		try
		{
			await transport.SendFilamentCodeAsync(0, cancellationToken);
		}
		catch
		{
		}
		Report(progress, MeasurementState.EndingMeasurement, "Kończenie sekwencji pomiarowej.", 92.0);
		try
		{
			await transport.SendEndMeasurementAsync(cancellationToken);
		}
		catch
		{
		}
		for (int remaining = dischargeSeconds; remaining > 0; remaining--)
		{
			Report(progress, MeasurementState.Discharging, $"Rozładowanie wysokiego napięcia: {remaining} s.", 92.0 + 8.0 * (double)(dischargeSeconds - remaining) / (double)dischargeSeconds, remaining);
			await DelayScaledAsync(1000, transport.IsEmulator, options, CancellationToken.None);
		}
	}

	private static void ValidateResultAgainstProfile(TubeProfile profile, MeasurementResult result)
	{
		EngineeringAdcReading engineering = result.Engineering;
		if ((object)engineering != null)
		{
			double num = profile.AnodeVoltage * engineering.AnodeCurrentMa / 1000.0;
			if (profile.MaxAnodePowerW > 0.0 && num > profile.MaxAnodePowerW * 1.1)
			{
				throw new InvalidOperationException($"Obliczona moc anody {num:F2} W przekracza limit {profile.MaxAnodePowerW:F2} W.");
			}
			if (engineering.AnodeCurrentMa > profile.AnodeComplianceMa * 1.05)
			{
				throw new InvalidOperationException("Zmierzony prąd anody przekroczył ustawiony limit.");
			}
		}
	}

	private static int? Remaining(int milliseconds, bool emulator, SinglePointMeasurementOptions options)
	{
		int num = ((emulator && options.AccelerateEmulator) ? (milliseconds / options.EmulatorSpeedMultiplier) : milliseconds);
		return Math.Max(0, (int)Math.Ceiling((double)num / 1000.0));
	}

	private static Task DelayScaledAsync(int milliseconds, bool emulator, SinglePointMeasurementOptions options, CancellationToken cancellationToken)
	{
		return Task.Delay((emulator && options.AccelerateEmulator) ? Math.Max(10, milliseconds / options.EmulatorSpeedMultiplier) : milliseconds, cancellationToken);
	}

	private static void Report(IProgress<MeasurementProgress>? progress, MeasurementState state, string message, double percent, int? remainingSeconds = null)
	{
		progress?.Report(new MeasurementProgress(state, message, Math.Clamp(percent, 0.0, 100.0), remainingSeconds));
	}
}
