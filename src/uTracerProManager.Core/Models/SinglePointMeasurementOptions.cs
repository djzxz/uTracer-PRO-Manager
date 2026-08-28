using System;

namespace uTracerProManager.Core.Models;

public sealed record SinglePointMeasurementOptions(int WarmupSeconds, bool AccelerateEmulator = true, int EmulatorSpeedMultiplier = 50, int AveragingIndex = 0, int HoldMilliseconds = 0)
{
	public void Validate(bool emulator)
	{
		int num = (emulator ? 1 : 60);
		if (WarmupSeconds < num || WarmupSeconds > 1800)
		{
			throw new ArgumentOutOfRangeException("WarmupSeconds", $"Czas rozgrzewania musi wynosić {num}–1800 s.");
		}
		int emulatorSpeedMultiplier = EmulatorSpeedMultiplier;
		if ((emulatorSpeedMultiplier < 1 || emulatorSpeedMultiplier > 200) ? true : false)
		{
			throw new ArgumentOutOfRangeException("EmulatorSpeedMultiplier", "Przyspieszenie emulatora musi wynosić 1–200.");
		}
		emulatorSpeedMultiplier = AveragingIndex;
		if ((emulatorSpeedMultiplier < 0 || emulatorSpeedMultiplier > 7) ? true : false)
		{
			throw new ArgumentOutOfRangeException("AveragingIndex", "Indeks uśredniania musi wynosić 0–7.");
		}
		emulatorSpeedMultiplier = HoldMilliseconds;
		if ((emulatorSpeedMultiplier < 0 || emulatorSpeedMultiplier > 5000) ? true : false)
		{
			throw new ArgumentOutOfRangeException("HoldMilliseconds", "Czas hold musi wynosić 0–5000 ms.");
		}
	}
}
