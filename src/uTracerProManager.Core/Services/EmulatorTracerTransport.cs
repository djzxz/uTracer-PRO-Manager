using System;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;

namespace uTracerProManager.Core.Services;

public sealed class EmulatorTracerTransport : ITracerTransport, IAsyncDisposable
{
	private readonly Random _random = new Random(20260727);

	private bool _measurementConfigured;

	private ushort _filamentCode;

	public bool IsConnected { get; private set; }

	public bool IsEmulator => true;

	public string ConnectionName => "Emulator uTracer 3+";

	public event EventHandler<string>? LogMessage;

	public Task ConnectAsync(string? endpoint, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		IsConnected = true;
		Log("Emulator połączony.");
		return Task.CompletedTask;
	}

	public Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		IsConnected = false;
		_measurementConfigured = false;
		_filamentCode = 0;
		Log("Emulator rozłączony.");
		return Task.CompletedTask;
	}

	public Task<string> PingAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		Log("Emulator: PING / ECHO OK.");
		return Task.FromResult("PING / ECHO OK");
	}

	public Task<MeasurementResult> ReadAdcAsync(CalibrationProfile? calibration = null, AdcConversionOptions? options = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		UTracerAdcPacket uTracerAdcPacket = CreatePacket(0, 0, 0, 0, 820, 820, 0, 0);
		EngineeringAdcReading engineering = null;
		if ((object)calibration != null && calibration.IsValidForHardwareDiagnostics)
		{
			engineering = AdcEngineeringConverter.Convert(uTracerAdcPacket, calibration, options ?? new AdcConversionOptions());
		}
		Log("Emulator ADC: " + uTracerAdcPacket.RawResponse);
		return Task.FromResult(new MeasurementResult(uTracerAdcPacket, engineering));
	}

	public Task SendStartMeasurementAsync(byte limits, byte averaging, byte screenGain, byte anodeGain, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		_measurementConfigured = true;
		string text = TracerProtocol.BuildStartMeasurement(limits, averaging, screenGain, anodeGain);
		Log("Emulator TX START: " + text);
		return Task.CompletedTask;
	}

	public Task SendFilamentCodeAsync(ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		_filamentCode = filamentCode;
		Log("Emulator TX HEATER: " + TracerProtocol.BuildFilament(filamentCode));
		return Task.CompletedTask;
	}

	public Task SendHoldMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		if (!_measurementConfigured)
		{
			throw new InvalidOperationException("Najpierw wyślij START.");
		}
		string text = TracerProtocol.BuildHoldMeasurement(anodeCode, screenCode, gridCode, filamentCode);
		Log("Emulator TX HOLD: " + text);
		return Task.CompletedTask;
	}

	public Task<MeasurementResult> ExecuteMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CalibrationProfile calibration, AdcConversionOptions options, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		if (!_measurementConfigured)
		{
			throw new InvalidOperationException("Emulator: brak START.");
		}
		Log("Emulator TX MEASURE: " + TracerProtocol.BuildGetMeasurement(anodeCode, screenCode, gridCode, filamentCode));
		double commandedAnodeVoltage = options.CommandedAnodeVoltage;
		double commandedScreenVoltage = options.CommandedScreenVoltage;
		double num = Math.Max(0.0, commandedAnodeVoltage - 1.8);
		double num2 = Math.Max(0.0, commandedScreenVoltage - 1.2);
		double num3 = Math.Max(0.0, commandedAnodeVoltage / 250.0 * 5.0 * (1.0 + (_random.NextDouble() - 0.5) * 0.08));
		double num4 = ((commandedScreenVoltage > 0.0) ? Math.Max(0.0, num3 * 0.12) : 0.0);
		UTracerAdcPacket packet = CreatePacket(ToWord(num3 * 20.0), ToWord(num4 * 20.0), ToWord(num * 2.0), ToWord(num2 * 2.0), 820, 820, 0, 0);
		EngineeringAdcReading engineering = new EngineeringAdcReading(num3, num3, num4, num4, 19.2, -40.0, num, num2, commandedAnodeVoltage, commandedScreenVoltage, options.CommandedGridVoltage, 0, 0, 1.0);
		return Task.FromResult(new MeasurementResult(packet, engineering));
	}

	public Task SendEndMeasurementAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		_measurementConfigured = false;
		Log("Emulator TX END: " + TracerProtocol.BuildEndMeasurement());
		return Task.CompletedTask;
	}

	public Task<MeasurementResult> RunEmulatedMeasurementAsync(TubeProfile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		cancellationToken.ThrowIfCancellationRequested();
		double num = 1.0 + (_random.NextDouble() - 0.5) * 0.08;
		double num2 = 1.0 + (_random.NextDouble() - 0.5) * 0.1;
		double num3 = Math.Max(0.0, profile.NominalAnodeCurrentMa * num);
		double num4 = Math.Max(0.0, profile.NominalScreenCurrentMa * num2);
		UTracerAdcPacket packet = CreatePacket(ToWord(num3 * 20.0), ToWord(num4 * 20.0), ToWord(Math.Max(0.0, profile.AnodeVoltage - 1.8) * 2.0), ToWord(Math.Max(0.0, profile.ScreenVoltage - 1.2) * 2.0), 820, 820, 0, 0);
		EngineeringAdcReading engineering = new EngineeringAdcReading(num3, num3, num4, num4, 19.2, -40.0, Math.Max(0.0, profile.AnodeVoltage - 1.8), Math.Max(0.0, profile.ScreenVoltage - 1.2), profile.AnodeVoltage, profile.ScreenVoltage, profile.GridVoltage, 0, 0, 1.0);
		return Task.FromResult(new MeasurementResult(packet, engineering));
	}

	public Task<MeasurementResult> RunNoTubeDiagnosticAsync(NoTubeDiagnosticRequest request, CalibrationProfile calibration, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureConnected();
		request.Validate();
		cancellationToken.ThrowIfCancellationRequested();
		UTracerAdcPacket packet = CreatePacket(1, 1, ToWord(request.AnodeVoltage * 2.7), ToWord(request.ScreenVoltage * 2.7), 820, 820, 0, 0);
		EngineeringAdcReading engineering = AdcEngineeringConverter.Convert(packet, calibration, new AdcConversionOptions(0, request.AnodeVoltage, request.ScreenVoltage));
		return Task.FromResult(new MeasurementResult(packet, engineering));
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
	}

	private static UTracerAdcPacket CreatePacket(ushort iaRaw, ushort isRaw, ushort vaRaw, ushort vsRaw, ushort vsuRaw, ushort vnRaw, byte anodeGain, byte screenGain)
	{
		return TracerProtocol.ParseAdcResponse("10" + iaRaw.ToString("X4") + iaRaw.ToString("X4") + isRaw.ToString("X4") + isRaw.ToString("X4") + vaRaw.ToString("X4") + vsRaw.ToString("X4") + vsuRaw.ToString("X4") + vnRaw.ToString("X4") + anodeGain.ToString("X2") + screenGain.ToString("X2"));
	}

	private static ushort ToWord(double value)
	{
		return checked((ushort)Math.Clamp(Math.Round(value), 0.0, 65535.0));
	}

	private void EnsureConnected()
	{
		if (!IsConnected)
		{
			throw new InvalidOperationException("Emulator nie jest połączony.");
		}
	}

	private void Log(string message)
	{
		this.LogMessage?.Invoke(this, message);
	}
}
