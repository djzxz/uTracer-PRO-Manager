using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.Services;

public sealed class FullTestExportService
{
	private readonly FullTestChartService _charts;

	public FullTestExportService(FullTestChartService charts)
	{
		_charts = charts;
		Settings.License = LicenseType.Community;
	}

	public async Task<ExportBundleResult> ExportAllAsync(FullTestResult result, string parentDirectory, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(result, "result");
		string value = SafeFileName(result.TubeInventoryNumber);
		string value2 = SafeFileName(result.TestMode.DisplayName());
		string path = $"{result.CompletedAt:yyyy-MM-dd_HH-mm-ss}_{value}_{value2}_{result.TestId:N}";
		string directory = Path.Combine(parentDirectory, path);
		Directory.CreateDirectory(directory);
		FullTestChartFiles charts = _charts.CreateCharts(result, directory);
		string pdfPath = Path.Combine(directory, "Raport_testu_lampy.pdf");
		string excelPath = Path.Combine(directory, "Test_lampy.xlsx");
		string csvPath = Path.Combine(directory, "Pomiary_AB.csv");
		string originalQuickTestTextPath = Path.Combine(directory, "uTracer_Quick_Test.txt");
		await Task.Run(delegate
		{
			CreatePdf(result, charts, pdfPath);
		}, cancellationToken);
		await Task.Run(delegate
		{
			CreateExcel(result, charts, excelPath);
		}, cancellationToken);
		await CreateCsvAsync(result, csvPath, cancellationToken);
		await new OriginalUTracerQuickTestReportService().ExportAsync(result, originalQuickTestTextPath, cancellationToken);
		IReadOnlyList<DiagnosticCurvePoint> diagnosticCurvePoints = result.DiagnosticCurvePoints;
		if (diagnosticCurvePoints != null && diagnosticCurvePoints.Count > 0)
		{
			string path2 = Path.Combine(directory, "Charakterystyki_AB.csv");
			await CreateCurvesCsvAsync(result.DiagnosticCurvePoints, path2, cancellationToken);
		}
		return new ExportBundleResult(directory, pdfPath, excelPath, csvPath, originalQuickTestTextPath, charts);
	}

