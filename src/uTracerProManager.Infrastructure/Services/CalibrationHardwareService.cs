using System;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;

namespace uTracerProManager.Services;

public sealed class CalibrationHardwareService
{
	private readonly SerialTracerTransport _transport;

	public CalibrationHardwareService(SerialTracerTransport transport)
	{
		_transport = transport;
	}

	public async Task<MeasurementResult> ReadIdleAsync(CalibrationProfile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await _transport.ReadAdcAsync(profile, new AdcConversionOptions(), cancellationToken);
	}

	public async Task HoldGridPointAsync(CalibrationProfile profile, double gridVoltage, CancellationToken cancellationToken = default(CancellationToken))
	{
		double supplyVoltage = ((await ReadIdleAsync(profile, cancellationToken)).Engineering ?? throw new InvalidOperationException("Nie udało się wyliczyć Vsu.")).SupplyVoltage;
		ushort va = CommandCodeConverter.AnodeCode(4.0, supplyVoltage, profile);
		ushort vs = CommandCodeConverter.ScreenCode(4.0, supplyVoltage, profile);
		ushort vg = CommandCodeConverter.GridCode(gridVoltage, profile, calibrationMode: true);
		await _transport.SendFilamentCodeAsync(0, cancellationToken);
		await _transport.SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(7), 16, 8, 8, cancellationToken);
		await _transport.SendHoldMeasurementAsync(va, vs, vg, 0, cancellationToken);
	}

	public async Task HoldBoostPointAsync(CalibrationProfile profile, bool anode, double targetVoltage, CancellationToken cancellationToken = default(CancellationToken))
	{
		double supplyVoltage = ((await ReadIdleAsync(profile, cancellationToken)).Engineering ?? throw new InvalidOperationException("Nie udało się wyliczyć Vsu.")).SupplyVoltage;
		ushort va = CommandCodeConverter.AnodeCode(anode ? targetVoltage : 20.0, supplyVoltage, profile);
		ushort vs = CommandCodeConverter.ScreenCode(anode ? 20.0 : targetVoltage, supplyVoltage, profile);
		await _transport.SendFilamentCodeAsync(0, cancellationToken);
		await _transport.SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(7), 16, 8, 8, cancellationToken);
		await _transport.SendHoldMeasurementAsync(va, vs, 0, 0, cancellationToken);
	}

	public async Task<MeasurementResult> MeasureCurrentResistorsAsync(CalibrationProfile profile, double targetVoltage, CancellationToken cancellationToken = default(CancellationToken))
	{
		double supplyVoltage = ((await ReadIdleAsync(profile, cancellationToken)).Engineering ?? throw new InvalidOperationException("Nie udało się wyliczyć Vsu.")).SupplyVoltage;
		ushort va = CommandCodeConverter.AnodeCode(targetVoltage, supplyVoltage, profile);
		ushort vs = CommandCodeConverter.ScreenCode(targetVoltage, supplyVoltage, profile);
		ushort vg = CommandCodeConverter.GridCode(-1.0, profile, calibrationMode: true);
		await _transport.SendFilamentCodeAsync(0, cancellationToken);
		await _transport.SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(25), 4, 8, 8, cancellationToken);
		MeasurementResult result;
		try
		{
			result = await _transport.ExecuteMeasurementAsync(va, vs, vg, 0, profile, new AdcConversionOptions(3, targetVoltage, targetVoltage, -1.0), cancellationToken);
		}
		finally
		{
			await SafeStopAsync();
		}
		return result;
	}

	public async Task SafeStopAsync()
	{
		try
		{
			await _transport.SendFilamentCodeAsync(0, CancellationToken.None);
		}
		catch
		{
		}
		try
		{
			await _transport.SendEndMeasurementAsync(CancellationToken.None);
		}
		catch
		{
		}
		await Task.Delay(1500);
	}
}
