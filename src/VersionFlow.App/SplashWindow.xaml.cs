using System.Windows;
using System.Windows.Media.Animation;

namespace VersionFlow.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            CardRoot.Opacity = 1;
            CardScale.ScaleX = 1;
            CardScale.ScaleY = 1;
            LogoScale.ScaleX = 1;
            LogoScale.ScaleY = 1;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        var scaleCard = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        var scaleLogo = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };

        CardRoot.BeginAnimation(OpacityProperty, fadeIn);
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleCard);
        CardScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleCard);
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleLogo);
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleLogo);

        var sweep = new DoubleAnimation(-110, 360, TimeSpan.FromMilliseconds(950))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        SweepTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, sweep);
    }

    public void SetStatus(string text)
    {
        Dispatcher.Invoke(() => StatusText.Text = text);
    }

    public async Task FadeOutAndCloseAsync()
    {
        if (SystemParameters.ClientAreaAnimation)
        {
            var tcs = new TaskCompletionSource();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => tcs.TrySetResult();
            CardRoot.BeginAnimation(OpacityProperty, fade);
            await tcs.Task;
        }
        Close();
    }
}