	private static void CreatePdf(FullTestResult result, FullTestChartFiles charts, string path)
	{
		Document.Create(delegate(IDocumentContainer document)
		{
			document.Page(delegate(PageDescriptor page)
			{
				page.Size(PageSizes.A4);
				page.Margin(25f);
				page.DefaultTextStyle((TextStyle style) => style.FontSize(9f));
				page.Header().Text("uTracer PRO Manager — " + result.TestMode.DisplayName()).SemiBold()
					.FontSize(17f);
				page.Content().Column(delegate(ColumnDescriptor column)
				{
					column.Spacing(8f);
					column.Item().Text(result.Profile.DisplayName).SemiBold()
						.FontSize(14f);
					column.Item().Row(delegate(RowDescriptor row)
					{
						row.RelativeItem().Text($"Nr ewidencyjny: {result.TubeInventoryNumber}\nProducent: {result.Manufacturer}\nKod 1: {result.ProductionCodePart1}\nKod 2: {result.ProductionCodePart2}\nStan deklarowany: {result.DeclaredCondition}");
						row.RelativeItem().Text($"Data: {result.CompletedAt:yyyy-MM-dd HH:mm:ss}\nTryb: {result.TestMode.DisplayName()}\nNagrzewanie: {result.Options.InitialWarmupSeconds} s\nEmulator: {(result.Emulator ? "tak" : "nie")}");
					});
					column.Item().Text("POŁÓWKA A").SemiBold()
						.FontSize(12f);
					AddStatisticsTable(column, result.Profile, result.Statistics, result.Options);
					if ((object)result.SectionBStatistics != null)
					{
						column.Item().Text("POŁÓWKA B").SemiBold()
							.FontSize(12f);
						AddStatisticsTable(column, result.Profile, result.SectionBStatistics, result.Options);
					}
					if ((object)result.DualComparison != null)
					{
						DualSectionComparison dualComparison = result.DualComparison;
						column.Item().Background(Colors.Grey.Lighten3).Padding(8f)
							.Text($"Dopasowanie A/B: {dualComparison.OverallMatchPercent:F1}% — {dualComparison.Grade}\nRóżnica Ia: {dualComparison.IaDifferencePercent:F2}%" + ((result.TestMode == TubeTestMode.Quick) ? string.Empty : $"; gm: {dualComparison.GmDifferencePercent:F2}%; Rp: {dualComparison.RpDifferencePercent:F2}%; μ: {dualComparison.MuDifferencePercent:F2}%") + "\n" + dualComparison.Recommendation);
					}
					column.Item().PageBreak();
					column.Item().Text("Wykres prądów anodowych A/B").SemiBold();
					column.Item().Image(charts.CurrentChartPath);
					if (result.TestMode != TubeTestMode.Quick)
					{
						column.Item().Text("Wykres gm A/B").SemiBold();
						column.Item().Image(charts.GmChartPath);
					}
					column.Item().Text("Wykres kondycji i dopasowania").SemiBold();
					column.Item().Image(charts.ConditionChartPath);
					column.Item().PageBreak();
					column.Item().Text("Szczegółowe serie").SemiBold();
					column.Item().Table(delegate(TableDescriptor table)
					{
						table.ColumnsDefinition(delegate(TableColumnsDefinitionDescriptor columns)
						{
							columns.ConstantColumn(28f);
							columns.RelativeColumn();
							columns.RelativeColumn();
							columns.RelativeColumn();
							columns.RelativeColumn();
							columns.RelativeColumn(2f);
						});
						table.Header(delegate(TableCellDescriptor header)
						{
							string[] array = new string[6] { "Nr", "Ia A", "Ia B", "gm A", "gm B", "Etap / uwagi" };
							foreach (string text in array)
							{
								header.Cell().Background(Colors.Blue.Lighten4).BorderBottom(1f)
									.Padding(3f)
									.Text(text)
									.SemiBold();
							}
						});
						foreach (FullTestSample item in result.Samples.OrderBy((FullTestSample sample) => sample.Sequence))
						{
							DataRow(table, item.Sequence.ToString(CultureInfo.InvariantCulture), $"{item.AnodeCurrentMa:F3}", $"{item.ScreenCurrentMa:F3}", (result.TestMode == TubeTestMode.Quick) ? "—" : $"{item.GmMaV:F3}", (result.TestMode == TubeTestMode.Quick) ? "—" : $"{item.SectionBGmMaV:F3}", item.MeasurementLabel + "; " + (item.IsOutlier ? "ODSTAJĄCY; " : string.Empty) + item.ActionAfterSample);
						}
					});
					IReadOnlyList<DiagnosticCurvePoint> diagnosticCurvePoints = result.DiagnosticCurvePoints;
					if (diagnosticCurvePoints != null && diagnosticCurvePoints.Count > 0)
					{
						column.Item().PageBreak();
						column.Item().Text($"Charakterystyki — {result.DiagnosticCurvePoints.Count} punktów").SemiBold();
						column.Item().Table(delegate(TableDescriptor table)
						{
							table.ColumnsDefinition(delegate(TableColumnsDefinitionDescriptor columns)
							{
								columns.ConstantColumn(30f);
								columns.RelativeColumn();
								columns.RelativeColumn();
								columns.RelativeColumn();
								columns.RelativeColumn();
								columns.RelativeColumn();
							});
							table.Header(delegate(TableCellDescriptor header)
							{
								string[] array = new string[6] { "Nr", "Vg", "Va A", "Ia A", "Va B", "Ia B" };
								foreach (string text in array)
								{
									header.Cell().Background(Colors.Blue.Lighten4).BorderBottom(1f)
										.Padding(3f)
										.Text(text)
										.SemiBold();
								}
							});
							foreach (DiagnosticCurvePoint diagnosticCurvePoint in result.DiagnosticCurvePoints)
							{
								DataRow(table, diagnosticCurvePoint.Sequence.ToString(CultureInfo.InvariantCulture), $"{diagnosticCurvePoint.GridVoltage:F2}", $"{diagnosticCurvePoint.MeasuredAnodeVoltageA:F1}", $"{diagnosticCurvePoint.AnodeCurrentAMa:F3}", $"{diagnosticCurvePoint.MeasuredAnodeVoltageB:F1}", $"{diagnosticCurvePoint.AnodeCurrentBMa:F3}");
							}
						});
					}
					if (!string.IsNullOrWhiteSpace(result.Notes))
					{
						column.Item().Text("Notatki").SemiBold();
						column.Item().Text(result.Notes);
					}
				});
				page.Footer().AlignCenter().Text(delegate(TextDescriptor text)
				{
					text.Span("uTracer PRO Manager v");
					text.Span(result.ApplicationVersion);
					text.Span(" • strona ");
					text.CurrentPageNumber();
					text.Span("/");
					text.TotalPages();
				});
			});
		}).GeneratePdf(path);
	}

