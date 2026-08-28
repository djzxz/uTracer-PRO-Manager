namespace uTracerProManager.Core.Models;

public sealed record UTracerAdcPacket(string StatusCode, ushort Ia, ushort IaRaw, ushort Is, ushort IsRaw, ushort Va, ushort Vs, ushort Vsu, ushort Vn, byte AnodeGain, byte ScreenGain, string RawResponse)
{
	public bool CurrentLimitHit => StatusCode == "11";
}
