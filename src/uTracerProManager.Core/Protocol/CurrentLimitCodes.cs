using System;

namespace uTracerProManager.Core.Protocol;

public static class CurrentLimitCodes
{
    private static readonly int[] _supportedMilliAmps = [7, 12, 25, 50, 100, 125, 150, 175, 200];

    public static IReadOnlyList<int> SupportedMilliAmps => _supportedMilliAmps;

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
            _ => throw new ArgumentOutOfRangeException(nameof(milliAmps),
                "Obsługiwane limity: 7, 12, 25, 50, 100, 125, 150, 175, 200 mA.")
        };
    }

    /// <summary>
    /// Dobiera najwyższy sprzętowy próg, który NIE przekracza podanego limitu.
    /// To celowo jest zaokrąglenie w dół. Zaokrąglanie do najbliższej / wyższej
    /// wartości może podnieść próg zabezpieczenia ponad limit profilu.
    /// </summary>
    public static int FloorMilliAmps(double maximumMilliAmps)
    {
        if (!double.IsFinite(maximumMilliAmps) || maximumMilliAmps < _supportedMilliAmps[0])
            throw new InvalidOperationException(
                $"Wymagany bezpieczny compliance {maximumMilliAmps:F2} mA jest niższy niż minimalny próg sprzętu {_supportedMilliAmps[0]} mA.");

        var selected = _supportedMilliAmps[0];
        foreach (var value in _supportedMilliAmps)
        {
            if (value > maximumMilliAmps + 1e-9)
                break;
            selected = value;
        }
        return selected;
    }

    public static byte ForSafeMaximum(double maximumMilliAmps) => ForMilliAmps(FloorMilliAmps(maximumMilliAmps));
}
