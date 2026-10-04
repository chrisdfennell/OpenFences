using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Pickets.Services
{
    /// <summary>
    /// A click-through window over every monitor that draws one rectangle given in screen
    /// pixels (the desktop lasso and the right-drag "new fence" box). One window can only have
    /// one scale, so it's sized in screen pixels and converts with its own scale; nothing is
    /// stretched, so the box lines up on every monitor even when they're scaled differently.
    /// </summary>
    internal sealed class ScreenOverlay : Window
    {
        private readonly Canvas _canvas = new();
        private readonly System.Windows.Shapes.Rectangle _rect = new();

        public ScreenOverlay(Brush stroke, double strokeThickness, Brush fill, double cornerRadius, bool topmost)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = topmost;
            IsHitTestVisible = false; // never block clicks

            _rect.Stroke = stroke;
            _rect.StrokeThickness = strokeThickness;
            _rect.Fill = fill;
            _rect.RadiusX = _rect.RadiusY = cornerRadius;
            _rect.Visibility = Visibility.Collapsed; // until the first update
            _canvas.Children.Add(_rect);
            Content = _canvas;

            SourceInitialized += (_, __) =>
            {
                var v = VirtualScreenPx();
                SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
                             (int)v.X, (int)v.Y, (int)v.Width, (int)v.Height, SWP_NOZORDER | SWP_NOACTIVATE);
            };
        }

        /// <summary>Draw the rectangle at <paramref name="px"/> (screen pixels).</summary>
        public void UpdateRectPx(Rect px)
        {
            if (px.Width < 0 || px.Height < 0) return;
            var v = VirtualScreenPx();
            double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Canvas.SetLeft(_rect, (px.X - v.X) / s);
            Canvas.SetTop(_rect, (px.Y - v.Y) / s);
            _rect.Width = px.Width / s;
            _rect.Height = px.Height / s;
            _rect.Visibility = Visibility.Visible;
        }

        private static Rect VirtualScreenPx() => new(
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

        private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
