using Avalonia;

namespace uTracerProManager.AvaloniaApp.ViewModels;

public sealed class PinoutPinViewModel
{
    public PinoutPinViewModel(int number, string function, double x, double y, string fill)
    {
        Number = number;
        Function = function;
        Position = new Thickness(x, y, 0, 0);
        Fill = fill;
    }

    public int Number { get; }
    public string Function { get; }
    public Thickness Position { get; }
    public string Fill { get; }
    public string Tooltip => string.IsNullOrWhiteSpace(Function)
        ? $"Pin {Number}: brak opisu w profilu"
        : $"Pin {Number}: {Function}";
}
