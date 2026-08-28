using System.Globalization;
using uTracerProManager.Core.Models;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed class ReferenceMeasurementViewModel : ObservableObject
{
    private ReferenceMeasurementDefinition _selectedMeasurement = ReferenceMeasurementDefinition.All[2];
    private double _xStart = 20;
    private double _xStop = 250;
    private int _intervals = 12;
    private string _steppingValues = "-1; -2; -3; -4";
    private double _constantVa = 250;
    private double _constantVs = 250;
    private double _constantVg = -2;
    private double _constantVh = 6.3;
    private double _ulKPercent = 43;
    private double _schadePercent = 1;
    private int _averagingIndex = 2;
    private int _complianceMa = 25;
    private int _delaySeconds;
    private int _warmupSeconds = 60;
    private bool _logarithmicX;
    private bool _specialWiringConfirmed;
    private bool _externalHeater;
    private string _y1Variable = "Ia [mA]";
    private string _y2Variable = "Is [mA]";
    private string _lineStyle = "Linie i punkty";
    private string _scaleMode = "Automatyczna";
    private double _xMinimum;
    private double _xMaximum = 400;
    private double _yMinimum;
    private double _yMaximum = 100;
    private int _xTicks = 10;
    private int _yTicks = 10;
    private bool _showGrid = true;
    private bool _useColor = true;
    private bool _keepPlot;
    private string _plotTitle = "Charakterystyki lampy";
    private string _status = "Wybierz profil i ustaw rodzaj skanu.";
    private double _progress;

    public IReadOnlyList<ReferenceMeasurementDefinition> MeasurementTypes => ReferenceMeasurementDefinition.All;
    public IReadOnlyList<int> AveragingOptions { get; } = Enumerable.Range(0, 8).ToArray();
    public IReadOnlyList<int> ComplianceOptions { get; } = [7, 12, 25, 50, 100, 125, 150, 175, 200];
    public IReadOnlyList<string> YVariables { get; } = ["Ia [mA]", "Is [mA]", "Brak"];
    public IReadOnlyList<string> LineStyles { get; } = ["Linie i punkty", "Linie", "Punkty"];
    public IReadOnlyList<string> ScaleModes { get; } = ["Automatyczna", "Ręczna"];

    public ReferenceMeasurementDefinition SelectedMeasurement
    {
        get => _selectedMeasurement;
        set
        {
            if (!SetProperty(ref _selectedMeasurement, value))
                return;
            SpecialWiringConfirmed = false;
            OnPropertyChanged(nameof(MeasurementDescription));
            OnPropertyChanged(nameof(MeasurementUsage));
            OnPropertyChanged(nameof(XAxisLabel));
            OnPropertyChanged(nameof(SteppingLabel));
            OnPropertyChanged(nameof(SpecialWiringRequired));
            OnPropertyChanged(nameof(SpecialWiringText));
            OnPropertyChanged(nameof(UltraLinearVisible));
            OnPropertyChanged(nameof(SchadeVisible));
        }
    }

    public string MeasurementDescription => SelectedMeasurement.Description;
    public string MeasurementUsage => SelectedMeasurement.Usage;
    public string XAxisLabel => SelectedMeasurement.XAxisLabel;
    public string SteppingLabel => SelectedMeasurement.SteppingLabel;
    public bool SpecialWiringRequired => SelectedMeasurement.RequiresSpecialWiring;
    public bool UltraLinearVisible => SelectedMeasurement.UltraLinearMode;
    public bool SchadeVisible => SelectedMeasurement.SchadeFeedbackMode;
    public string SpecialWiringText => SelectedMeasurement.PositiveGridMode
        ? "Podłącz siatkę sterującą lampy do wyjścia SCREEN (Vs), pozostaw zacisk GRID uTracera wolny i potwierdź połączenie."
        : "Sprawdź połączenie Va=Vs / drugiej anody zgodnie z pinoutem wybranego profilu i potwierdź je przed pomiarem.";

    public double XStart { get => _xStart; set => SetProperty(ref _xStart, value); }
    public double XStop { get => _xStop; set => SetProperty(ref _xStop, value); }
    public int Intervals { get => _intervals; set => SetProperty(ref _intervals, value); }
    public string SteppingValues { get => _steppingValues; set => SetProperty(ref _steppingValues, value); }
    public double ConstantVa { get => _constantVa; set => SetProperty(ref _constantVa, value); }
    public double ConstantVs { get => _constantVs; set => SetProperty(ref _constantVs, value); }
    public double ConstantVg { get => _constantVg; set => SetProperty(ref _constantVg, value); }
    public double ConstantVh { get => _constantVh; set => SetProperty(ref _constantVh, value); }
    public double UltraLinearKPercent { get => _ulKPercent; set => SetProperty(ref _ulKPercent, value); }
    public double SchadeFeedbackPercent { get => _schadePercent; set => SetProperty(ref _schadePercent, value); }
    public int AveragingIndex { get => _averagingIndex; set => SetProperty(ref _averagingIndex, value); }
    public int ComplianceMa { get => _complianceMa; set => SetProperty(ref _complianceMa, value); }
    public int DelaySeconds { get => _delaySeconds; set => SetProperty(ref _delaySeconds, value); }
    public int WarmupSeconds { get => _warmupSeconds; set => SetProperty(ref _warmupSeconds, value); }
    public bool LogarithmicX { get => _logarithmicX; set => SetProperty(ref _logarithmicX, value); }
    public bool SpecialWiringConfirmed { get => _specialWiringConfirmed; set => SetProperty(ref _specialWiringConfirmed, value); }
    public bool ExternalHeater { get => _externalHeater; set => SetProperty(ref _externalHeater, value); }
    public string Y1Variable { get => _y1Variable; set => SetProperty(ref _y1Variable, value); }
    public string Y2Variable { get => _y2Variable; set => SetProperty(ref _y2Variable, value); }
    public string LineStyle { get => _lineStyle; set => SetProperty(ref _lineStyle, value); }
    public string ScaleMode { get => _scaleMode; set { if (SetProperty(ref _scaleMode, value)) OnPropertyChanged(nameof(ManualScale)); } }
    public bool ManualScale => string.Equals(ScaleMode, "Ręczna", StringComparison.Ordinal);
    public double XMinimum { get => _xMinimum; set => SetProperty(ref _xMinimum, value); }
    public double XMaximum { get => _xMaximum; set => SetProperty(ref _xMaximum, value); }
    public double YMinimum { get => _yMinimum; set => SetProperty(ref _yMinimum, value); }
    public double YMaximum { get => _yMaximum; set => SetProperty(ref _yMaximum, value); }
    public int XTicks { get => _xTicks; set => SetProperty(ref _xTicks, value); }
    public int YTicks { get => _yTicks; set => SetProperty(ref _yTicks, value); }
    public bool ShowGrid { get => _showGrid; set => SetProperty(ref _showGrid, value); }
    public bool UseColor { get => _useColor; set => SetProperty(ref _useColor, value); }
    public bool KeepPlot { get => _keepPlot; set => SetProperty(ref _keepPlot, value); }
    public string PlotTitle { get => _plotTitle; set => SetProperty(ref _plotTitle, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

    public void ApplyProfile(TubeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ConstantVa = profile.AnodeVoltage;
        ConstantVs = profile.ScreenVoltage;
        ConstantVg = profile.GridVoltage;
        ConstantVh = profile.HeaterVoltage;
        WarmupSeconds = Math.Clamp(profile.WarmupSeconds, 60, 1800);
        ComplianceMa = ResolveCompliance(profile);
        XStart = profile.CurveVaStartV > 0 ? profile.CurveVaStartV : Math.Max(2, profile.AnodeVoltage * 0.1);
        XStop = profile.CurveVaStopV > XStart ? profile.CurveVaStopV : Math.Max(XStart + 10, profile.AnodeVoltage);
        Intervals = profile.CurveVaStepV > 0
            ? Math.Clamp((int)Math.Round((XStop - XStart) / profile.CurveVaStepV), 1, 50)
            : 12;
        var grids = ParseValues(profile.CurveGridVoltages);
        SteppingValues = grids.Count > 0
            ? string.Join("; ", grids.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))
            : profile.GridVoltage.ToString("0.###", CultureInfo.InvariantCulture);
        PlotTitle = profile.DisplayName;
        XMinimum = XStart;
        XMaximum = XStop;
        YMinimum = 0;
        YMaximum = Math.Max(10, profile.AnodeComplianceMa);
        Status = "Wczytano zatwierdzone limity profilu. Sprawdź rodzaj skanu i okablowanie.";
    }

    public void ApplyOriginalSetup(OriginalUTracerSetupDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var index = Math.Clamp(document.MeasurementType, 0, ReferenceMeasurementDefinition.All.Count - 1);
        SelectedMeasurement = ReferenceMeasurementDefinition.All[index];
        XStart = document.VariableStart;
        XStop = document.VariableStop;
        Intervals = Math.Clamp(document.VariableIntervals, 1, 200);
        ConstantVs = document.Constant1;
        ConstantVh = document.Constant2;
        SteppingValues = document.FirstSteppingValue.ToString("0.###", CultureInfo.InvariantCulture);
        Status = $"Zaimportowano panel pomiarowy z {document.Variant}.";
    }

    public ReferenceMeasurementRequest BuildRequest()
    {
        var values = ParseValues(SteppingValues);
        return new ReferenceMeasurementRequest(
            SelectedMeasurement.Kind,
            XStart,
            XStop,
            Intervals,
            values,
            ConstantVa,
            ConstantVs,
            ConstantVg,
            ConstantVh,
            UltraLinearKPercent,
            SchadeFeedbackPercent,
            AveragingIndex,
            ComplianceMa,
            DelaySeconds,
            WarmupSeconds,
            LogarithmicX,
            SpecialWiringConfirmed,
            ExternalHeater);
    }

    private static IReadOnlyList<double> ParseValues(string text)
    {
        var result = new List<double>();
        foreach (var token in (text ?? string.Empty).Split(new[] { ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant) ||
                double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out invariant))
                result.Add(invariant);
        }
        return result.Distinct().Take(40).ToArray();
    }

    private static int ResolveCompliance(TubeProfile profile)
    {
        var requested = Math.Max(profile.AnodeComplianceMa, profile.ScreenComplianceMa);
        return new[] { 7, 12, 25, 50, 100, 125, 150, 175, 200 }
            .First(value => value >= Math.Min(200, requested));
    }
}
