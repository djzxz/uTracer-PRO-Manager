using System;
using System.Threading;
using System.Threading.Tasks;

namespace uTracerProManager.Services;

public sealed class AutoPortDiscoveryService
{
	public async Task<ConnectedPortProbeResult?> FindAndConnectAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
	{
		string[] portNames = Win32SerialConnection.GetPortNames();
		if (portNames.Length == 0)
		{
			progress?.Report("Nie wykryto żadnego portu COM.");
			return null;
		}

		var failures = new List<string>();
		foreach (string port in portNames)
		{
			cancellationToken.ThrowIfCancellationRequested();
			progress?.Report("Sprawdzanie " + port + "…");
			SerialTracerTransport transport = new();
			transport.LogMessage += (_, message) => progress?.Report(port + ": " + message);
			try
			{
				using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				timeout.CancelAfter(TimeSpan.FromSeconds(45));
				await transport.ConnectAsync(port, timeout.Token);
				await transport.PingAsync(timeout.Token);
				await transport.ReadAdcAsync(null, null, timeout.Token);
				progress?.Report("Znaleziono uTracer na " + port + " i pozostawiono port otwarty.");
				var probe = new PortProbeResult(port, Identified: true, "PING/ECHO i odpowiedź ADC są prawidłowe.");
				return new ConnectedPortProbeResult(probe, transport);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await transport.DisposeAsync();
				throw;
			}
			catch (Exception ex)
			{
				var failure = port + ": " + ex.Message;
				failures.Add(failure);
				progress?.Report(failure);
				await transport.DisposeAsync();
			}
		}

		throw new IOException(
			"Porty COM zostały wykryte, lecz żaden nie przeszedł otwarcia, echa i odczytu ADC. " +
			string.Join(" | ", failures));
	}
}
