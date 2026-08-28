namespace uTracerProManager.Core.Models;

public enum ReferenceMeasurementKind
{
    GridSweepSteppedAnode = 1,
    GridSweepSteppedTiedAnodeScreen = 2,
    AnodeSweepSteppedGrid = 3,
    AnodeSweepSteppedScreen = 4,
    TiedAnodeScreenSweepSteppedGrid = 5,
    ScreenSweepSteppedGrid = 6,
    PositiveGridSweepSteppedAnode = 7,
    AnodeSweepSteppedPositiveGrid = 8,
    HeaterSweepSteppedGrid = 9,
    HeaterSweepSteppedAnode = 10,
    GridSweepSteppedAnodeUltraLinear = 11,
    AnodeSweepSteppedGridUltraLinear = 12,
    AnodeSweepSteppedGridSchadeFeedback = 13
}
