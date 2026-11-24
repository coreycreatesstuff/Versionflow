using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VersionFlow.App.ViewModels;
using VersionFlow.Core.Interfaces;

namespace VersionFlow.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settingsService;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = _viewModel;

        RestoreWindowPlacement();
        SourceInitialized += (_, _) => ApplyTitleBarTheme(_settingsService.Current.Theme);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
                AnimatePageTransition();
            else if (e.PropertyName == nameof(MainViewModel.HasResult) && _viewModel.HasResult)
                AnimateCardEntrance(ResultCardBorder);
            else if (e.PropertyName == nameof(MainViewModel.HasLoadedProject) && _viewModel.HasLoadedProject)
                AnimateCardEntrance(LoadedProjectCard);
        };
    }

    public void AnimateWindowEntrance()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Opacity = 1;
            return;
        }

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void AnimatePageTransition()
    {
        if (!SystemParameters.ClientAreaAnimation) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease };

        PageViewport.BeginAnimation(OpacityProperty, fade);
        PageSlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private static void AnimateCardEntrance(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, fade);
    }

    public void ApplyTitleBarTheme(string themeMode)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            bool isLight = themeMode switch
            {
                "Light" => true,
                "System" => App.IsWindowsLightTheme(),
                _ => false
            };

            int useDark = isLight ? 0 : 1;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
            // Ignore on older Windows builds without DWM dark title bar support
        }
    }

    private void RestoreWindowPlacement()
    {
        var s = _settingsService.Current;
        if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        if (s.WindowLeft.HasValue && s.WindowTop.HasValue)
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            if (s.WindowLeft.Value >= vLeft - 50 && s.WindowLeft.Value <= vLeft + vWidth - 100
                && s.WindowTop.Value >= vTop - 50 && s.WindowTop.Value <= vTop + vHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = s.WindowLeft.Value;
                Top = s.WindowTop.Value;
            }
        }

        if (s.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var s = _settingsService.Current;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            s.WindowWidth = bounds.Width;
            s.WindowHeight = bounds.Height;
            s.WindowLeft = bounds.Left;
            s.WindowTop = bounds.Top;
        }
        _ = _settingsService.SaveAsync(s);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool valid = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = valid ? DragDropEffects.Copy : DragDropEffects.None;
        if (valid)
            DropZoneOutline.Stroke = (Brush)FindResource("AccentBrush");
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        DropZoneOutline.Stroke = (Brush)FindResource("BorderHoverBrush");
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropZoneOutline.Stroke = (Brush)FindResource("BorderHoverBrush");
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await _viewModel.HandleDroppedFilesAsync(files);
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && FocusManager.GetFocusedElement(this) is not System.Windows.Controls.TextBox)
        {
            await _viewModel.PastePathFromClipboardCommand.ExecuteAsync(null);
        }
    }
}