	private static void AddStatisticsTable(ColumnDescriptor column, TubeProfile profile, FullTestStatistics statistics, FullTestOptions options)
	{
		column.Item().Background(Colors.Grey.Lighten3).Padding(6f)
			.Text($"Ocena: {statistics.Grade}; kondycja: {statistics.OverallConditionPercent:F1}%; wiarygodność: {statistics.Reliability}. {statistics.Recommendation}");
		column.Item().Table(delegate(TableDescriptor table)
		{
			table.ColumnsDefinition(delegate(TableColumnsDefinitionDescriptor columns)
			{
				columns.RelativeColumn(2f);
				columns.RelativeColumn();
				columns.RelativeColumn();
				columns.RelativeColumn();
			});
			table.Header(delegate(TableCellDescriptor header)
			{
				string[] array = new string[4] { "Parametr", "Średnia", "Katalog", "% / CV" };
				foreach (string text in array)
				{
					header.Cell().Background(Colors.Blue.Lighten4).BorderBottom(1f)
						.Padding(4f)
						.Text(text)
						.SemiBold();
				}
			});
			DataRow(table, "Ia", $"{statistics.MeanIaMa:F3} mA", $"{profile.NominalAnodeCurrentMa:F3} mA", $"{statistics.IaPercentOfNominal:F1}% / {statistics.CvIaPercent:F2}%");
			if (options.MeasureDynamicParameters)
			{
				DataRow(table, "gm", $"{statistics.MeanGmMaV:F3} mA/V", $"{profile.NominalGmMaV:F3} mA/V", $"{statistics.GmPercentOfNominal:F1}% / {statistics.CvGmPercent:F2}%");
				DataRow(table, "Rp", $"{statistics.MeanRpKohm:F3} kΩ", $"{profile.NominalRpKohm:F3} kΩ", "—");
				DataRow(table, "μ", $"{statistics.MeanMu:F2}", $"{profile.NominalMu:F2}", "—");
			}
			else
			{
				DataRow(table, "gm / Rp / μ", "pominięte", "—", "szybki test");
			}
			DataRow(table, "Serie", $"{statistics.ValidSeries} ważnych", $"min. {options.MinimumValidSeries}", $"{statistics.Outliers} odrzuconych");
		});
	}

