using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Input;

namespace GoldenBullet.Helpers
{
    public static class NumericUpDownBehavior
    {
        // 1. Define the Attached Property
        public static readonly DependencyProperty SuppressEnterKeyProperty =
            DependencyProperty.RegisterAttached(
                "SuppressEnterKey",
                typeof(bool),
                typeof(NumericUpDownBehavior),
                new PropertyMetadata(false, OnSuppressEnterKeyChanged));

        // 2. Getter and Setter
        public static void SetSuppressEnterKey(UIElement element, bool value) =>
            element.SetValue(SuppressEnterKeyProperty, value);

        public static bool GetSuppressEnterKey(UIElement element) =>
            (bool)element.GetValue(SuppressEnterKeyProperty);

        // 3. Logic to Attach/Detach Event
        private static void OnSuppressEnterKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDown control)
            {
                control.PreviewKeyDown -= Control_PreviewKeyDown; // Always remove first
                if ((bool)e.NewValue)
                    control.PreviewKeyDown += Control_PreviewKeyDown;
            }
        }

        // 4. The Event Handler that Blocks Enter
        private static void Control_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true; // Stops the Enter key from bubbling up
            }
        }
    }
}
