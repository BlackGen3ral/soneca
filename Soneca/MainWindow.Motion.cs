using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Soneca;

public partial class MainWindow
{
    private FrameworkElement? animatedPage;
    private static readonly TimeSpan PageMotionDuration = TimeSpan.FromMilliseconds(180);

    private void ShowPageMotion(FrameworkElement page)
    {
        StopPageMotion();
        if (!page.IsVisible || IntroOverlay.IsVisible || WindowState == WindowState.Minimized ||
            !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;

        animatedPage = page;
        var offset = new TranslateTransform();
        page.RenderTransform = offset;
        var fade = PageMotion(.78, 1);
        fade.Completed += (_, _) =>
        {
            if (animatedPage == page && ReferenceEquals(page.RenderTransform, offset)) StopPageMotion();
        };
        offset.BeginAnimation(TranslateTransform.YProperty, PageMotion(7, 0));
        page.BeginAnimation(OpacityProperty, fade);
    }

    private static DoubleAnimation PageMotion(double from, double to)
    {
        var motion = new DoubleAnimation(from, to, new Duration(PageMotionDuration))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(motion, 30);
        return motion;
    }

    private void StopPageMotion()
    {
        if (animatedPage is not { } page) return;
        animatedPage = null;
        page.BeginAnimation(OpacityProperty, null);
        if (page.RenderTransform is TranslateTransform offset)
            offset.BeginAnimation(TranslateTransform.YProperty, null);
        page.ClearValue(RenderTransformProperty);
    }
}
