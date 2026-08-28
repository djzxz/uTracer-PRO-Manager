using uTracerProManager.Core.Models;
using uTracerProManager.Core.Services;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed class CalibrationWizardViewModel : ObservableObject
{
    private int _step;
    private string _portName;
    private double _vaFactor;
    private double _vsFactor;
    private double _iaFactor;
    private double _isFactor;
    private double _vsuFactor;
    private double _vnFactor;
    private double _vg1Factor;
    private double _vg4Factor;
    private double _vg40Factor;
    private double _originalVsatFactor;
    private double _originalSpareFactor;
    private int _originalHardwareVersion;
    private double _gridOffset;
    private double _gridSlope;
    private double _anodeDivider;
    private double _anodeSense;
    private double _screenSense;
    private double _maxAnodeVoltage;
    private double _maxAnodeCurrent;
    private double _maxScreenCurrent;
    private double _maxGridMagnitude;
    private bool _supplyVerified;
    private bool _negativeVerified;
    private bool _gridVerified;
    private bool _voltageVerified;
    private bool _currentVerified;
    private bool _noTubeConfirmed;
    private string _hardwareStatus = "Wyjścia wyłączone.";
    private double _rawSupplyVoltage = 20;
    private double _dmmSupplyVoltage = 20;
    private double _rawNegativeVoltage = -50;
    private double _dmmNegativeVoltage = -50;
    private double _measuredGridHalf = -0.5;
    private double _measuredGridForty = -40;
    private double _targetBoostVoltage = 100;
    private double _measuredVa = 100;
    private double _measuredVs = 100;
    private double _expectedIa = 10;
    private double _measuredIa = 10;
    private double _expectedIs = 10;
    private double _measuredIs = 10;

    public CalibrationWizardViewModel(CalibrationProfile source)
    {
        _portName = source.PortName;
        _vaFactor = source.VaFactor;
        _vsFactor = source.VsFactor;
        _iaFactor = source.IaFactor;
        _isFactor = source.IsFactor;
        _vsuFactor = source.VsuFactor;
        _vnFactor = source.VnFactor;
        _vg1Factor = source.Vg1Factor;
        _vg4Factor = source.Vg4Factor;
        _vg40Factor = source.Vg40Factor;
        _originalVsatFactor = source.OriginalVsatFactor;
        _originalSpareFactor = source.OriginalSpareFactor;
        _originalHardwareVersion = source.OriginalHardwareVersion;
        _gridOffset = source.GridOffsetV;
        _gridSlope = source.GridSlope;
        _anodeDivider = source.AnodeDividerOhm;
        _anodeSense = source.AnodeSenseOhm;
        _screenSense = source.ScreenSenseOhm;
        _maxAnodeVoltage = source.MaxAnodeVoltage;
        _maxAnodeCurrent = source.MaxAnodeCurrentMa;
        _maxScreenCurrent = source.MaxScreenCurrentMa;
        _maxGridMagnitude = source.MaxGridMagnitudeV;
        _supplyVerified = source.SupplyCalibrationVerified;
        _negativeVerified = source.NegativeSupplyCalibrationVerified;
        _gridVerified = source.GridCalibrationVerified && source.GridOffsetSlopeVerified;
        _voltageVerified = source.VoltageCalibrationVerified;
        _currentVerified = source.CurrentCalibrationVerified;
    }

    public int Step { get => _step; set => SetProperty(ref _step, value); }
    public string PortName { get => _portName; set => SetProperty(ref _portName, value); }
    public double VaFactor { get => _vaFactor; set => SetProperty(ref _vaFactor, value); }
    public double VsFactor { get => _vsFactor; set => SetProperty(ref _vsFactor, value); }
    public double IaFactor { get => _iaFactor; set => SetProperty(ref _iaFactor, value); }
    public double IsFactor { get => _isFactor; set => SetProperty(ref _isFactor, value); }
    public double VsuFactor { get => _vsuFactor; set => SetProperty(ref _vsuFactor, value); }
    public double VnFactor { get => _vnFactor; set => SetProperty(ref _vnFactor, value); }
    public double Vg1Factor { get => _vg1Factor; set => SetProperty(ref _vg1Factor, value); }
    public double Vg4Factor { get => _vg4Factor; set => SetProperty(ref _vg4Factor, value); }
    public double Vg40Factor { get => _vg40Factor; set => SetProperty(ref _vg40Factor, value); }
    public double OriginalVsatFactor { get => _originalVsatFactor; set => SetProperty(ref _originalVsatFactor, value); }
    public double OriginalSpareFactor { get => _originalSpareFactor; set => SetProperty(ref _originalSpareFactor, value); }
    public double GridOffset { get => _gridOffset; set => SetProperty(ref _gridOffset, value); }
    public double GridSlope { get => _gridSlope; set => SetProperty(ref _gridSlope, value); }
    public double AnodeDivider { get => _anodeDivider; set => SetProperty(ref _anodeDivider, value); }
    public double AnodeSense { get => _anodeSense; set => SetProperty(ref _anodeSense, value); }
    public double ScreenSense { get => _screenSense; set => SetProperty(ref _screenSense, value); }
    public double MaxAnodeVoltage { get => _maxAnodeVoltage; set => SetProperty(ref _maxAnodeVoltage, value); }
    public double MaxAnodeCurrent { get => _maxAnodeCurrent; set => SetProperty(ref _maxAnodeCurrent, value); }
    public double MaxScreenCurrent { get => _maxScreenCurrent; set => SetProperty(ref _maxScreenCurrent, value); }
    public double MaxGridMagnitude { get => _maxGridMagnitude; set => SetProperty(ref _maxGridMagnitude, value); }
    public bool SupplyVerified { get => _supplyVerified; set => SetProperty(ref _supplyVerified, value); }
    public bool NegativeVerified { get => _negativeVerified; set => SetProperty(ref _negativeVerified, value); }
    public bool GridVerified { get => _gridVerified; set => SetProperty(ref _gridVerified, value); }
    public bool VoltageVerified { get => _voltageVerified; set => SetProperty(ref _voltageVerified, value); }
    public bool CurrentVerified { get => _currentVerified; set => SetProperty(ref _currentVerified, value); }
    public bool NoTubeConfirmed { get => _noTubeConfirmed; set => SetProperty(ref _noTubeConfirmed, value); }
    public string HardwareStatus { get => _hardwareStatus; set => SetProperty(ref _hardwareStatus, value); }
    public double RawSupplyVoltage { get => _rawSupplyVoltage; set => SetProperty(ref _rawSupplyVoltage, value); }
    public double DmmSupplyVoltage { get => _dmmSupplyVoltage; set => SetProperty(ref _dmmSupplyVoltage, value); }
    public double RawNegativeVoltage { get => _rawNegativeVoltage; set => SetProperty(ref _rawNegativeVoltage, value); }
    public double DmmNegativeVoltage { get => _dmmNegativeVoltage; set => SetProperty(ref _dmmNegativeVoltage, value); }
    public double MeasuredGridHalf { get => _measuredGridHalf; set => SetProperty(ref _measuredGridHalf, value); }
    public double MeasuredGridForty { get => _measuredGridForty; set => SetProperty(ref _measuredGridForty, value); }
    public double TargetBoostVoltage { get => _targetBoostVoltage; set => SetProperty(ref _targetBoostVoltage, value); }
    public double MeasuredVa { get => _measuredVa; set => SetProperty(ref _measuredVa, value); }
    public double MeasuredVs { get => _measuredVs; set => SetProperty(ref _measuredVs, value); }
    public double ExpectedIa { get => _expectedIa; set => SetProperty(ref _expectedIa, value); }
    public double MeasuredIa { get => _measuredIa; set => SetProperty(ref _measuredIa, value); }
    public double ExpectedIs { get => _expectedIs; set => SetProperty(ref _expectedIs, value); }
    public double MeasuredIs { get => _measuredIs; set => SetProperty(ref _measuredIs, value); }

    public void ApplySupplyMath()
    {
        VsuFactor = CalibrationMath.CorrectMultiplicativeFactor(VsuFactor, DmmSupplyVoltage, RawSupplyVoltage);
        VnFactor = CalibrationMath.CorrectMultiplicativeFactor(VnFactor, Math.Abs(DmmNegativeVoltage), Math.Abs(RawNegativeVoltage));
        SupplyVerified = CalibrationMath.IsWithinTolerance(DmmSupplyVoltage, RawSupplyVoltage, 1, 0.15);
        NegativeVerified = CalibrationMath.IsWithinTolerance(Math.Abs(DmmNegativeVoltage), Math.Abs(RawNegativeVoltage), 1, 0.25);
    }

    public void ApplyGridMath()
    {
        var (offset, slope) = CalibrationMath.CalculateGridOffsetSlope(MeasuredGridHalf, MeasuredGridForty);
        GridOffset = offset;
        GridSlope = slope;
        Vg1Factor = LegacyGridFactor(1, offset, slope);
        Vg4Factor = LegacyGridFactor(4, offset, slope);
        Vg40Factor = LegacyGridFactor(40, offset, slope);
        GridVerified = true;
    }

    public void ApplyBoostMath()
    {
        VaFactor = CalibrationMath.CorrectCommandDividerFactor(VaFactor, TargetBoostVoltage, MeasuredVa);
        VsFactor = CalibrationMath.CorrectCommandDividerFactor(VsFactor, TargetBoostVoltage, MeasuredVs);
        VoltageVerified =
            CalibrationMath.IsWithinTolerance(TargetBoostVoltage, MeasuredVa, 1, 0.5) &&
            CalibrationMath.IsWithinTolerance(TargetBoostVoltage, MeasuredVs, 1, 0.5);
    }

    public void ApplyCurrentMath()
    {
        IaFactor = CalibrationMath.CorrectMultiplicativeFactor(IaFactor, ExpectedIa, MeasuredIa);
        IsFactor = CalibrationMath.CorrectMultiplicativeFactor(IsFactor, ExpectedIs, MeasuredIs);
        CurrentVerified =
            CalibrationMath.IsWithinTolerance(ExpectedIa, MeasuredIa, 1, 0.15) &&
            CalibrationMath.IsWithinTolerance(ExpectedIs, MeasuredIs, 1, 0.15);
    }

    public CalibrationProfile Build()
    {
        var complete = SupplyVerified && NegativeVerified && GridVerified && VoltageVerified && CurrentVerified;
        return new CalibrationProfile
        {
            DeviceName = "uTracer 3+ — kreator Avalonia",
            SourcePath = "Kreator pełnej kalibracji Avalonia",
            ImportedAt = DateTimeOffset.Now,
            ImportedFromFile = true,
            PortName = PortName.Trim(),
            CalibrationVersion = "2.0",
            CalibrationCompletedAt = complete ? DateTimeOffset.Now : null,
            VaFactor = VaFactor,
            VsFactor = VsFactor,
            IaFactor = IaFactor,
            IsFactor = IsFactor,
            VsuFactor = VsuFactor,
            VnFactor = VnFactor,
            Vg1Factor = Vg1Factor,
            Vg4Factor = Vg4Factor,
            Vg40Factor = Vg40Factor,
            OriginalVsatFactor = OriginalVsatFactor,
            OriginalSpareFactor = OriginalSpareFactor,
            OriginalHardwareVersion = _originalHardwareVersion,
            GridOffsetV = GridOffset,
            GridSlope = GridSlope,
            GridCalibrationModel = "offset-slope",
            AnodeDividerOhm = AnodeDivider,
            AnodeSenseOhm = AnodeSense,
            ScreenSenseOhm = ScreenSense,
            MaxAnodeVoltage = MaxAnodeVoltage,
            MaxAnodeCurrentMa = MaxAnodeCurrent,
            MaxScreenCurrentMa = MaxScreenCurrent,
            MaxGridMagnitudeV = MaxGridMagnitude,
            SupplyCalibrationVerified = SupplyVerified,
            NegativeSupplyCalibrationVerified = NegativeVerified,
            GridCalibrationVerified = GridVerified,
            GridOffsetSlopeVerified = GridVerified,
            VoltageCalibrationVerified = VoltageVerified,
            CurrentCalibrationVerified = CurrentVerified
        };
    }

    private static double LegacyGridFactor(double magnitude, double offset, double slope)
    {
        if (!double.IsFinite(slope) || slope <= 0)
            return 1;
        return Math.Clamp((magnitude + offset) / (magnitude * slope), 0.4, 1.6);
    }
}
