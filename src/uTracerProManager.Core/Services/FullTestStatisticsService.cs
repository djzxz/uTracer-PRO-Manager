using System;
using System.Collections.Generic;
using System.Linq;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Core.Services;

public sealed class FullTestStatisticsService
{
	public FullTestStatistics Calculate(TubeProfile profile, IReadOnlyList<FullTestSample> samples, FullTestOptions options)
	{
		return CalculateCore(profile, samples, options, (FullTestSample sample) => sample.AnodeCurrentMa, (FullTestSample sample) => sample.GmMaV, (FullTestSample sample) => sample.RpKohm, (FullTestSample sample) => sample.Mu, "A");
	}

	public FullTestStatistics CalculateSectionB(TubeProfile profile, IReadOnlyList<FullTestSample> samples, FullTestOptions options)
	{
		return CalculateCore(profile, samples, options, (FullTestSample sample) => sample.ScreenCurrentMa, (FullTestSample sample) => sample.SectionBGmMaV, (FullTestSample sample) => sample.SectionBRpKohm, (FullTestSample sample) => sample.SectionBMu, "B");
	}

	public DualSectionComparison CompareSections(FullTestStatistics a, FullTestStatistics b, TubeTestMode mode)
	{
		double num = DifferencePercent(a.MeanIaMa, b.MeanIaMa);
		double num2 = DifferencePercent(a.MeanGmMaV, b.MeanGmMaV);
		double num3 = DifferencePercent(a.MeanRpKohm, b.MeanRpKohm);
		double num4 = DifferencePercent(a.MeanMu, b.MeanMu);
		double num5 = ((mode == TubeTestMode.Quick) ? num : WeightedMismatch(num, num2, num3, num4));
		double overallMatchPercent = Math.Clamp(100.0 - num5, 0.0, 100.0);
		string text = ((num5 <= 5.0) ? "PREMIUM MATCHED" : ((num5 <= 10.0) ? "BARDZO DOBRZE DOBRANA" : ((num5 <= 15.0) ? "DOBRZE DOBRANA" : ((!(num5 <= 25.0)) ? "DUŻA ASYMETRIA" : "NIEDOBRANA"))));
		string grade = text;
		string recommendation = ((num5 <= 10.0) ? "Połówki są dobrze dopasowane." : ((num5 <= 25.0) ? "Połówki różnią się zauważalnie — sprawdź zastosowanie układowe." : "Duża asymetria. Sprawdź połączenia i wykonaj pełną diagnostykę."));
		return new DualSectionComparison(num, num2, num3, num4, overallMatchPercent, grade, recommendation);
	}

	public IReadOnlyList<int> DetectOutlierSequences(IReadOnlyList<FullTestSample> samples, bool includeSectionB = false)
	{
		FullTestSample[] array = (from sample in samples
			where !sample.Conditioning
			orderby sample.Sequence
			select sample).ToArray();
		if (array.Length < 5)
		{
			return Array.Empty<int>();
		}
		bool[] iaA = DetectOutliers(array.Select((FullTestSample x) => x.AnodeCurrentMa).ToArray());
		bool[] gmA = DetectOutliers(array.Select((FullTestSample x) => x.GmMaV).ToArray());
		bool[] iaB = (includeSectionB ? DetectOutliers(array.Select((FullTestSample x) => x.ScreenCurrentMa).ToArray()) : new bool[array.Length]);
		bool[] gmB = (includeSectionB ? DetectOutliers(array.Select((FullTestSample x) => x.SectionBGmMaV).ToArray()) : new bool[array.Length]);
		return (from sample in array.Where((FullTestSample sample, int index) => iaA[index] || gmA[index] || iaB[index] || gmB[index])
			select sample.Sequence).ToArray();
	}

