using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using uTracerProManager.Core.Models;

namespace uTracerProManager.Services;

public sealed class FullTestChartService
{
	public FullTestChartFiles CreateCharts(FullTestResult result, string directory)
	{
		ArgumentNullException.ThrowIfNull(result, "result");
		Directory.CreateDirectory(directory);
		FullTestSample[] array = (from sample in result.Samples
			where !sample.Conditioning
			orderby sample.Sequence
			select sample).ToArray();
		if (array.Length == 0)
		{
			throw new InvalidOperationException("Brak próbek do wykresu.");
		}
		double[] x = ((IEnumerable<FullTestSample>)array).Select((Func<FullTestSample, double>)((FullTestSample sample) => sample.Sequence)).ToArray();
		bool flag = (object)result.SectionBStatistics != null;
		string text = Path.Combine(directory, "wykres_Ia_AB.png");
		string text2 = Path.Combine(directory, "wykres_gm_AB.png");
		string text3 = Path.Combine(directory, "wykres_kondycja_AB.png");
		CreateLineChart(text, "Stabilność prądu anodowego", "Seria", "Ia [mA]", x, array.Select((FullTestSample sample) => sample.AnodeCurrentMa).ToArray(), "Połówka A", result.Profile.NominalAnodeCurrentMa, flag ? array.Select((FullTestSample sample) => sample.ScreenCurrentMa).ToArray() : null, "Połówka B");
		CreateLineChart(text2, (result.TestMode == TubeTestMode.Quick) ? "Szybki test — gm nie jest mierzone" : "Stabilność nachylenia gm", "Seria", "gm [mA/V]", x, array.Select((FullTestSample sample) => sample.GmMaV).ToArray(), "Połówka A", (result.TestMode == TubeTestMode.Quick) ? 0.0 : result.Profile.NominalGmMaV, flag ? array.Select((FullTestSample sample) => sample.SectionBGmMaV).ToArray() : null, "Połówka B");
		double[] first = array.Select((FullTestSample sample) => ConditionPercent(result, sample.AnodeCurrentMa, sample.GmMaV)).ToArray();
		double[] second = (flag ? array.Select((FullTestSample sample) => ConditionPercent(result, sample.ScreenCurrentMa, sample.SectionBGmMaV)).ToArray() : null);
		CreateLineChart(text3, "Ocena względem wartości katalogowej", "Seria", "Kondycja [%]", x, first, "Połówka A", 100.0, second, "Połówka B");
		return new FullTestChartFiles(text, text2, text3);
	}

	private static double ConditionPercent(FullTestResult result, double current, double gm)
	{
		double num = ((result.Profile.NominalAnodeCurrentMa > 0.0) ? (current / result.Profile.NominalAnodeCurrentMa * 100.0) : 100.0);
		if (result.TestMode == TubeTestMode.Quick || result.Profile.NominalGmMaV <= 0.0)
		{
			return num;
		}
		double num2 = gm / result.Profile.NominalGmMaV * 100.0;
		return 0.45 * num + 0.55 * num2;
	}

	private static void CreateLineChart(string path, string title, string xLabel, string yLabel, double[] x, double[] first, string firstLabel, double nominal, double[]? second = null, string secondLabel = "")
	{
		Plot plot = new Plot();
		Scatter scatter = plot.Add.Scatter(x, first);
		scatter.LegendText = firstLabel;
		scatter.LineWidth = 2f;
		if (second != null && second.Length > 0)
		{
			Scatter scatter2 = plot.Add.Scatter(x, second);
			scatter2.LegendText = (string.IsNullOrWhiteSpace(secondLabel) ? "Połówka B" : secondLabel);
			scatter2.LineWidth = 2f;
		}
		if (nominal > 0.0)
		{
			double[] ys = Enumerable.Repeat(nominal, x.Length).ToArray();
			Scatter scatter3 = plot.Add.Scatter(x, ys);
			scatter3.LegendText = "Wartość katalogowa";
			scatter3.LinePattern = LinePattern.Dashed;
			scatter3.MarkerSize = 0f;
		}
		plot.Title(title);
		plot.XLabel(xLabel);
		plot.YLabel(yLabel);
		plot.ShowLegend();
		plot.Axes.AutoScale();
		plot.SavePng(path, 1200, 650);
	}
}
