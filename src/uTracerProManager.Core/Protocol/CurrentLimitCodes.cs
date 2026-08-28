using System;

namespace uTracerProManager.Core.Protocol;

public static class CurrentLimitCodes
{
	public static byte ForMilliAmps(int milliAmps)
	{
		return milliAmps switch
		{
			200 => 143, 
			175 => 141, 
			150 => 173, 
			125 => 171, 
			100 => 132, 
			50 => 164, 
			25 => 162, 
			12 => 161, 
			7 => 128, 
			_ => throw new ArgumentOutOfRangeException("milliAmps", "Obsługiwane limity: 7, 12, 25, 50, 100, 125, 150, 175, 200 mA."), 
		};
	}
}