	private static void CreateExcel(FullTestResult result, FullTestChartFiles charts, string path)
	{
		using XLWorkbook xLWorkbook = new XLWorkbook();
		IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add("Podsumowanie");
		iXLWorksheet.Cell("A1").Value = "uTracer PRO Manager — " + result.TestMode.DisplayName();
		iXLWorksheet.Cell("A1").Style.Font.Bold = true;
		iXLWorksheet.Cell("A1").Style.Font.FontSize = 16.0;
		List<object[]> list = new List<object[]>();
		list.Add(new object[2]
		{
			"ID testu",
			result.TestId.ToString("D")
		});
		list.Add(new object[2]
		{
			"Tryb testu",
			result.TestMode.DisplayName()
		});
		list.Add(new object[2]
		{
			"Nagrzewanie [s]",
			result.Options.InitialWarmupSeconds
		});
		list.Add(new object[2] { "Nr ewidencyjny", result.TubeInventoryNumber });
		list.Add(new object[2] { "Producent", result.Manufacturer });
		list.Add(new object[2] { "Kod produkcyjny 1", result.ProductionCodePart1 });
		list.Add(new object[2] { "Kod produkcyjny 2", result.ProductionCodePart2 });
		list.Add(new object[2] { "Stan deklarowany", result.DeclaredCondition });
		list.Add(new object[2]
		{
			"Profil",
			result.Profile.DisplayName
		});
		list.Add(new object[2]
		{
			"Data",
			result.CompletedAt.DateTime
		});
		list.Add(new object[2]
		{
			"Ocena A",
			result.Statistics.Grade
		});
		list.Add(new object[2]
		{
			"Kondycja A [%]",
			result.Statistics.OverallConditionPercent
		});
		list.Add(new object[2]
		{
			"Ia A [mA]",
			result.Statistics.MeanIaMa
		});
		list.Add(new object[2]
		{
			"gm A [mA/V]",
			result.Statistics.MeanGmMaV
		});
		list.Add(new object[2]
		{
			"Rp A [kΩ]",
			result.Statistics.MeanRpKohm
		});
		list.Add(new object[2]
		{
			"μ A",
			result.Statistics.MeanMu
		});
		list.Add(new object[2]
		{
			"Ważne serie",
			result.Statistics.ValidSeries
		});
		list.Add(new object[2]
		{
			"Odrzucone",
			result.Statistics.Outliers
		});
		list.Add(new object[2]
		{
			"Rekomendacja A",
			result.Statistics.Recommendation
		});
		List<object[]> list2 = list;
		if ((object)result.SectionBStatistics != null)
		{
			FullTestStatistics sectionBStatistics = result.SectionBStatistics;
			list2.AddRange(new object[7][]
			{
				new object[2] { "Ocena B", sectionBStatistics.Grade },
				new object[2] { "Kondycja B [%]", sectionBStatistics.OverallConditionPercent },
				new object[2] { "Ia B [mA]", sectionBStatistics.MeanIaMa },
				new object[2] { "gm B [mA/V]", sectionBStatistics.MeanGmMaV },
				new object[2] { "Rp B [kΩ]", sectionBStatistics.MeanRpKohm },
				new object[2] { "μ B", sectionBStatistics.MeanMu },
				new object[2] { "Rekomendacja B", sectionBStatistics.Recommendation }
			});
		}
		if ((object)result.DualComparison != null)
		{
			DualSectionComparison dualComparison = result.DualComparison;
			list2.AddRange(new object[7][]
			{
				new object[2] { "Zgodność A/B [%]", dualComparison.OverallMatchPercent },
				new object[2] { "Klasa dopasowania", dualComparison.Grade },
				new object[2] { "Różnica Ia [%]", dualComparison.IaDifferencePercent },
				new object[2] { "Różnica gm [%]", dualComparison.GmDifferencePercent },
				new object[2] { "Różnica Rp [%]", dualComparison.RpDifferencePercent },
				new object[2] { "Różnica μ [%]", dualComparison.MuDifferencePercent },
				new object[2] { "Rekomendacja A/B", dualComparison.Recommendation }
			});
		}
		list2.Add(new object[2] { "Notatki", result.Notes });
		iXLWorksheet.Cell("A3").InsertData(list2);
		iXLWorksheet.Columns("A:B").AdjustToContents();
		iXLWorksheet.Column("B").Width = Math.Min(iXLWorksheet.Column("B").Width, 80.0);
		IXLRange iXLRange = iXLWorksheet.RangeUsed();
		if (iXLRange != null)
		{
			iXLRange.Style.Alignment.WrapText = true;
		}
		iXLWorksheet.AddPicture(charts.ConditionChartPath).MoveTo(iXLWorksheet.Cell("D3")).WithSize(720, 390);
		IXLWorksheet iXLWorksheet2 = xLWorkbook.Worksheets.Add("Serie A-B");
		string[] array = new string[23]
		{
			"Seria", "Data/czas", "Etap", "Kondycjonująca", "Vg [V]", "Va A zadane [V]", "Va A zmierzone [V]", "Ia A [mA]", "gm A [mA/V]", "Rp A [kΩ]",
			"μ A", "Pa A [W]", "Va B zadane [V]", "Va B zmierzone [V]", "Ia B [mA]", "gm B [mA/V]", "Rp B [kΩ]", "μ B", "Pa B [W]", "Uśrednianie",
			"Odstająca", "Działanie po pomiarze", "Status"
		};
		for (int i = 0; i < array.Length; i++)
		{
			iXLWorksheet2.Cell(1, i + 1).Value = array[i];
			iXLWorksheet2.Cell(1, i + 1).Style.Font.Bold = true;
		}
		int num = 2;
		foreach (FullTestSample item in result.Samples.OrderBy((FullTestSample x) => x.Sequence))
		{
			object[] array2 = new object[23]
			{
				item.Sequence,
				item.Timestamp.DateTime,
				item.MeasurementLabel,
				item.Conditioning,
				item.GridVoltage,
				item.CommandedAnodeVoltage,
				item.MeasuredAnodeVoltage,
				item.AnodeCurrentMa,
				item.GmMaV,
				item.RpKohm,
				item.Mu,
				item.AnodePowerW,
				item.CommandedScreenVoltage,
				item.MeasuredScreenVoltage,
				item.ScreenCurrentMa,
				item.SectionBGmMaV,
				item.SectionBRpKohm,
				item.SectionBMu,
				item.SectionBPowerW,
				item.AveragingIndex,
				item.IsOutlier,
				item.ActionAfterSample,
				item.RawStatus
			};
			for (int num2 = 0; num2 < array2.Length; num2++)
			{
				SetCellValue(iXLWorksheet2.Cell(num, num2 + 1), array2[num2]);
			}
			num++;
		}
		iXLWorksheet2.RangeUsed()?.CreateTable();
		iXLWorksheet2.SheetView.FreezeRows(1);
		iXLWorksheet2.Columns().AdjustToContents();
		iXLWorksheet2.Column(22).Width = 70.0;
		iXLWorksheet2.Column(22).Style.Alignment.WrapText = true;
		IReadOnlyList<DiagnosticCurvePoint> diagnosticCurvePoints = result.DiagnosticCurvePoints;
		if (diagnosticCurvePoints != null && diagnosticCurvePoints.Count > 0)
		{
			IXLWorksheet iXLWorksheet3 = xLWorkbook.Worksheets.Add("Charakterystyki");
			string[] array3 = new string[10] { "Punkt", "Data/czas", "Vg [V]", "Va A zadane [V]", "Va A zmierzone [V]", "Ia A [mA]", "Va B zadane [V]", "Va B zmierzone [V]", "Ia B [mA]", "Status" };
			for (int num3 = 0; num3 < array3.Length; num3++)
			{
				iXLWorksheet3.Cell(1, num3 + 1).Value = array3[num3];
				iXLWorksheet3.Cell(1, num3 + 1).Style.Font.Bold = true;
			}
			int num4 = 2;
			foreach (DiagnosticCurvePoint diagnosticCurvePoint in result.DiagnosticCurvePoints)
			{
				object[] array4 = new object[10]
				{
					diagnosticCurvePoint.Sequence,
					diagnosticCurvePoint.Timestamp.DateTime,
					diagnosticCurvePoint.GridVoltage,
					diagnosticCurvePoint.TargetAnodeVoltageA,
					diagnosticCurvePoint.MeasuredAnodeVoltageA,
					diagnosticCurvePoint.AnodeCurrentAMa,
					diagnosticCurvePoint.TargetAnodeVoltageB,
					diagnosticCurvePoint.MeasuredAnodeVoltageB,
					diagnosticCurvePoint.AnodeCurrentBMa,
					diagnosticCurvePoint.Status
				};
				for (int num5 = 0; num5 < array4.Length; num5++)
				{
					SetCellValue(iXLWorksheet3.Cell(num4, num5 + 1), array4[num5]);
				}
				num4++;
			}
			iXLWorksheet3.RangeUsed()?.CreateTable();
			iXLWorksheet3.SheetView.FreezeRows(1);
			iXLWorksheet3.Columns().AdjustToContents();
		}
		IXLWorksheet iXLWorksheet4 = xLWorkbook.Worksheets.Add("Wykresy");
		iXLWorksheet4.AddPicture(charts.CurrentChartPath).MoveTo(iXLWorksheet4.Cell("A1")).WithSize(900, 480);
		iXLWorksheet4.AddPicture(charts.GmChartPath).MoveTo(iXLWorksheet4.Cell("A26")).WithSize(900, 480);
		iXLWorksheet4.AddPicture(charts.ConditionChartPath).MoveTo(iXLWorksheet4.Cell("A51")).WithSize(900, 480);
		xLWorkbook.SaveAs(path);
	}

