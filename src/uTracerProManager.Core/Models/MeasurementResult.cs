namespace uTracerProManager.Core.Models;

public sealed record MeasurementResult(UTracerAdcPacket Packet, EngineeringAdcReading? Engineering)
{
	public string StatusCode => Packet.StatusCode;

	public bool CurrentLimitHit => Packet.CurrentLimitHit;

	public string RawResponse => Packet.RawResponse;
}
