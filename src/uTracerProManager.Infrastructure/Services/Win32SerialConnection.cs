using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace uTracerProManager.Services;

/// <summary>
/// Natywne, synchroniczne połączenie z portem COM. Starsze GUI u-Tracera używa
/// klasycznego uchwytu Win32; ten kod celowo nie korzysta z FILE_FLAG_OVERLAPPED.
/// Każda konfiguracja zaczyna się od GetCommState i zachowuje pola wymagane przez
/// konkretny sterownik USB-UART.
/// </summary>
internal sealed class Win32SerialConnection : IDisposable
{
    private const uint GenericRead = 0x8000_0000;
    private const uint GenericWrite = 0x4000_0000;
    private const uint OpenExisting = 3;

    private const uint FBinary = 1u << 0;
    private const uint FParity = 1u << 1;
    private const uint FOutxCtsFlow = 1u << 2;
    private const uint FOutxDsrFlow = 1u << 3;
    private const uint FDtrControlMask = 3u << 4;
    private const uint FDsrSensitivity = 1u << 6;
    private const uint FOutX = 1u << 8;
    private const uint FInX = 1u << 9;
    private const uint FErrorChar = 1u << 10;
    private const uint FNull = 1u << 11;
    private const uint FRtsControlMask = 3u << 12;
    private const uint FAbortOnError = 1u << 14;

    private const uint DtrControlEnable = 1u << 4;
    private const uint RtsControlEnable = 1u << 12;

    private const uint PurgeTxAbort = 0x0001;
    private const uint PurgeRxAbort = 0x0002;
    private const uint PurgeTxClear = 0x0004;
    private const uint PurgeRxClear = 0x0008;

    private const int ReadTimeoutMilliseconds = 5_000;
    private const int WriteTimeoutMilliseconds = 5_000;
    private static readonly int DcbSize = Marshal.SizeOf<Dcb>();

    private SafeFileHandle? _handle;
    private string _portName = string.Empty;

    internal static IReadOnlyList<SerialOpenProfile> OpenProfiles { get; } =
    [
        SerialOpenProfile.PreserveDriverState,
        SerialOpenProfile.NoFlowPreserveLines,
        SerialOpenProfile.LegacyLinesOff,
        SerialOpenProfile.LegacyLinesOn,
        SerialOpenProfile.UseExisting9600WithoutSetCommState
    ];

    public string PortName => _portName;
    public bool IsOpen => _handle is { IsClosed: false, IsInvalid: false };
    public string OpenProfileName { get; private set; } = string.Empty;

    public void Open(string portName, SerialOpenProfile profile)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Natywny transport COM jest przeznaczony dla Windows.");
        if (IsOpen)
            throw new InvalidOperationException("Port COM jest już otwarty.");
        if (DcbSize != 28)
            throw new InvalidOperationException($"Nieprawidłowy układ struktury DCB: {DcbSize} bajtów zamiast 28.");

        var normalized = NormalizePortName(portName);
        var profileName = GetProfileName(profile);
        var handle = CreateFileW(
            $"\\\\.\\{normalized}",
            GenericRead | GenericWrite,
            0,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw NativeFailure(normalized, profileName, "CreateFile", error);
        }