	private static FullTestStatistics CalculateCore(TubeProfile profile, IReadOnlyList<FullTestSample> samples, FullTestOptions options, Func<FullTestSample, double> iaSelector, Func<FullTestSample, double> gmSelector, Func<FullTestSample, double> rpSelector, Func<FullTestSample, double> muSelector, string sectionName)
	{
		FullTestSample[] array = (from sample in samples
			where !sample.Conditioning
			orderby sample.Sequence
			select sample).ToArray();
		if (array.Length == 0)
		{
			return Empty();
		}
		bool flag = options.TestMode == TubeTestMode.Quick || !options.MeasureDynamicParameters;
		double[] values = array.Select(iaSelector).ToArray();
		double[] values2 = array.Select(gmSelector).ToArray();
		bool[] iaOutliers = DetectOutliers(values);
		bool[] gmOutliers = (flag ? new bool[array.Length] : DetectOutliers(values2));
		FullTestSample[] array2 = array.Where((FullTestSample sample, int index) => !iaOutliers[index] && !gmOutliers[index]).ToArray();
		if (array2.Length == 0)
		{
			return Empty()with
			{
				TotalSeries = array.Length,
				Outliers = array.Length,
				Reliability = "Brak wiarygodnych próbek połówki " + sectionName,
				Grade = "NIEPRAWIDŁOWY",
				Recommendation = "Sprawdź podstawkę, styki, pinout i powtórz test."
			};
		}
		double num = Mean(array2.Select(iaSelector));
		double num2 = StdDev(array2.Select(iaSelector));
		double num3 = CvPercent(num, num2);
		double num4 = (flag ? 0.0 : Mean(array2.Select(gmSelector)));
		double num5 = (flag ? 0.0 : StdDev(array2.Select(gmSelector)));
		double num6 = (flag ? 0.0 : CvPercent(num4, num5));
		double meanRpKohm = (flag ? 0.0 : Mean(array2.Select(rpSelector)));
		double meanMu = (flag ? 0.0 : Mean(array2.Select(muSelector)));
		double num7 = ((array2.Length < 2) ? 0.0 : LastStepDrift(array2.Select(iaSelector).ToArray()));
		double num8 = ((flag || array2.Length < 2) ? 0.0 : LastStepDrift(array2.Select(gmSelector).ToArray()));
		double num9 = (profile.CountsForConditionPercent ? PercentOfNominal(num, profile.NominalAnodeCurrentMa) : 0.0);
		double num10 = ((profile.CountsForConditionPercent && !flag) ? PercentOfNominal(num4, profile.NominalGmMaV) : 0.0);
		double num11 = ((!profile.CountsForConditionPercent) ? 0.0 : (flag ? Math.Clamp(num9, 0.0, 130.0) : WeightedCondition(num9, num10)));
		bool flag2 = array2.Length >= options.MinimumValidSeries && num3 <= options.MaxIaCvPercent && num7 <= options.MaxStepDriftPercent && (flag || (num6 <= options.MaxGmCvPercent && num8 <= options.MaxStepDriftPercent));
		string reliability = ((!flag2) ? ((array.Length >= options.MaximumSeries) ? "NIESTABILNY — osiągnięto limit powtórzeń" : "W TRAKCIE — wymagane kolejne serie") : (flag ? "SZYBKI — pojedynczy skorygowany punkt" : "WIARYGODNY — wynik ustabilizowany"));
		string grade = (profile.CountsForConditionPercent ? Grade(num11, flag2) : (flag2 ? "PORÓWNAWCZY" : "NIESTABILNY"));
		string recommendation = (profile.CountsForConditionPercent ? Recommendation(flag2, flag, num3, num6, num7, num8, num11) : (flag2 ? "Profil porównawczy — wynik zapisano bez procentowej oceny kondycji." : "Profil porównawczy nie ustabilizował się. Sprawdź styki i powtórz."));
		return new FullTestStatistics(array.Length, array2.Length, array.Length - array2.Length, num, num2, num3, num4, num5, num6, meanRpKohm, meanMu, num7, num8, num9, num10, num11, flag2, reliability, grade, recommendation);
	}

	private static FullTestStatistics Empty()
	{
		return new FullTestStatistics(0, 0, 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, Stable: false, "Brak danych", "BRAK", "Wykonaj pomiar.");
	}

	private static double Mean(IEnumerable<double> values)
	{
		double[] array = values.Where(double.IsFinite).ToArray();
		if (array.Length != 0)
		{
			return array.Average();
		}
		return 0.0;
	}

	private static double StdDev(IEnumerable<double> values)
	{
		double[] array = values.Where(double.IsFinite).ToArray();
		if (array.Length < 2)
		{
			return 0.0;
		}
		double mean = array.Average();
		return Math.Sqrt(array.Sum((double value) => Math.Pow(value - mean, 2.0)) / (double)(array.Length - 1));
	}

