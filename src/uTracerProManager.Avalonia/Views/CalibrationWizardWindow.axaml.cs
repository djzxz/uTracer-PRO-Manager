using Avalonia.Controls;
using Avalonia.Interactivity;
using uTracerProManager.AvaloniaApp.ViewModels;

namespace uTracerProManager.AvaloniaApp.Views;

public sealed partial class CalibrationWizardWindow : Window
{
    private readonly MainWindowViewModel _ownerViewModel;
    private readonly CalibrationWizardViewModel _viewModel;

    public CalibrationWizardWindow(MainWindowViewModel ownerViewModel)
    {
        _ownerViewModel = ownerViewModel;
        _viewModel = new CalibrationWizardViewModel(ownerViewModel.CalibrationForWizard);
        DataContext = _viewModel;
        InitializeComponent();
        Opened += (_, _) => FitToScreen();
        Closed += async (_, _) => await TryStopAsync();
    }

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var scale = screen.Scaling;
        Width = Math.Min(1220, screen.WorkingArea.Width / scale - 30);
        Height = Math.Min(790, screen.WorkingArea.Height / scale - 30);
        MinWidth = Math.Min(900, Width);
        MinHeight = Math.Min(620, Height);
    }

    private void RequireNoTube()
    {
        if (!_viewModel.NoTubeConfirmed)
            throw new InvalidOperationException("Najpierw potwierdź, że w podstawce nie ma lampy.");
    }

    private async void ReadIdle_Click(object? sender, RoutedEventArgs e)
    {
        await RunHardwareActionAsync(async () =>
            _viewModel.HardwareStatus = await _ownerViewModel.ReadCalibrationIdleAsync(_viewModel.Build()));
    }

    private void ApplySupply_Click(object? sender, RoutedEventArgs e) => RunMath(_viewModel.ApplySupplyMath, "Przeliczono Vsu i Vn. Powtórz odczyt i zaznacz weryfikację dopiero po zgodności.");
    private void ApplyGrid_Click(object? sender, RoutedEventArgs e) => RunMath(_viewModel.ApplyGridMath, "Wyliczono Vg offset/slope. Powtórz oba punkty po korekcie.");
    private void ApplyBoost_Click(object? sender, RoutedEventArgs e) => RunMath(_viewModel.ApplyBoostMath, "Przeliczono Va/Vs. Powtórz oba pomiary po korekcie.");
    private void ApplyCurrent_Click(object? sender, RoutedEventArgs e) => RunMath(_viewModel.ApplyCurrentMath, "Przeliczono Ia/Is. Zweryfikuj ponownie na rezystorach wzorcowych.");

    private async void HoldGridHalf_Click(object? sender, RoutedEventArgs e) =>
        await RunHardwareActionAsync(async () => await _ownerViewModel.HoldCalibrationGridAsync(_viewModel.Build(), -0.5));

    private async void HoldGridForty_Click(object? sender, RoutedEventArgs e) =>
        await RunHardwareActionAsync(async () => await _ownerViewModel.HoldCalibrationGridAsync(_viewModel.Build(), -40));

    private async void HoldAnode_Click(object? sender, RoutedEventArgs e) =>
        await RunHardwareActionAsync(async () => await _ownerViewModel.HoldCalibrationBoostAsync(_viewModel.Build(), true, _viewModel.TargetBoostVoltage));

    private async void HoldScreen_Click(object? sender, RoutedEventArgs e) =>
        await RunHardwareActionAsync(async () => await _ownerViewModel.HoldCalibrationBoostAsync(_viewModel.Build(), false, _viewModel.TargetBoostVoltage));

    private async void StopOutput_Click(object? sender, RoutedEventArgs e) => await TryStopAsync();

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await TryStopAsync();
            var profile = _viewModel.Build();
            var errors = profile.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            await _ownerViewModel.SaveCalibrationFromWizardAsync(profile);
            _viewModel.HardwareStatus = profile.IsCompleteForTubeTesting
                ? "Zapisano kompletną kalibrację v2."
                : "Zapisano kalibrację roboczą; pomiar lampy pozostaje zablokowany.";
            Close(profile.IsCompleteForTubeTesting);
        }
        catch (Exception ex)
        {
            _viewModel.HardwareStatus = "Nie zapisano: " + ex.Message;
        }
    }

    private async Task RunHardwareActionAsync(Func<Task> action)
    {
        try
        {
            RequireNoTube();
            await action();
            _viewModel.HardwareStatus = _ownerViewModel.StatusMessage;
        }
        catch (Exception ex)
        {
            _viewModel.HardwareStatus = "Błąd: " + ex.Message;
        }
    }

    private void RunMath(Action action, string success)
    {
        try
        {
            action();
            _viewModel.HardwareStatus = success;
        }
        catch (Exception ex)
        {
            _viewModel.HardwareStatus = "Nie obliczono: " + ex.Message;
        }
    }

    private async Task TryStopAsync()
    {
        try
        {
            await _ownerViewModel.StopCalibrationOutputAsync();
            _viewModel.HardwareStatus = "Wyjścia wyłączone. Odczekaj i sprawdź spadek napięcia multimetrem.";
        }
        catch
        {
            _viewModel.HardwareStatus = "Brak aktywnego połączenia sprzętowego; nie wysłano STOP.";
        }
    }
}
