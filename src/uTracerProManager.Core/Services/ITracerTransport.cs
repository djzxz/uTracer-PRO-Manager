using System;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public interface ITracerTransport : IAsyncDisposable
{
	bool IsConnected { get; }

	bool IsEmulator { get; }

	string ConnectionName { get; }

	event EventHandler<string>? LogMessage;

	Task ConnectAsync(string? endpoint, CancellationToken cancellationToken = default(CancellationToken));

	Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<string> PingAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<MeasurementResult> ReadAdcAsync(CalibrationProfile? calibration = null, AdcConversionOptions? options = null, CancellationToken cancellationToken = default(CancellationToken));

	Task SendStartMeasurementAsync(byte limits, byte averaging, byte screenGain, byte anodeGain, CancellationToken cancellationToken = default(CancellationToken));

	Task SendFilamentCodeAsync(ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken));

	Task SendHoldMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken));

	Task<MeasurementResult> ExecuteMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CalibrationProfile calibration, AdcConversionOptions options, CancellationToken cancellationToken = default(CancellationToken));

	Task SendEndMeasurementAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<MeasurementResult> RunEmulatedMeasurementAsync(TubeProfile profile, CancellationToken cancellationToken = default(CancellationToken));

	Task<MeasurementResult> RunNoTubeDiagnosticAsync(NoTubeDiagnosticRequest request, CalibrationProfile calibration, CancellationToken cancellationToken = default(CancellationToken));
}