	private static async Task CreateCsvAsync(FullTestResult result, string path, CancellationToken cancellationToken)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Seria;Data;Etap;Kondycjonująca;Vg_V;VaA_zadane_V;VaA_zmierzone_V;IaA_mA;gmA_mA_V;RpA_kOhm;MuA;PaA_W;VaB_zadane_V;VaB_zmierzone_V;IaB_mA;gmB_mA_V;RpB_kOhm;MuB;PaB_W;Uśrednianie;Odstająca;Działanie;Status");
		foreach (FullTestSample item in result.Samples.OrderBy((FullTestSample x) => x.Sequence))
		{
			stringBuilder.AppendLine(string.Join(";", item.Sequence, item.Timestamp.ToString("O", CultureInfo.InvariantCulture), Quote(item.MeasurementLabel), item.Conditioning, F(item.GridVoltage), F(item.CommandedAnodeVoltage), F(item.MeasuredAnodeVoltage), F(item.AnodeCurrentMa), F(item.GmMaV), F(item.RpKohm), F(item.Mu), F(item.AnodePowerW), F(item.CommandedScreenVoltage), F(item.MeasuredScreenVoltage), F(item.ScreenCurrentMa), F(item.SectionBGmMaV), F(item.SectionBRpKohm), F(item.SectionBMu), F(item.SectionBPowerW), item.AveragingIndex, item.IsOutlier, Quote(item.ActionAfterSample), Quote(item.RawStatus)));
		}
		await File.WriteAllTextAsync(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
	}

