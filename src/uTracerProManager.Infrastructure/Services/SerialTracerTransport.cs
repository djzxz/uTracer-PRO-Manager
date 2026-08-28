using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Protocol;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed class SerialTracerTransport : ITracerTransport, IAsyncDisposable
{
	private readonly SemaphoreSlim _commandLock = new SemaphoreSlim(1, 1);

	private Win32SerialConnection? _port;

	public bool IsConnected => _port?.IsOpen ?? false;

	public bool IsEmulator => false;

	public string ConnectionName => _port is { } port
		? $"{port.PortName} • {port.OpenProfileName}"
		: "Port szeregowy";

	public event EventHandler<string>? LogMessage;

	public async Task ConnectAsync(string? endpoint, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (string.IsNullOrWhiteSpace(endpoint))
		{
			throw new ArgumentException("Nie podano portu COM.", "endpoint");
		}
		if (_port != null)
		{
			throw new InvalidOperationException("Port jest już skonfigurowany.");
		}

		var failures = new List<string>();
		Exception? lastError = null;
		foreach (SerialOpenProfile profile in Win32SerialConnection.OpenProfiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var connection = new Win32SerialConnection();
			var profileName = Win32SerialConnection.GetProfileName(profile);
			Log($"Próba otwarcia {endpoint}: {profileName}.");
			try
			{
				await Task.Run(() => connection.Open(endpoint, profile), cancellationToken);
				_port = connection;
				Log($"Otwarto {connection.PortName}: 9600 8N1; profil „{connection.OpenProfileName}”.");

				// ESC i END są bezpiecznym testem echa: nie uruchamiają napięć pomiarowych.
				connection.TrySendEscape();
				await SendEndMeasurementAsync(cancellationToken);
				Log($"Autotest echa OK. {connection.PortName} pozostaje otwarty w profilu „{connection.OpenProfileName}”.");
				return;
			}
			catch (OperationCanceledException)
			{
				Interlocked.CompareExchange(ref _port, null, connection);
				connection.TrySendEscape();
				connection.Dispose();
				throw;
			}
			catch (SerialProfileNotApplicableException ex)
			{
				lastError = ex;
				failures.Add(ex.Message);
				Log(ex.Message);
			}
			catch (Exception ex)
			{
				lastError = ex;
				var failure = $"{profileName}: {ex.Message}";
				failures.Add(failure);
				Log("Nieudana " + failure);
				Interlocked.CompareExchange(ref _port, null, connection);
				connection.TrySendEscape();
				if (ex is SerialPortOpenException { IsFatalBeforeConfiguration: true })
				{
					break;
				}
			}
			finally
			{
				if (!ReferenceEquals(_port, connection))
					connection.Dispose();
			}
		}

		throw new IOException(
			$"Port {endpoint} jest widoczny, ale nie przeszedł otwarcia i bezpiecznego testu echa. " +
			"Program nie wysłał poleceń pomiarowych. Próby: " + string.Join(" | ", failures),
			lastError);
	}

	public async Task DisconnectAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (_port == null)
		{
			return;
		}

		using CancellationTokenSource shutdownTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		shutdownTimeout.CancelAfter(TimeSpan.FromSeconds(8));
		await TrySafeShutdownAsync(shutdownTimeout.Token);

		Win32SerialConnection? port = Interlocked.Exchange(ref _port, null);
		port?.Dispose();
		Log("Port szeregowy zamknięty.");
	}

	public async Task<string> PingAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await SendEndMeasurementAsync(cancellationToken);
		return "PING / ECHO OK";
	}

	public async Task<MeasurementResult> ReadAdcAsync(CalibrationProfile? calibration = null, AdcConversionOptions? options = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		UTracerAdcPacket packet = await ReadAdcPacketAsync(cancellationToken);
		EngineeringAdcReading engineering = null;
		if (calibration?.IsValidForHardwareDiagnostics ?? false)
		{
			engineering = AdcEngineeringConverter.Convert(packet, calibration, options ?? new AdcConversionOptions());
		}
		return new MeasurementResult(packet, engineering);
	}

	public async Task SendStartMeasurementAsync(byte limits, byte averaging, byte screenGain, byte anodeGain, CancellationToken cancellationToken = default(CancellationToken))
	{
		await SendCommandAsync(TracerProtocol.BuildStartMeasurement(limits, averaging, screenGain, anodeGain), 0, cancellationToken);
	}

	public async Task SendFilamentCodeAsync(ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (filamentCode > 1023)
		{
			throw new ArgumentOutOfRangeException("filamentCode", "Kod żarzenia musi wynosić 0–1023.");
		}
		await SendCommandAsync(TracerProtocol.BuildFilament(filamentCode), 0, cancellationToken);
	}

	public async Task SendHoldMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CancellationToken cancellationToken = default(CancellationToken))
	{
		await SendCommandAsync(TracerProtocol.BuildHoldMeasurement(anodeCode, screenCode, gridCode, filamentCode), 0, cancellationToken);
	}

	public async Task<MeasurementResult> ExecuteMeasurementAsync(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode, CalibrationProfile calibration, AdcConversionOptions options, CancellationToken cancellationToken = default(CancellationToken))
	{
		UTracerAdcPacket packet = TracerProtocol.ParseAdcResponse(await SendCommandAsync(TracerProtocol.BuildGetMeasurement(anodeCode, screenCode, gridCode, filamentCode), 38, cancellationToken));
		EngineeringAdcReading engineering = AdcEngineeringConverter.Convert(packet, calibration, options);
		return new MeasurementResult(packet, engineering);
	}

	public async Task SendEndMeasurementAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await SendCommandAsync(TracerProtocol.BuildEndMeasurement(), 0, cancellationToken);
	}

	/// <summary>
	/// Przerywa niedokończoną ramkę protokołu tak samo jak przycisk "Send esc"
	/// w oryginalnym GUI u-Tracer. Port pozostaje otwarty.
	/// </summary>
	public async Task SendEscapeAsync(CancellationToken cancellationToken = default)
	{
		Win32SerialConnection port = RequirePort();
		await _commandLock.WaitAsync(cancellationToken);
		try
		{
			await Task.Run(port.TrySendEscape, cancellationToken);
			Log("TX ESC — przerwano niedokończoną komendę; port pozostaje otwarty.");
		}
		finally
		{
			_commandLock.Release();
		}
	}

	public Task<MeasurementResult> RunEmulatedMeasurementAsync(TubeProfile profile, CancellationToken cancellationToken = default(CancellationToken))
	{
		throw new NotSupportedException("Ta metoda jest dostępna tylko w emulatorze.");
	}

	public async Task<MeasurementResult> RunNoTubeDiagnosticAsync(NoTubeDiagnosticRequest request, CalibrationProfile calibration, CancellationToken cancellationToken = default(CancellationToken))
	{
		request.Validate();
		IReadOnlyList<string> readOnlyList = calibration.Validate();
		if (readOnlyList.Count > 0)
		{
			throw new InvalidOperationException("Kalibracja nie pozwala uruchomić diagnostyki:\n- " + string.Join("\n- ", readOnlyList));
		}
		double supplyVoltage = ((await ReadAdcAsync(calibration, new AdcConversionOptions(), cancellationToken)).Engineering ?? throw new InvalidOperationException("Nie udało się wyliczyć Vsu.")).SupplyVoltage;
		if ((supplyVoltage < 10.0 || supplyVoltage > 25.0) ? true : false)
		{
			throw new InvalidOperationException($"Vsu={supplyVoltage:F2} V jest poza zakresem 10–25 V.");
		}
		ushort vaCode = CommandCodeConverter.AnodeCode(request.AnodeVoltage, supplyVoltage, calibration);
		ushort vsCode = CommandCodeConverter.ScreenCode(request.ScreenVoltage, supplyVoltage, calibration);
		ushort vgCode = CommandCodeConverter.GridCode(0.0, calibration);
		UTracerAdcPacket packet = null;
		try
		{
			await SendFilamentCodeAsync(0, cancellationToken);
			await SendStartMeasurementAsync(CurrentLimitCodes.ForMilliAmps(7), 64, 8, 8, cancellationToken);
			packet = (await ExecuteMeasurementAsync(vaCode, vsCode, vgCode, 0, calibration, new AdcConversionOptions(0, request.AnodeVoltage, request.ScreenVoltage), cancellationToken)).Packet;
		}
		finally
		{
			await TrySafeShutdownAsync();
		}
		if ((object)packet == null)
		{
			throw new InvalidOperationException("Nie odebrano wyniku diagnostyki.");
		}
		EngineeringAdcReading engineering = AdcEngineeringConverter.Convert(packet, calibration, new AdcConversionOptions(0, request.AnodeVoltage, request.ScreenVoltage));
		if (packet.CurrentLimitHit)
		{
			throw new InvalidOperationException("Status 11 — zadziałało ograniczenie prądowe.");
		}
		return new MeasurementResult(packet, engineering);
	}

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
		_commandLock.Dispose();
	}

	private async Task<UTracerAdcPacket> ReadAdcPacketAsync(CancellationToken cancellationToken)
	{
		return TracerProtocol.ParseAdcResponse(await SendCommandAsync(TracerProtocol.BuildReadAdc(), 38, cancellationToken));
	}

	private async Task TrySafeShutdownAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			if (IsConnected)
			{
				await SendFilamentCodeAsync(0, cancellationToken);
				await SendEndMeasurementAsync(cancellationToken);
				Log("Bezpieczne wyłączenie: HEATER 0 + END.");
			}
		}
		catch (Exception ex)
		{
			_port?.TrySendEscape();
			Log("UWAGA: błąd bezpiecznego wyłączania: " + ex.Message);
		}
	}

	private async Task<string> SendCommandAsync(string command, int expectedResponseLength, CancellationToken cancellationToken)
	{
		Win32SerialConnection port = RequirePort();
		TracerProtocol.ValidateCommand(command);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(35.0));
		CancellationToken token = timeout.Token;
		await _commandLock.WaitAsync(token);
		try
		{
			port.DiscardBuffers();
			Log("TX " + command);
			string text = await Task.Run(delegate
			{
				string text2 = command;
				foreach (char num in text2)
				{
					token.ThrowIfCancellationRequested();
					byte b = checked((byte)num);
					port.WriteByte(b, token);
					byte b2 = port.ReadByte(token);
					if (b2 != b)
					{
						throw new IOException($"Błąd echa: wysłano 0x{b:X2}, odebrano 0x{b2:X2}.");
					}
				}
				if (expectedResponseLength == 0)
				{
					return string.Empty;
				}
				byte[] bytes = port.ReadExact(expectedResponseLength, token);
				return Encoding.ASCII.GetString(bytes);
			}, token);
			if (expectedResponseLength == 0)
			{
				Log("RX ECHO OK.");
				return string.Empty;
			}
			Log("RX " + text);
			return text;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			RecoverProtocol(port);
			throw new TimeoutException("Upłynął czas oczekiwania na odpowiedź uTracera.");
		}
		catch
		{
			RecoverProtocol(port);
			throw;
		}
		finally
		{
			_commandLock.Release();
		}
	}

	private void RecoverProtocol(Win32SerialConnection port)
	{
		port.TrySendEscape();
		Log("TX ESC — automatyczne odzyskanie protokołu po błędzie; port nie został zamknięty.");
	}

	private Win32SerialConnection RequirePort()
	{
		if (_port == null || !_port.IsOpen)
		{
			throw new InvalidOperationException("Port COM nie jest otwarty.");
		}
		return _port;
	}

	private void Log(string message)
	{
		this.LogMessage?.Invoke(this, message);
	}
}
