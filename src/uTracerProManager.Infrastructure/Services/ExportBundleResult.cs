namespace uTracerProManager.Services;

public sealed record ExportBundleResult(
    string Directory,
    string PdfPath,
    string ExcelPath,
    string CsvPath,
    string OriginalQuickTestTextPath,
    FullTestChartFiles Charts);
