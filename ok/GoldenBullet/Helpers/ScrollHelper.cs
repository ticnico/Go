// File: Helpers/ScrollHelper.cs
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Helpers  // ⚠️ MUST match your project namespace
{
    public static class ScrollHelper
    {
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.RegisterAttached(
                "AutoScroll",
                typeof(bool),
                typeof(ScrollHelper),
                new PropertyMetadata(false, OnAutoScrollChanged));

        public static void SetAutoScroll(DependencyObject element, bool value) =>
            element.SetValue(AutoScrollProperty, value);

        public static bool GetAutoScroll(DependencyObject element) =>
            (bool)element.GetValue(AutoScrollProperty);

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer && (bool)e.NewValue)
            {
                // Scroll to bottom when layout updates (e.g., new text added)
                scrollViewer.LayoutUpdated += (s, args) =>
                {
                    if (scrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                    {
                        scrollViewer.ScrollToEnd();
                    }
                };
            }
        }
    }
}
