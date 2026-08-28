using System;
using System.Globalization;
using System.Linq;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Protocol;

public static class TracerProtocol
{
	public const int CommandLength = 18;

	public const int AdcResponseLength = 38;

	public const string EndOrPingCommand = "300000000000000000";

	public const string ReadAdcCommand = "500000000000000000";

	public static string BuildStartMeasurement(byte limits, byte averaging, byte screenGain, byte anodeGain)
	{
		return ValidateCommand($"0000000000{limits:X2}{averaging:X2}{screenGain:X2}{anodeGain:X2}");
	}

	public static string BuildGetMeasurement(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode)
	{
		return ValidateCommand($"10{anodeCode:X4}{screenCode:X4}{gridCode:X4}{filamentCode:X4}");
	}

	public static string BuildHoldMeasurement(ushort anodeCode, ushort screenCode, ushort gridCode, ushort filamentCode)
	{
		return ValidateCommand($"20{anodeCode:X4}{screenCode:X4}{gridCode:X4}{filamentCode:X4}");
	}

	public static string BuildEndMeasurement()
	{
		return "300000000000000000";
	}

	public static string BuildFilament(ushort filamentCode)
	{
		return ValidateCommand($"40000000000000{filamentCode:X4}");
	}

	public static string BuildReadAdc()
	{
		return "500000000000000000";
	}

	public static UTracerAdcPacket ParseAdcResponse(string response)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(response, "response");
		string text = response.Trim();
		if (text.Length != 38)
		{
			throw new FormatException($"Odpowiedź ADC musi mieć {38} znaków, otrzymano {text.Length}.");
		}
		if (!text.All(Uri.IsHexDigit))
		{
			throw new FormatException("Odpowiedź ADC zawiera znak spoza HEX.");
		}
		return new UTracerAdcPacket(text.Substring(0, 2), Word(text, 2), Word(text, 6), Word(text, 10), Word(text, 14), Word(text, 18), Word(text, 22), Word(text, 26), Word(text, 30), Byte(text, 34), Byte(text, 36), text);
	}

	public static string ValidateCommand(string command)
	{
		if (command.Length != 18)
		{
			throw new InvalidOperationException($"Polecenie ma {command.Length} znaków zamiast {18}: {command}");
		}
		if (!command.All(Uri.IsHexDigit))
		{
			throw new InvalidOperationException("Polecenie nie jest HEX ASCII: " + command);
		}
		return command.ToUpperInvariant();
	}

	private static ushort Word(string text, int start)
	{
		return ushort.Parse(text.Substring(start, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
	}

	private static byte Byte(string text, int start)
	{
		return byte.Parse(text.Substring(start, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
	}
}