        try
        {
            var dcb = new Dcb { DCBlength = checked((uint)DcbSize) };
            if (!GetCommState(handle, ref dcb))
                throw NativeFailure(normalized, profileName, "GetCommState", Marshal.GetLastWin32Error());

            var original = dcb;
            var requiresSetCommState = ConfigureProfile(ref dcb, profile, normalized, profileName);
            if (requiresSetCommState && !DcbEquals(original, dcb) && !SetCommState(handle, ref dcb))
                throw NativeFailure(normalized, profileName, "SetCommState", Marshal.GetLastWin32Error());

            var timeouts = new CommTimeouts
            {
                ReadIntervalTimeout = 50,
                ReadTotalTimeoutMultiplier = 0,
                ReadTotalTimeoutConstant = ReadTimeoutMilliseconds,
                WriteTotalTimeoutMultiplier = 0,
                WriteTotalTimeoutConstant = WriteTimeoutMilliseconds
            };
            if (!SetCommTimeouts(handle, ref timeouts))
                throw NativeFailure(normalized, profileName, "SetCommTimeouts", Marshal.GetLastWin32Error());

            if (!ClearCommError(handle, out _, IntPtr.Zero))
                throw NativeFailure(normalized, profileName, "ClearCommError", Marshal.GetLastWin32Error());

            if (!PurgeComm(handle, PurgeTxAbort | PurgeRxAbort | PurgeTxClear | PurgeRxClear))
                throw NativeFailure(normalized, profileName, "PurgeComm", Marshal.GetLastWin32Error());

            _portName = normalized;
            OpenProfileName = profileName;
            _handle = handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void DiscardBuffers()
    {
        var handle = RequireHandle();
        if (!ClearCommError(handle, out _, IntPtr.Zero))
            throw NativeFailure(PortName, OpenProfileName, "ClearCommError", Marshal.GetLastWin32Error());
        if (!PurgeComm(handle, PurgeRxAbort | PurgeRxClear))
            throw NativeFailure(PortName, OpenProfileName, "PurgeComm(RX)", Marshal.GetLastWin32Error());
    }

    public void WriteByte(byte value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new[] { value };
        if (!WriteFile(RequireHandle(), buffer, 1, out var written, IntPtr.Zero))
            throw NativeFailure(PortName, OpenProfileName, "WriteFile", Marshal.GetLastWin32Error());
        if (written != 1)
            throw new IOException($"Port {PortName} przyjął {written} z 1 bajtu.");
        cancellationToken.ThrowIfCancellationRequested();
    }

    public byte ReadByte(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[1];
        if (!ReadFile(RequireHandle(), buffer, 1, out var read, IntPtr.Zero))
            throw NativeFailure(PortName, OpenProfileName, "ReadFile", Marshal.GetLastWin32Error());
        cancellationToken.ThrowIfCancellationRequested();
        if (read == 0)
            throw new TimeoutException($"Brak echa z {PortName} przez {ReadTimeoutMilliseconds / 1000} s.");
        return buffer[0];
    }

    public byte[] ReadExact(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var result = new byte[count];
        var offset = 0;
        var handle = RequireHandle();

        while (offset < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = new byte[count - offset];
            if (!ReadFile(handle, chunk, checked((uint)chunk.Length), out var read, IntPtr.Zero))
                throw NativeFailure(PortName, OpenProfileName, "ReadFile", Marshal.GetLastWin32Error());
            if (read == 0)
                throw new TimeoutException($"Nie odebrano pełnej odpowiedzi z {PortName}: {offset}/{count} bajtów.");

            Buffer.BlockCopy(chunk, 0, result, offset, checked((int)read));
            offset += checked((int)read);
        }

        return result;
    }

    public void TrySendEscape()
    {
        if (!IsOpen)
            return;

        try
        {
            DiscardBuffers();
            WriteByte(0x1B, CancellationToken.None);
            Thread.Sleep(150);
            DiscardBuffers();
        }
        catch
        {
            // Odzyskiwanie jest best-effort; pierwotny błąd zostanie zgłoszony wyżej.
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, null);
        handle?.Dispose();
        _portName = string.Empty;
        OpenProfileName = string.Empty;
    }

    internal static string NormalizePortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Nie podano portu COM.", nameof(value));

        var text = value.Trim();
        if (text.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
            text = text[4..];

        if (!text.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(text.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 4096)
        {
            throw new ArgumentException($"Nieprawidłowa nazwa portu COM: {value}.", nameof(value));
        }

        return $"COM{number}";
    }

    internal static string[] GetPortNames() => SerialPort.GetPortNames()
        .Select(NormalizePortName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => int.Parse(name.AsSpan(3), CultureInfo.InvariantCulture))
        .ToArray();

    internal static int GetDcbLayoutSizeForSelfTest() => DcbSize;

    internal static string GetProfileName(SerialOpenProfile profile) => profile switch
    {
        SerialOpenProfile.PreserveDriverState => "DCB sterownika + 9600 8N1",
        SerialOpenProfile.NoFlowPreserveLines => "bez flow, stan DTR/RTS sterownika",
        SerialOpenProfile.LegacyLinesOff => "tryb klasyczny, DTR/RTS wyłączone",
        SerialOpenProfile.LegacyLinesOn => "tryb klasyczny, DTR/RTS włączone",
        SerialOpenProfile.UseExisting9600WithoutSetCommState => "bieżące 9600 8N1 bez SetCommState",
        _ => profile.ToString()
    };

    private static bool ConfigureProfile(
        ref Dcb dcb,
        SerialOpenProfile profile,
        string portName,
        string profileName)
    {
        if (profile == SerialOpenProfile.UseExisting9600WithoutSetCommState)
        {
            if (dcb.BaudRate != 9600 || dcb.ByteSize != 8 || dcb.Parity != 0 || dcb.StopBits != 0)
            {
                throw new SerialProfileNotApplicableException(
                    $"{portName}: profil „{profileName}” pominięty, bo bieżące DCB to " +
                    $"{dcb.BaudRate},{dcb.Parity},{dcb.ByteSize},{dcb.StopBits}.");
            }

            return false;
        }

        Apply9600EightNOne(ref dcb);
        switch (profile)
        {
            case SerialOpenProfile.PreserveDriverState:
                return true;
            case SerialOpenProfile.NoFlowPreserveLines:
                ClearFlowControl(ref dcb, preserveDtrRts: true);
                return true;
            case SerialOpenProfile.LegacyLinesOff:
                ClearFlowControl(ref dcb, preserveDtrRts: false);
                return true;
            case SerialOpenProfile.LegacyLinesOn:
                ClearFlowControl(ref dcb, preserveDtrRts: false);
                dcb.Flags |= DtrControlEnable | RtsControlEnable;
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, null);
        }
    }

    private static void Apply9600EightNOne(ref Dcb dcb)
    {
        dcb.DCBlength = checked((uint)DcbSize);
        dcb.BaudRate = 9600;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        dcb.Flags |= FBinary;
        dcb.Flags &= ~FParity;
        if (dcb.XonChar == dcb.XoffChar)
        {
            // SetCommState jawnie odrzuca identyczne znaki XON/XOFF, nawet gdy
            // program nie używa programowej kontroli przepływu.
            dcb.XonChar = 0x11;
            dcb.XoffChar = 0x13;
        }
    }

    private static void ClearFlowControl(ref Dcb dcb, bool preserveDtrRts)
    {
        const uint flowMask =
            FOutxCtsFlow | FOutxDsrFlow | FDsrSensitivity | FOutX | FInX |
            FErrorChar | FNull | FAbortOnError;
        dcb.Flags &= ~flowMask;
        if (!preserveDtrRts)
            dcb.Flags &= ~(FDtrControlMask | FRtsControlMask);
    }

    private static bool DcbEquals(Dcb left, Dcb right) =>
        left.DCBlength == right.DCBlength &&
        left.BaudRate == right.BaudRate &&
        left.Flags == right.Flags &&
        left.wReserved == right.wReserved &&
        left.XonLim == right.XonLim &&
        left.XoffLim == right.XoffLim &&
        left.ByteSize == right.ByteSize &&
        left.Parity == right.Parity &&
        left.StopBits == right.StopBits &&
        left.XonChar == right.XonChar &&
        left.XoffChar == right.XoffChar &&
        left.ErrorChar == right.ErrorChar &&
        left.EofChar == right.EofChar &&
        left.EvtChar == right.EvtChar &&
        left.wReserved1 == right.wReserved1;

    private SafeFileHandle RequireHandle()
    {
        var handle = _handle;
        if (handle is null || handle.IsClosed || handle.IsInvalid)
            throw new InvalidOperationException("Port COM nie jest otwarty.");
        return handle;
    }

    private static SerialPortOpenException NativeFailure(
        string portName,
        string profileName,
        string stage,
        int error)
    {
        var native = new Win32Exception(error);
        var hint = error switch
        {
            5 or 32 => " Port jest zajęty — zamknij oryginalne GUI u-Tracer i wszystkie inne programy korzystające z COM.",
            31 => " Sterownik zgłosił błąd urządzenia przed rozpoczęciem protokołu uTracera.",
            _ => string.Empty
        };
        return new SerialPortOpenException(
            portName,
            profileName,
            stage,
            error,
            $"{portName}, profil „{profileName}”, etap {stage}: {native.Message} (Win32 {error}).{hint}",
            native);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dcb
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public sbyte XonChar;
        public sbyte XoffChar;
        public sbyte ErrorChar;
        public sbyte EofChar;
        public sbyte EvtChar;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CommTimeouts
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommState(SafeFileHandle hFile, ref Dcb lpDcb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref CommTimeouts lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeComm(SafeFileHandle hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClearCommError(
        SafeFileHandle hFile,
        out uint lpErrors,
        IntPtr lpStat);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle hFile,
        [Out] byte[] lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle hFile,
        byte[] lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);
}

internal enum SerialOpenProfile
{
    PreserveDriverState,
    NoFlowPreserveLines,
    LegacyLinesOff,
    LegacyLinesOn,
    UseExisting9600WithoutSetCommState
}

internal sealed class SerialProfileNotApplicableException(string message) : IOException(message);

internal sealed class SerialPortOpenException(
    string portName,
    string profileName,
    string stage,
    int win32Error,
    string message,
    Exception innerException) : IOException(message, innerException)
{
    public string PortName { get; } = portName;
    public string ProfileName { get; } = profileName;
    public string Stage { get; } = stage;
    public int Win32Error { get; } = win32Error;

    public bool IsFatalBeforeConfiguration =>
        Stage is "CreateFile" or "GetCommState" or "SetCommTimeouts" or "ClearCommError" or "PurgeComm";
}