	private static double CvPercent(double mean, double sd)
	{
		if (!(Math.Abs(mean) < 1E-12))
		{
			return Math.Abs(sd / mean) * 100.0;
		}
		return 0.0;
	}

	private static double LastStepDrift(IReadOnlyList<double> values)
	{
		if (values.Count < 2)
		{
			return 0.0;
		}
		double num = values[values.Count - 2];
		double num2 = values[values.Count - 1];
		double num3 = (Math.Abs(num) + Math.Abs(num2)) / 2.0;
		if (!(num3 < 1E-12))
		{
			return Math.Abs(num2 - num) / num3 * 100.0;
		}
		return 0.0;
	}

	private static double PercentOfNominal(double measured, double nominal)
	{
		if (!(nominal <= 0.0))
		{
			return measured / nominal * 100.0;
		}
		return 100.0;
	}

	private static double WeightedCondition(double iaPercent, double gmPercent)
	{
		double num = Math.Clamp(iaPercent, 0.0, 130.0);
		double num2 = Math.Clamp(gmPercent, 0.0, 130.0);
		return 0.45 * num + 0.55 * num2;
	}

	private static string Grade(double condition, bool stable)
	{
		if (!stable)
		{
			return "NIESTABILNY";
		}
		if (!(condition >= 100.0))
		{
			if (!(condition >= 90.0))
			{
				if (!(condition >= 80.0))
				{
					if (condition >= 65.0)
					{
						return "UŻYWANA / ŚREDNIA";
					}
					return "SŁABA";
				}
				return "DOBRA";
			}
			return "BARDZO DOBRA";
		}
		return "BARDZO MOCNA";
	}

	private static string Recommendation(bool stable, bool quick, double cvIa, double cvGm, double driftIa, double driftGm, double condition)
	{
		if (quick)
		{
			if (!(condition >= 80.0))
			{
				return "Szybki test wykazał osłabienie. Uruchom pełną diagnostykę.";
			}
			return "Szybka selekcja pozytywna. Dla dokładnej oceny uruchom tryb normalny lub pełny.";
		}
		if (!stable)
		{
			if (driftIa > 1.5 || driftGm > 1.5)
			{
				return "Lampa nadal dryfuje termicznie. Powtórz pełną diagnostykę.";
			}
			if (cvIa > 2.0 || cvGm > 2.5)
			{
				return "Duży rozrzut. Sprawdź podstawkę, styki, mikrofonowanie i powtórz.";
			}
			return "Wynik nie spełnił kryteriów stabilności.";
		}
		if (condition >= 90.0)
		{
			return "Wynik stabilny. Lampa nadaje się do dalszej selekcji i parowania.";
		}
		if (condition >= 65.0)
		{
			return "Wynik stabilny, ale emisja jest obniżona. Zapisz jako lampę używaną.";
		}
		return "Wynik stabilny, lecz niski. Lampa słaba — zastosowanie ograniczone.";
	}

	private static bool[] DetectOutliers(IReadOnlyList<double> values)
	{
		bool[] array = new bool[values.Count];
		if (values.Count < 5)
		{
			return array;
		}
		double median = Median(values);
		double num = Median(values.Select((double value) => Math.Abs(value - median)).ToArray());
		if (num < 1E-12)
		{
			return array;
		}
		double num2 = 4.4478 * num;
		for (int num3 = 0; num3 < values.Count; num3++)
		{
			array[num3] = Math.Abs(values[num3] - median) > num2;
		}
		return array;
	}

	private static double Median(IReadOnlyList<double> values)
	{
		double[] array = values.OrderBy((double value) => value).ToArray();
		int num = array.Length / 2;
		if (array.Length % 2 != 0)
		{
			return array[num];
		}
		return (array[num - 1] + array[num]) / 2.0;
	}

	private static double DifferencePercent(double a, double b)
	{
		double num = (Math.Abs(a) + Math.Abs(b)) / 2.0;
		if (!(num < 1E-12))
		{
			return Math.Abs(a - b) / num * 100.0;
		}
		return 0.0;
	}

	private static double WeightedMismatch(double ia, double gm, double rp, double mu)
	{
		double num = (double.IsFinite(rp) ? rp : 100.0);
		double num2 = (double.IsFinite(mu) ? mu : 100.0);
		return 0.4 * ia + 0.4 * gm + 0.1 * num + 0.1 * num2;
	}
}
