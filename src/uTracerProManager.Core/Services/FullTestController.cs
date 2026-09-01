using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Safety;

namespace uTracerProManager.Core.Services;

public sealed class FullTestController
{
	private sealed record PointReading(double IaMa, double IsMa, double CommandedVa, double MeasuredVa, double CommandedVs, double MeasuredVs, string Status);

	private readonly SafetyValidator _safetyValidator;

	private readonly FullTestStatisticsService _statistics;

	private readonly Random _random = new Random(20260727);

	public FullTestController(SafetyValidator safetyValidator, FullTestStatisticsService statistics)
	{
		_safetyValidator = safetyValidator;
		_statistics = statistics;
	}

	public async Task<FullTestResult> RunAsync(string tubeInventoryNumber, string manufacturer, string notes, TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, FullTestOptions options, IProgress<FullTestProgress>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(profile, "profile");
		ArgumentNullException.ThrowIfNull(transport, "transport");
		ArgumentNullException.ThrowIfNull(calibration, "calibration");
		ArgumentNullException.ThrowIfNull(options, "options");
		options.Validate();
		if (!transport.IsConnected)
		{
			throw new InvalidOperationException("Transport nie jest połączony.");
		}
		if (!transport.IsEmulator && !calibration.IsCompleteForTubeTesting)
		{
			throw new InvalidOperationException("Prawdziwy test lampy wymaga zakończonego kreatora kalibracji: Vsu, Vn, Vg, Va/Vs oraz Ia/Is.");
		}
		SafetyCheckResult safetyCheckResult = _safetyValidator.ValidateProfile(profile);
		if (!safetyCheckResult.IsSafe)
		{
			throw new InvalidOperationException("Profil nie przeszedł kontroli bezpieczeństwa:\n- " + string.Join("\n- ", safetyCheckResult.Errors));
		}
		IReadOnlyList<string> readOnlyList = calibration.Validate();
		if (readOnlyList.Count > 0)
		{
			throw new InvalidOperationException("Kalibracja jest nieprawidłowa:\n- " + string.Join("\n- ", readOnlyList));
		}
		List<FullTestSample> samples = new List<FullTestSample>();
		List<DiagnosticCurvePoint> curvePoints = new List<DiagnosticCurvePoint>();
		bool dualSections = profile.IsDualTriode && options.DualSectionSimultaneous;
		DateTimeOffset startedAt = DateTimeOffset.Now;
		bool configured = false;
		int averagingIndex = options.StartingAveragingIndex;
		_statistics.Calculate(profile, samples, options);
		try
		{
			Report(progress, FullTestStage.Preflight, "Kontrola profilu i limitów — " + options.TestMode.DisplayName() + ".", 2.0);
			double supply = (await transport.ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken)).Engineering?.SupplyVoltage ?? 19.2;
			if ((supply < 10.0 || supply > 25.0) ? true : false)
			{
				throw new InvalidOperationException($"Vsu={supply:F2} V jest poza zakresem 10–25 V.");
			}
			int compliance = ResolveCompliance(profile);
			ushort fullHeaterCode = profile.RequiresExternalHeater
				? (ushort)0
				: CommandCodeConverter.HeaterCode(profile.HeaterVoltage, supply);
			await transport.SendFilamentCodeAsync(0, cancellationToken);
			await transport.SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(compliance), AverageCode(averagingIndex), 8, 8, cancellationToken);
			configured = true;
			await HeaterRampAsync(profile, transport, supply, options, progress, cancellationToken);
			await LogicalDelayWithProgressAsync(options.InitialWarmupSeconds, FullTestStage.InitialWarmup, "Nagrzewanie początkowe", 15.0, 35.0, options, progress, cancellationToken);
			if (options.TestMode == TubeTestMode.FullDiagnostic)
			{
				double precheckVa = Math.Min(100.0, Math.Max(25.0, profile.AnodeVoltage * 0.4));
				double value = Math.Clamp(profile.GridVoltage * 0.5, 0.0 - calibration.MaxGridMagnitudeV, 0.0);
				FullTestSample fullTestSample = await CreateSampleAsync(-100, conditioning: true, profile, transport, calibration, supply, fullHeaterCode, averagingIndex, options, dualSections, precheckVa, value, "Test wstępny", cancellationToken);
				samples.Add(fullTestSample);
				string message = $"Test wstępny {precheckVa:F0} V zakończony — połówka A {fullTestSample.AnodeCurrentMa:F3} mA" + (dualSections ? $", połówka B {fullTestSample.ScreenCurrentMa:F3} mA." : ".");
				FullTestSample latestSample = fullTestSample;
				Report(progress, FullTestStage.Conditioning, message, 37.0, 0, 0, null, latestSample);
			}
			for (int conditioning = 1; conditioning <= options.ConditioningSeries; conditioning++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				FullTestController fullTestController = this;
				int sequence = -conditioning;
				int averagingIndex2 = averagingIndex;
				CancellationToken cancellationToken2 = cancellationToken;
				FullTestSample fullTestSample2 = await fullTestController.CreateSampleAsync(sequence, conditioning: true, profile, transport, calibration, supply, fullHeaterCode, averagingIndex2, options, dualSections, null, null, "Kondycjonowanie", cancellationToken2);
				samples.Add(fullTestSample2);
				string message2 = $"Seria kondycjonująca {conditioning}/{options.ConditioningSeries}. " + "Nie jest liczona do wyniku końcowego.";
				double percent = 35.0 + 5.0 * (double)conditioning / (double)Math.Max(1, options.ConditioningSeries);
				int currentSeries = conditioning;
				int conditioningSeries = options.ConditioningSeries;
				FullTestSample latestSample = fullTestSample2;
				Report(progress, FullTestStage.Conditioning, message2, percent, currentSeries, conditioningSeries, null, latestSample);
				await DelayLogicalSecondsAsync(options.IntervalSeconds, options, cancellationToken);
			}
			int officialSeries = 0;
			FullTestStatistics currentStatistics;
			while (officialSeries < options.MaximumSeries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				officialSeries++;
				Report(progress, FullTestStage.Measuring, $"{options.TestMode.DisplayName()} — seria {officialSeries}/{options.MaximumSeries}.", 40.0 + 30.0 * (double)officialSeries / (double)options.MaximumSeries, officialSeries, options.MaximumSeries);
				FullTestController fullTestController2 = this;
				int sequence2 = officialSeries;
				int averagingIndex3 = averagingIndex;
				string measurementLabel = ((options.TestMode == TubeTestMode.Quick) ? "Szybki punkt katalogowy" : "Punkt główny");
				CancellationToken cancellationToken2 = cancellationToken;
				FullTestSample fullTestSample3 = await fullTestController2.CreateSampleAsync(sequence2, conditioning: false, profile, transport, calibration, supply, fullHeaterCode, averagingIndex3, options, dualSections, null, null, measurementLabel, cancellationToken2);
				samples.Add(fullTestSample3);
				ApplyOutlierFlags(samples, dualSections);
				currentStatistics = _statistics.Calculate(profile, samples, options);
				FullTestStatistics fullTestStatistics = (dualSections ? _statistics.CalculateSectionB(profile, samples, options) : null);
				string text = DecideAction(currentStatistics, officialSeries, options, ref averagingIndex);
				ReplaceLatestAction(samples, fullTestSample3.Sequence, text);
				string message3 = $"Ocena po serii {officialSeries}: {currentStatistics.Reliability}. " + text;
				double percent2 = 40.0 + 30.0 * (double)officialSeries / (double)options.MaximumSeries;
				int currentSeries2 = officialSeries;
				int maximumSeries = options.MaximumSeries;
				FullTestSample latestSample = samples.Last();
				Report(progress, FullTestStage.Evaluating, message3, percent2, currentSeries2, maximumSeries, null, latestSample, currentStatistics);
				if ((currentStatistics.Stable && currentStatistics.ValidSeries >= options.MinimumValidSeries && ((object)fullTestStatistics == null || fullTestStatistics.Stable)) || officialSeries >= options.MaximumSeries)
				{
					break;
				}
				if (currentStatistics.ValidSeries < options.MinimumValidSeries || (!(currentStatistics.LastStepIaDriftPercent > options.MaxStepDriftPercent) && !(currentStatistics.LastStepGmDriftPercent > options.MaxStepDriftPercent)) || options.ExtraStabilizationSeconds <= 0)
				{
					await DelayLogicalSecondsAsync(options.IntervalSeconds, options, cancellationToken);
				}
				else
				{
					await LogicalDelayWithProgressAsync(options.ExtraStabilizationSeconds, FullTestStage.ExtraStabilization, "Dodatkowa stabilizacja po wykryciu dryftu", 40.0 + 30.0 * (double)officialSeries / (double)options.MaximumSeries, 41.0 + 30.0 * (double)officialSeries / (double)options.MaximumSeries, options, progress, cancellationToken);
				}
			}
			if (options.ThermalStabilitySeconds > 0)
			{
				await RunThermalStabilityAsync(samples, profile, transport, calibration, supply, fullHeaterCode, averagingIndex, options, dualSections, progress, cancellationToken);
				ApplyOutlierFlags(samples, dualSections);
			}
			currentStatistics = _statistics.Calculate(profile, samples, options);
			FullTestStatistics sectionBStatistics = (dualSections ? _statistics.CalculateSectionB(profile, samples, options) : null);
			DualSectionComparison comparison = (((object)sectionBStatistics != null) ? _statistics.CompareSections(currentStatistics, sectionBStatistics, options.TestMode) : null);
			if (options.RunCharacteristicScan)
			{
				List<DiagnosticCurvePoint> list = curvePoints;
				list.AddRange(await RunCharacteristicScanAsync(profile, transport, calibration, supply, fullHeaterCode, averagingIndex, options, dualSections, progress, cancellationToken));
			}
			Report(progress, FullTestStage.ShuttingDown, "Wyłączanie żarzenia i kończenie pomiaru.", 90.0, 0, 0, null, null, currentStatistics);
			await SafeShutdownAsync(transport, profile, options, progress);
			configured = false;
			FullTestResult result = new FullTestResult(Guid.NewGuid(), NormalizeText(tubeInventoryNumber, "BRAK-NR"), NormalizeText(manufacturer, "Nie podano"), notes?.Trim() ?? string.Empty, profile, startedAt, DateTimeOffset.Now, options, currentStatistics, samples.ToArray(), transport.IsEmulator, "1.2.7", "", "", "Nieznany", options.TestMode, sectionBStatistics, comparison, curvePoints.ToArray());
			Report(progress, FullTestStage.Completed, $"{options.TestMode.DisplayName()} zakończony: {currentStatistics.Grade}; {currentStatistics.Reliability}.", 100.0, 0, 0, null, null, currentStatistics);
			return result;
		}
		catch (OperationCanceledException)
		{
			Report(progress, FullTestStage.Aborted, "Test przerwany przez operatora.", 0.0);
			if (configured)
			{
				await SafeShutdownAsync(transport, profile, options, progress);
			}
			throw;
		}
		catch
		{
			Report(progress, FullTestStage.Faulted, "Błąd testu — uruchamianie bezpiecznego wyłączenia.", 0.0);
			if (configured)
			{
				await SafeShutdownAsync(transport, profile, options, progress);
			}
			throw;
		}
	}

	private async Task<FullTestSample> CreateSampleAsync(int sequence, bool conditioning, TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, double supplyVoltage, ushort fullHeaterCode, int averagingIndex, FullTestOptions options, bool dualSections, double? targetVaOverride = null, double? targetVgOverride = null, string measurementLabel = "Punkt główny", CancellationToken cancellationToken = default(CancellationToken))
	{
		if (transport.IsEmulator)
		{
			return await CreateEmulatedSampleAsync(sequence, conditioning, profile, transport, averagingIndex, options, dualSections, targetVaOverride, targetVgOverride, measurementLabel, cancellationToken);
		}
		double targetVa = targetVaOverride ?? profile.AnodeVoltage;
		double targetVg = targetVgOverride ?? profile.GridVoltage;
		double targetVs = (dualSections ? targetVa : profile.ScreenVoltage);
		await transport.SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(ResolveCompliance(profile)), AverageCode(averagingIndex), 8, 8, cancellationToken);
		double num = Math.Clamp(targetVa * 0.1, 5.0, 25.0);
		double num2 = Math.Clamp(Math.Abs(targetVg) * 0.1, 0.1, 0.2);
		double vaLow = Math.Max(2.0, targetVa - num);
		double vaHigh = Math.Min(Math.Min(profile.MaxAnodeVoltage, calibration.MaxAnodeVoltage), targetVa + num);
		double vgLow = Math.Max(0.0 - calibration.MaxGridMagnitudeV, targetVg - num2);
		double vgHigh = Math.Min(0.0, targetVg + num2);
		PointReading center = await MeasureCorrectedPointAsync(targetVa, targetVs, targetVg, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
		PointReading lowerVa = center;
		PointReading upperVa = center;
		PointReading lowerVg = center;
		PointReading pointReading = center;
		if (options.MeasureDynamicParameters)
		{
			lowerVa = await MeasureCorrectedPointAsync(vaLow, dualSections ? vaLow : targetVs, targetVg, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
			upperVa = await MeasureCorrectedPointAsync(vaHigh, dualSections ? vaHigh : targetVs, targetVg, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
			lowerVg = await MeasureCorrectedPointAsync(targetVa, targetVs, vgLow, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
			pointReading = await MeasureCorrectedPointAsync(targetVa, targetVs, vgHigh, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
		}
		double num3 = upperVa.IaMa - lowerVa.IaMa;
		double num4 = upperVa.MeasuredVa - lowerVa.MeasuredVa;
		double num5 = ((!options.MeasureDynamicParameters) ? 0.0 : ((Math.Abs(num3) < 1E-06) ? 999999.0 : Math.Abs(num4 / num3)));
		double num6 = pointReading.IaMa - lowerVg.IaMa;
		double num7 = vgHigh - vgLow;
		double num8 = ((!options.MeasureDynamicParameters || Math.Abs(num7) < 1E-06) ? 0.0 : Math.Abs(num6 / num7));
		double mu = (options.MeasureDynamicParameters ? (num8 * num5) : 0.0);
		double num9 = center.MeasuredVa * center.IaMa / 1000.0;
		double num10 = 0.0;
		double num11 = 0.0;
		double sectionBMu = 0.0;
		double num12 = 0.0;
		if (dualSections)
		{
			double num13 = upperVa.IsMa - lowerVa.IsMa;
			double num14 = upperVa.MeasuredVs - lowerVa.MeasuredVs;
			num10 = ((!options.MeasureDynamicParameters) ? 0.0 : ((Math.Abs(num13) < 1E-06) ? 999999.0 : Math.Abs(num14 / num13)));
			double num15 = pointReading.IsMa - lowerVg.IsMa;
			num11 = ((!options.MeasureDynamicParameters || Math.Abs(num7) < 1E-06) ? 0.0 : Math.Abs(num15 / num7));
			sectionBMu = (options.MeasureDynamicParameters ? (num11 * num10) : 0.0);
			num12 = center.MeasuredVs * center.IsMa / 1000.0;
		}
		ValidateMeasuredSample(profile, center, num9, dualSections, num12);
		string text = (dualSections ? $"Korekcja A: {center.CommandedVa:F2}/{center.MeasuredVa:F2} V; B: {center.CommandedVs:F2}/{center.MeasuredVs:F2} V." : $"Korekcja Va: {center.CommandedVa:F2}/{center.MeasuredVa:F2} V; Vg2: {center.CommandedVs:F2}/{center.MeasuredVs:F2} V.");
		return new FullTestSample(sequence, DateTimeOffset.Now, conditioning, targetVa, targetVs, targetVg, center.IaMa, center.IsMa, num8, num5, mu, num9, averagingIndex, IsOutlier: false, conditioning ? (measurementLabel + " — pominięty w statystyce. " + text) : text, center.Status, center.CommandedVa, center.MeasuredVa, center.CommandedVs, center.MeasuredVs, num11, num10, sectionBMu, num12, measurementLabel);
	}

	private async Task<FullTestSample> CreateEmulatedSampleAsync(int sequence, bool conditioning, TubeProfile profile, ITracerTransport transport, int averagingIndex, FullTestOptions options, bool dualSections, double? targetVaOverride, double? targetVgOverride, string measurementLabel, CancellationToken cancellationToken)
	{
		MeasurementResult measurementResult = await transport.RunEmulatedMeasurementAsync(profile, cancellationToken);
		EngineeringAdcReading engineeringAdcReading = measurementResult.Engineering ?? throw new InvalidOperationException("Emulator nie zwrócił przeliczonych danych.");
		VoltageSetpointController voltageSetpointController = new VoltageSetpointController();
		double num = targetVaOverride ?? profile.AnodeVoltage;
		double gridVoltage = targetVgOverride ?? profile.GridVoltage;
		double num2 = (dualSections ? num : profile.ScreenVoltage);
		double num3 = num;
		double num4 = engineeringAdcReading.EstimatedAnodeVoltage;
		for (int i = 0; i < options.MaximumCorrectionIterations; i++)
		{
			VoltageCorrection voltageCorrection = voltageSetpointController.Correct(num, num3, num4, Math.Min(profile.MaxAnodeVoltage, 425.0));
			num3 = voltageCorrection.NewCommandVoltage;
			if (voltageCorrection.InTolerance)
			{
				break;
			}
			num4 += (num3 - num4) * 0.85;
		}
		double num5 = num2;
		double num6 = ((num2 > 0.0) ? Math.Max(0.0, num2 * 0.97) : 0.0);
		if (num2 > 0.0)
		{
			for (int j = 0; j < options.MaximumCorrectionIterations; j++)
			{
				VoltageCorrection voltageCorrection2 = voltageSetpointController.Correct(num2, num5, num6, dualSections ? Math.Min((profile.MaxAnodeVoltage > 0.0) ? profile.MaxAnodeVoltage : 425.0, 425.0) : ((profile.MaxScreenVoltage > 0.0) ? Math.Min(profile.MaxScreenVoltage, 425.0) : 425.0));
				num5 = voltageCorrection2.NewCommandVoltage;
				if (voltageCorrection2.InTolerance)
				{
					break;
				}
				num6 += (num5 - num6) * 0.85;
			}
		}
		int num7 = Math.Max(1, conditioning ? Math.Abs(sequence) : (sequence + 2));
		double num8 = -0.035 * Math.Exp((double)(-num7) / 2.2);
		double num9 = 0.012 / Math.Sqrt(Math.Max(1.0, AveragingDivisor(averagingIndex)));
		double num10 = Math.Max(0.0, profile.NominalAnodeCurrentMa * (1.0 + num8 + NextGaussian() * num9));
		double num11 = 1.0 + NextGaussian() * 0.035;
		double num12 = (dualSections ? (profile.NominalAnodeCurrentMa * num11) : profile.NominalScreenCurrentMa);
		double num13 = ((num12 > 0.0) ? Math.Max(0.0, num12 * (1.0 + num8 * 0.7 + NextGaussian() * num9 * 1.2)) : 0.0);
		double num14 = ((profile.NominalGmMaV > 0.0) ? profile.NominalGmMaV : Math.Max(0.1, engineeringAdcReading.AnodeCurrentMa / 5.0));
		double num15 = (options.MeasureDynamicParameters ? Math.Max(0.001, num14 * (1.0 + num8 * 0.8 + NextGaussian() * num9 * 0.9)) : 0.0);
		double num16 = ((profile.NominalRpKohm > 0.0) ? profile.NominalRpKohm : ((profile.NominalMu > 0.0) ? (profile.NominalMu / num14) : (1.0 / Math.Max(0.001, num14))));
		double num17 = (options.MeasureDynamicParameters ? Math.Max(0.001, num16 * (1.0 + NextGaussian() * num9 * 0.7)) : 0.0);
		double mu = ((!options.MeasureDynamicParameters) ? 0.0 : ((profile.NominalMu > 0.0) ? Math.Max(0.01, profile.NominalMu * (1.0 + NextGaussian() * num9 * 0.5)) : (num15 * num17)));
		double num18 = ((dualSections && options.MeasureDynamicParameters) ? Math.Max(0.001, num14 * num11 * (1.0 + num8 * 0.8 + NextGaussian() * num9)) : 0.0);
		double num19 = ((dualSections && options.MeasureDynamicParameters) ? Math.Max(0.001, num16 / Math.Max(0.1, num11) * (1.0 + NextGaussian() * num9)) : 0.0);
		double sectionBMu = ((dualSections && options.MeasureDynamicParameters) ? (num18 * num19) : 0.0);
		double num20 = num4 * num10 / 1000.0;
		double sectionBPowerW = (dualSections ? (num6 * num13 / 1000.0) : 0.0);
		if (measurementResult.CurrentLimitHit)
		{
			throw new CurrentLimitException("Status 11 — zadziałało ograniczenie prądowe.");
		}
		if (num10 > profile.AnodeComplianceMa * 0.85)
		{
			throw new InvalidOperationException("Prąd anody przekroczył 85% compliance.");
		}
		if (profile.MaxAnodePowerW > 0.0 && num20 > profile.MaxAnodePowerW * 0.95)
		{
			throw new InvalidOperationException("Moc anody przekroczyła 95% limitu.");
		}
		return new FullTestSample(sequence, DateTimeOffset.Now, conditioning, num, num2, gridVoltage, num10, num13, num15, num17, mu, num20, averagingIndex, IsOutlier: false, conditioning ? "Seria kondycjonująca — pominięta w statystyce." : $"Napięcia skorygowane: Va zadane {num3:F2} V, zmierzone {num4:F2} V; Vg2 zadane {num5:F2} V, zmierzone {num6:F2} V.", measurementResult.StatusCode, num3, num4, num5, num6, num18, num19, sectionBMu, sectionBPowerW, measurementLabel);
	}

	private async Task<PointReading> MeasureCorrectedPointAsync(double targetVa, double targetVs, double targetVg, TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, double supplyVoltage, ushort fullHeaterCode, int averagingIndex, int maximumCorrectionIterations, CancellationToken cancellationToken)
	{
		VoltageSetpointController controller = new VoltageSetpointController();
		double commandVa = targetVa;
		double commandVs = targetVs;
		double usedCommandVa = commandVa;
		double usedCommandVs = commandVs;
		MeasurementResult measurementResult = null;
		for (int iteration = 0; iteration < maximumCorrectionIterations; iteration++)
		{
			usedCommandVa = commandVa;
			usedCommandVs = commandVs;
			ushort anodeCode = CommandCodeConverter.AnodeCode(usedCommandVa, supplyVoltage, calibration);
			ushort screenCode = (ushort)((targetVs > 0.0) ? CommandCodeConverter.ScreenCode(usedCommandVs, supplyVoltage, calibration) : 0);
			ushort gridCode = CommandCodeConverter.GridCode(targetVg, calibration);
			measurementResult = await transport.ExecuteMeasurementAsync(anodeCode, screenCode, gridCode, fullHeaterCode, calibration, new AdcConversionOptions(averagingIndex, usedCommandVa, usedCommandVs, targetVg), cancellationToken);
			if (measurementResult.CurrentLimitHit)
			{
				throw new CurrentLimitException("Status 11 — zadziałało ograniczenie prądowe.");
			}
			EngineeringAdcReading engineeringAdcReading = measurementResult.Engineering ?? throw new InvalidOperationException("Brak przeliczonych danych pomiarowych.");
			VoltageCorrection voltageCorrection = controller.Correct(targetVa, commandVa, engineeringAdcReading.EstimatedAnodeVoltage, Math.Min(Math.Min(profile.MaxAnodeVoltage, calibration.MaxAnodeVoltage), 425.0));
			double maximumAllowedVoltage = (profile.IsDualTriode ? Math.Min((profile.MaxAnodeVoltage > 0.0) ? profile.MaxAnodeVoltage : calibration.MaxAnodeVoltage, calibration.MaxAnodeVoltage) : ((profile.MaxScreenVoltage > 0.0) ? Math.Min(profile.MaxScreenVoltage, 425.0) : 425.0));
			VoltageCorrection voltageCorrection2 = ((targetVs > 0.0) ? controller.Correct(targetVs, commandVs, engineeringAdcReading.MeasuredScreenVoltage, maximumAllowedVoltage) : new VoltageCorrection(0.0, 0.0, 0.0, 0.0, 0.0, InTolerance: true, Limited: false));
			commandVa = voltageCorrection.NewCommandVoltage;
			commandVs = voltageCorrection2.NewCommandVoltage;
			if (voltageCorrection.InTolerance && voltageCorrection2.InTolerance)
			{
				break;
			}
		}
		EngineeringAdcReading engineeringAdcReading2 = measurementResult?.Engineering ?? throw new InvalidOperationException("Nie wykonano pomiaru.");
		return new PointReading(engineeringAdcReading2.AnodeCurrentMa, engineeringAdcReading2.ScreenCurrentMa, usedCommandVa, engineeringAdcReading2.EstimatedAnodeVoltage, usedCommandVs, engineeringAdcReading2.MeasuredScreenVoltage, measurementResult.StatusCode);
	}

	private async Task RunThermalStabilityAsync(List<FullTestSample> samples, TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, double supplyVoltage, ushort fullHeaterCode, int averagingIndex, FullTestOptions options, bool dualSections, IProgress<FullTestProgress>? progress, CancellationToken cancellationToken)
	{
		int interval = Math.Max(5, options.ThermalSampleIntervalSeconds);
		int sampleCount = Math.Max(2, options.ThermalStabilitySeconds / interval + 1);
		int startingSequence = (from sample in samples
			where !sample.Conditioning
			select sample.Sequence).DefaultIfEmpty(0).Max();
		for (int index = 0; index < sampleCount; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int num = Math.Min(options.ThermalStabilitySeconds, index * interval);
			Report(progress, FullTestStage.ThermalStability, $"Stabilność termiczna: {num}/{options.ThermalStabilitySeconds} s — pomiar {index + 1}/{sampleCount}.", 72.0 + 10.0 * (double)index / (double)Math.Max(1, sampleCount - 1), index + 1, sampleCount, Math.Max(0, options.ThermalStabilitySeconds - num));
			FullTestController fullTestController = this;
			int sequence = startingSequence + index + 1;
			CancellationToken cancellationToken2 = cancellationToken;
			FullTestSample fullTestSample = await fullTestController.CreateSampleAsync(sequence, conditioning: false, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options, dualSections, null, null, "Stabilność termiczna 5 min", cancellationToken2);
			samples.Add(fullTestSample);
			ApplyOutlierFlags(samples, dualSections);
			FullTestStatistics fullTestStatistics = _statistics.Calculate(profile, samples, options);
			string message = $"Stabilność termiczna: A={fullTestSample.AnodeCurrentMa:F3} mA" + (dualSections ? $", B={fullTestSample.ScreenCurrentMa:F3} mA" : string.Empty) + $"; dryft A={fullTestStatistics.LastStepIaDriftPercent:F2}%.";
			double percent = 72.0 + 10.0 * (double)index / (double)Math.Max(1, sampleCount - 1);
			int currentSeries = index + 1;
			FullTestSample latestSample = fullTestSample;
			FullTestStatistics currentStatistics = fullTestStatistics;
			Report(progress, FullTestStage.ThermalStability, message, percent, currentSeries, sampleCount, null, latestSample, currentStatistics);
			if (index < sampleCount - 1)
			{
				await DelayLogicalSecondsAsync(interval, options, cancellationToken);
			}
		}
	}

	private async Task<IReadOnlyList<DiagnosticCurvePoint>> RunCharacteristicScanAsync(TubeProfile profile, ITracerTransport transport, CalibrationProfile calibration, double supplyVoltage, ushort fullHeaterCode, int averagingIndex, FullTestOptions options, bool dualSections, IProgress<FullTestProgress>? progress, CancellationToken cancellationToken)
	{
		double[] array = ParseGridVoltages(profile);
		double[] anodeValues = BuildAnodeScan(profile, calibration);
		int total = array.Length * anodeValues.Length;
		if (total > 160)
		{
			int stride = (int)Math.Ceiling((double)total / 160.0);
			anodeValues = anodeValues.Where((double _, int index) => index % stride == 0).ToArray();
			total = array.Length * anodeValues.Length;
		}
		List<DiagnosticCurvePoint> points = new List<DiagnosticCurvePoint>(total);
		int sequence = 0;
		double[] array2 = array;
		foreach (double grid in array2)
		{
			double[] array3 = anodeValues;
			foreach (double anode in array3)
			{
				cancellationToken.ThrowIfCancellationRequested();
				sequence++;
				double sectionBTarget = (dualSections ? anode : profile.ScreenVoltage);
				PointReading pointReading;
				try
				{
					if (transport.IsEmulator)
					{
						double num3 = ((profile.AnodeVoltage <= 0.0) ? 1.0 : Math.Clamp(anode / profile.AnodeVoltage, 0.0, 1.5));
						double num4 = grid - profile.GridVoltage;
						double num5 = Math.Clamp(1.0 + num4 * Math.Max(0.1, profile.NominalGmMaV) / Math.Max(0.1, profile.NominalAnodeCurrentMa), 0.0, 2.0);
						double num6 = Math.Max(0.0, profile.NominalAnodeCurrentMa * num3 * num5);
						double isMa = (dualSections ? Math.Max(0.0, num6 * 0.97) : (profile.NominalScreenCurrentMa * num3));
						pointReading = new PointReading(num6, isMa, anode, anode, sectionBTarget, sectionBTarget, "EMULATOR CURVE");
					}
					else
					{
						pointReading = await MeasureCorrectedPointAsync(anode, sectionBTarget, grid, profile, transport, calibration, supplyVoltage, fullHeaterCode, averagingIndex, options.MaximumCorrectionIterations, cancellationToken);
					}
				}
				catch (CurrentLimitException ex)
				{
					Report(progress, FullTestStage.CharacteristicScan, $"Skan zatrzymany dla Vg={grid:F2} V przy Va={anode:F0} V: {ex.Message}", 83.0 + 6.0 * (double)sequence / (double)Math.Max(1, total), sequence, total);
					break;
				}
				double power = pointReading.MeasuredVa * pointReading.IaMa / 1000.0;
				double sectionBPower = (dualSections ? (pointReading.MeasuredVs * pointReading.IsMa / 1000.0) : 0.0);
				try
				{
					ValidateMeasuredSample(profile, pointReading, power, dualSections, sectionBPower);
				}
				catch (InvalidOperationException ex2)
				{
					Report(progress, FullTestStage.CharacteristicScan, $"Pominięto dalsze punkty dla Vg={grid:F2} V: {ex2.Message}", 83.0 + 6.0 * (double)sequence / (double)Math.Max(1, total), sequence, total);
					break;
				}
				points.Add(new DiagnosticCurvePoint(sequence, DateTimeOffset.Now, grid, anode, pointReading.MeasuredVa, pointReading.IaMa, sectionBTarget, pointReading.MeasuredVs, dualSections ? pointReading.IsMa : 0.0, pointReading.Status));
				Report(progress, FullTestStage.CharacteristicScan, $"Skan charakterystyk: {sequence}/{total}, Vg={grid:F2} V, Va={anode:F0} V.", 83.0 + 6.0 * (double)sequence / (double)Math.Max(1, total), sequence, total);
			}
		}
		return points;
	}

	private static double[] ParseGridVoltages(TubeProfile profile)
	{
		double result;
		double[] array = (from value in (from value in (from value in (profile.CurveGridVoltages ?? string.Empty).Split(new char[3] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					select (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)) ? double.NaN : result).Where(double.IsFinite)
				where value <= 0.0
				select value).Distinct()
			orderby value descending
			select value).ToArray();
		if (array.Length != 0)
		{
			return array;
		}
		double num = Math.Clamp(Math.Abs(profile.GridVoltage) * 0.25, 0.2, 1.0);
		return new double[3]
		{
			Math.Min(0.0, profile.GridVoltage + num),
			profile.GridVoltage,
			profile.GridVoltage - num
		};
	}

	private static double[] BuildAnodeScan(TubeProfile profile, CalibrationProfile calibration)
	{
		double num = ((profile.CurveVaStartV > 0.0) ? profile.CurveVaStartV : Math.Max(25.0, profile.AnodeVoltage * 0.25));
		double val = ((profile.MaxAnodeVoltage > 0.0) ? profile.MaxAnodeVoltage : Math.Max(profile.AnodeVoltage, calibration.MaxAnodeVoltage));
		double val2 = ((profile.CurveVaStopV > num) ? profile.CurveVaStopV : Math.Max(profile.AnodeVoltage, num));
		val2 = Math.Min(val2, Math.Min(val, calibration.MaxAnodeVoltage));
		if (val2 < num)
		{
			val2 = num;
		}
		double num2 = ((profile.CurveVaStepV > 0.0) ? profile.CurveVaStepV : Math.Clamp(Math.Max(1.0, val2 - num) / 8.0, 10.0, 50.0));
		List<double> list = new List<double>();
		for (double num3 = num; num3 <= val2 + num2 * 0.25; num3 += num2)
		{
			list.Add(Math.Min(num3, val2));
		}
		if (!list.Any((double value) => Math.Abs(value - profile.AnodeVoltage) < 0.5) && profile.AnodeVoltage >= num && profile.AnodeVoltage <= val2)
		{
			list.Add(profile.AnodeVoltage);
		}
		return (from value in list.Where((double value) => value >= 2.0).Distinct()
			orderby value
			select value).ToArray();
	}

	private static void ValidateMeasuredSample(TubeProfile profile, PointReading reading, double power, bool dualSections, double sectionBPower)
	{
		if (reading.IaMa > profile.AnodeComplianceMa * 0.9)
		{
			throw new InvalidOperationException("Prąd anody przekroczył 90% compliance.");
		}
		if (!dualSections && profile.ScreenVoltage > 0.0 && reading.IsMa > profile.ScreenComplianceMa * 0.9)
		{
			throw new InvalidOperationException("Prąd siatki ekranowej przekroczył 90% compliance.");
		}
		if (profile.MaxAnodePowerW > 0.0 && power > profile.MaxAnodePowerW * 0.95)
		{
			throw new InvalidOperationException("Moc anody przekroczyła 95% limitu katalogowego.");
		}
		if (dualSections && reading.IsMa > profile.AnodeComplianceMa * 0.9)
		{
			throw new InvalidOperationException("Prąd połówki B przekroczył 90% compliance.");
		}
		if (dualSections && profile.MaxAnodePowerW > 0.0 && sectionBPower > profile.MaxAnodePowerW * 0.95)
		{
			throw new InvalidOperationException("Moc anody połówki B przekroczyła 95% limitu katalogowego.");
		}
	}

	private void ApplyOutlierFlags(List<FullTestSample> samples, bool includeSectionB)
	{
		HashSet<int> hashSet = _statistics.DetectOutlierSequences(samples, includeSectionB).ToHashSet();
		for (int i = 0; i < samples.Count; i++)
		{
			FullTestSample fullTestSample = samples[i];
			bool flag = !fullTestSample.Conditioning && hashSet.Contains(fullTestSample.Sequence);
			if (fullTestSample.IsOutlier != flag)
			{
				samples[i] = fullTestSample with
				{
					IsOutlier = flag
				};
			}
		}
	}

	private static string DecideAction(FullTestStatistics statistics, int series, FullTestOptions options, ref int averagingIndex)
	{
		if (statistics.Stable)
		{
			return "Kryteria stabilności spełnione — koniec automatyczny.";
		}
		if (series >= options.MaximumSeries)
		{
			return $"Osiągnięto maksymalnie {options.MaximumSeries} serii — wynik oznaczony jako niestabilny.";
		}
		if (statistics.ValidSeries < options.MinimumValidSeries)
		{
			return $"Kontynuacja do minimum {options.MinimumValidSeries} ważnych serii.";
		}
		if (statistics.LastStepIaDriftPercent > options.MaxStepDriftPercent || statistics.LastStepGmDriftPercent > options.MaxStepDriftPercent)
		{
			return $"Wykryto dryft — dodatkowe {options.ExtraStabilizationSeconds} s stabilizacji.";
		}
		if ((statistics.CvIaPercent > options.MaxIaCvPercent || statistics.CvGmPercent > options.MaxGmCvPercent) && averagingIndex < options.MaximumAveragingIndex)
		{
			averagingIndex++;
			return $"Duży rozrzut — zwiększono uśrednianie do poziomu {averagingIndex}.";
		}
		if (statistics.Outliers > 0)
		{
			return "Wykryto próbkę odstającą metodą MAD — zostanie zastąpiona kolejną serią.";
		}
		return "Kryteria jeszcze niespełnione — powtórzenie w tym samym punkcie.";
	}

	private static void ReplaceLatestAction(List<FullTestSample> samples, int sequence, string action)
	{
		int num = samples.FindLastIndex((FullTestSample sample) => sample.Sequence == sequence);
		if (num >= 0)
		{
			samples[num] = samples[num]with
			{
				ActionAfterSample = action
			};
		}
	}

	private static async Task HeaterRampAsync(TubeProfile profile, ITracerTransport transport, double supply, FullTestOptions options, IProgress<FullTestProgress>? progress, CancellationToken cancellationToken)
	{
		if (profile.RequiresExternalHeater)
		{
			await transport.SendFilamentCodeAsync(0, cancellationToken);
			Report(progress, FullTestStage.HeaterRamp, "Żarzenie zewnętrzne — wyjście Vh uTracera pozostaje wyłączone.", 15.0);
			return;
		}
		for (int step = 1; step <= 20; step++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			double voltage = profile.HeaterVoltage * (double)step / 20.0;
			ushort filamentCode = CommandCodeConverter.HeaterCode(voltage, supply);
			await transport.SendFilamentCodeAsync(filamentCode, cancellationToken);
			Report(progress, FullTestStage.HeaterRamp, $"Rampa żarzenia: {voltage:F2}/{profile.HeaterVoltage:F2} V.", 3.0 + 12.0 * (double)step / 20.0, 0, 0, (int)Math.Ceiling((double)(20 - step) * 0.5));
			await DelayMillisecondsAsync(500, options, cancellationToken);
		}
	}

	private static async Task LogicalDelayWithProgressAsync(int logicalSeconds, FullTestStage stage, string label, double startPercent, double endPercent, FullTestOptions options, IProgress<FullTestProgress>? progress, CancellationToken cancellationToken)
	{
		for (int elapsed = 0; elapsed < logicalSeconds; elapsed++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int value = logicalSeconds - elapsed;
			Report(progress, stage, $"{label}: pozostało {value} s.", startPercent + (endPercent - startPercent) * (double)elapsed / (double)Math.Max(1, logicalSeconds), 0, 0, value);
			await DelayMillisecondsAsync(1000, options, cancellationToken);
		}
	}

	private static Task DelayLogicalSecondsAsync(int seconds, FullTestOptions options, CancellationToken cancellationToken)
	{
		return DelayMillisecondsAsync(seconds * 1000, options, cancellationToken);
	}

	private static Task DelayMillisecondsAsync(int milliseconds, FullTestOptions options, CancellationToken cancellationToken)
	{
		return Task.Delay(options.AccelerateEmulator ? Math.Max(5, milliseconds / options.EmulatorSpeedMultiplier) : milliseconds, cancellationToken);
	}

	private static async Task SafeShutdownAsync(ITracerTransport transport, TubeProfile profile, FullTestOptions options, IProgress<FullTestProgress>? progress)
	{
		Report(progress, FullTestStage.ShuttingDown, "HEATER 0 i END.", 92.0);
		try
		{
			await transport.SendFilamentCodeAsync(0, CancellationToken.None);
		}
		catch
		{
		}
		try
		{
			await transport.SendEndMeasurementAsync(CancellationToken.None);
		}
		catch
		{
		}
		double highestVoltage = Math.Max(Math.Max(profile.AnodeVoltage, profile.ScreenVoltage), profile.CurveVaStopV);
		int dischargeSeconds = DischargeTimeCalculator.CalculateSeconds(highestVoltage);
		for (int remaining = dischargeSeconds; remaining > 0; remaining--)
		{
			Report(progress, FullTestStage.Discharging, $"Rozładowanie: {remaining} s.", 92.0 + 7.0 * (double)(dischargeSeconds - remaining) / (double)Math.Max(1, dischargeSeconds), 0, 0, remaining);
			await DelayMillisecondsAsync(1000, options, CancellationToken.None);
		}
	}

	private static int ResolveCompliance(TubeProfile profile)
	{
		int requested = (int)Math.Ceiling(Math.Max(profile.AnodeComplianceMa, profile.ScreenComplianceMa));
		int num = new int[9] { 7, 12, 25, 50, 100, 125, 150, 175, 200 }.FirstOrDefault((int item) => item >= requested);
		if (num == 0)
		{
			throw new InvalidOperationException("Wymagany compliance przekracza 200 mA.");
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

	private static double AveragingDivisor(int index)
	{
		return index switch
		{
			0 => 1, 
			1 => 1, 
			2 => 2, 
			3 => 4, 
			4 => 8, 
			5 => 16, 
			6 => 32, 
			7 => 1, 
			_ => 1, 
		};
	}

	private double NextGaussian()
	{
		double d = 1.0 - _random.NextDouble();
		double num = 1.0 - _random.NextDouble();
		return Math.Sqrt(-2.0 * Math.Log(d)) * Math.Sin(Math.PI * 2.0 * num);
	}

	private static string NormalizeText(string? value, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return fallback;
	}

	private static void Report(IProgress<FullTestProgress>? progress, FullTestStage stage, string message, double percent, int CurrentSeries = 0, int MaximumSeries = 0, int? RemainingSeconds = null, FullTestSample? LatestSample = null, FullTestStatistics? CurrentStatistics = null)
	{
		progress?.Report(new FullTestProgress(stage, message, Math.Clamp(percent, 0.0, 100.0), CurrentSeries, MaximumSeries, RemainingSeconds, LatestSample, CurrentStatistics));
	}
}