	private static async Task CreateCurvesCsvAsync(IReadOnlyList<DiagnosticCurvePoint> points, string path, CancellationToken cancellationToken)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Punkt;Data;Vg_V;VaA_zadane_V;VaA_zmierzone_V;IaA_mA;VaB_zadane_V;VaB_zmierzone_V;IaB_mA;Status");
		foreach (DiagnosticCurvePoint item in points.OrderBy((DiagnosticCurvePoint x) => x.Sequence))
		{
			stringBuilder.AppendLine(string.Join(";", item.Sequence, item.Timestamp.ToString("O", CultureInfo.InvariantCulture), F(item.GridVoltage), F(item.TargetAnodeVoltageA), F(item.MeasuredAnodeVoltageA), F(item.AnodeCurrentAMa), F(item.TargetAnodeVoltageB), F(item.MeasuredAnodeVoltageB), F(item.AnodeCurrentBMa), Quote(item.Status)));
		}
		await File.WriteAllTextAsync(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
	}

	private static void SetCellValue(IXLCell cell, object? value)
	{
		if (value != null)
		{
			if (!(value is string text))
			{
				if (!(value is bool flag))
				{
					if (!(value is int num))
					{
						if (!(value is long num2))
						{
							if (!(value is double num3))
							{
								if (!(value is decimal num4))
								{
									if (!(value is DateTime dateTime))
									{
										if (value is DateTimeOffset dateTimeOffset)
										{
											cell.Value = dateTimeOffset.DateTime;
										}
										else
										{
											cell.Value = value.ToString() ?? string.Empty;
										}
									}
									else
									{
										cell.Value = dateTime;
									}
								}
								else
								{
									cell.Value = num4;
								}
							}
							else
							{
								cell.Value = num3;
							}
						}
						else
						{
							cell.Value = num2;
						}
					}
					else
					{
						cell.Value = num;
					}
				}
				else
				{
					cell.Value = flag;
				}
			}
			else
			{
				cell.Value = text;
			}
		}
		else
		{
			cell.Clear();
		}
	}

	private static void DataRow(TableDescriptor table, params string[] values)
	{
		foreach (string text in values)
		{
			table.Cell().BorderBottom(1f).BorderColor(Colors.Grey.Lighten2)
				.Padding(3f)
				.Text(text);
		}
	}

	private static string SafeFileName(string value)
	{
		string text = value;
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return "Lampa";
	}

	private static string F(double value)
	{
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}

	private static string Quote(string value)
	{
		return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
	}
}
