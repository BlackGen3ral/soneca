using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Soneca;

public partial class MainWindow
{
    private bool backdropRunning;

    private void UpdateBackdropMotion()
    {
        bool animate = !demo && IsLoaded && IsActive && WindowState != WindowState.Minimized
            && SystemParameters.ClientAreaAnimation && !IntroOverlay.IsVisible;
        if (animate == backdropRunning) return;
        backdropRunning = animate;
        ApplyBackdropMotion(BackdropGlow, -130, 78, 19, animate);
        ApplyBackdropMotion(BackdropWash, 110, -47, 25, animate);
    }

    private static void ApplyBackdropMotion(TranslateTransform transform, double x, double y, int seconds, bool animate)
    {
        if (!animate)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }
        var motion = new DoubleAnimation(0, x, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, motion);
        var vertical = motion.Clone();
        vertical.To = y;
        transform.BeginAnimation(TranslateTransform.YProperty, vertical);
    }
}
