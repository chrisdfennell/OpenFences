using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Pickets.Controls
{
    /// <summary>
    /// A small filled line graph of recent values (each 0…1, oldest first) across the control's
    /// width, as on a system monitor tile. Fewer values than <see cref="Capacity"/> start from
    /// the right, so the line grows in from the right edge the way Task Manager's does.
    /// </summary>
    public class Sparkline : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<double>? Values
        {
            get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        /// <summary>How many values span the full width.</summary>
        public int Capacity
        {
            get => (int)GetValue(CapacityProperty);
            set => SetValue(CapacityProperty, value);
        }

        public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
            nameof(Capacity), typeof(int), typeof(Sparkline),
            new FrameworkPropertyMetadata(Pickets.MonitorTile.DefaultCapacity, FrameworkPropertyMetadataOptions.AffectsRender));

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var values = Values;
            if (w <= 0 || h <= 0 || values == null || values.Count == 0) return;

            int cap = System.Math.Max(2, Capacity);
            double step = w / (cap - 1);
            double x0 = w - step * (values.Count - 1);
            const double pad = 1; // keep the line's thickness inside the box
            double Y(double v) => pad + (h - 2 * pad) * (1 - v);

            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var l = line.Open())
            using (var a = area.Open())
            {
                var first = new Point(x0, Y(values[0]));
                l.BeginFigure(first, false, false);
                a.BeginFigure(new Point(x0, h), true, true);
                a.LineTo(first, false, false);
                for (int i = 1; i < values.Count; i++)
                {
                    var p = new Point(x0 + i * step, Y(values[i]));
                    l.LineTo(p, true, true);
                    a.LineTo(p, false, false);
                }
                a.LineTo(new Point(x0 + (values.Count - 1) * step, h), false, false);
            }
            line.Freeze();
            area.Freeze();

            var fill = Stroke.CloneCurrentValue();
            fill.Opacity = 0.22;
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
            var pen = new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
        }
    }
}
