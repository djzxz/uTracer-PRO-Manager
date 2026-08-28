using System.IO.Ports;

namespace uTracerProManager.Services;

public sealed class PortCatalogService
{
    public IReadOnlyList<string> GetPortNames()
    {
        if (OperatingSystem.IsWindows())
            return Win32SerialConnection.GetPortNames();

        return SerialPort.GetPortNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
