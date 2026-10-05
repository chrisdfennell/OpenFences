using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Pickets.Controls
{
    /// <summary>
    /// A system monitor fence's tiles for screen readers: a list named after the fence whose
    /// entries read their current value ("CPU, 37 percent"). Values change every couple of
    /// seconds, so nothing is announced on its own; a tile is read when it gets focus.
    /// </summary>
    public class MonitorItemsControl : ItemsControl
    {
        protected override DependencyObject GetContainerForItemOverride() => new MonitorTileView();
        protected override bool IsItemItsOwnContainerOverride(object item) => item is MonitorTileView;
        protected override AutomationPeer OnCreateAutomationPeer() => new MonitorListPeer(this);

        private sealed class MonitorListPeer : FrameworkElementAutomationPeer
        {
            public MonitorListPeer(MonitorItemsControl owner) : base(owner) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
            protected override string GetClassNameCore() => "MonitorTiles";
        }
    }

    /// <summary>One monitor tile (or list row). Focusable so arrow keys can move along them.</summary>
    public class MonitorTileView : ContentControl
    {
        public MonitorTileView()
        {
            Focusable = true;
            IsTabStop = false;
        }

        internal MonitorTile? Tile => DataContext as MonitorTile;

        protected override AutomationPeer OnCreateAutomationPeer() => new MonitorTilePeer(this);

        private sealed class MonitorTilePeer : FrameworkElementAutomationPeer
        {
            public MonitorTilePeer(MonitorTileView owner) : base(owner) { }

            private MonitorTileView View => (MonitorTileView)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
            protected override string GetClassNameCore() => "MonitorTile";
            protected override string GetNameCore() => View.Tile?.Spoken ?? "";
            protected override string GetHelpTextCore() => "Press Enter to open more details";
            protected override bool IsKeyboardFocusableCore() => true;
            protected override List<AutomationPeer>? GetChildrenCore() => null; // the name says it all

            protected override int GetPositionInSetCore()
            {
                var list = ItemsControl.ItemsControlFromItemContainer(View);
                return list == null ? -1 : list.ItemContainerGenerator.IndexFromContainer(View) + 1;
            }

            protected override int GetSizeOfSetCore() =>
                ItemsControl.ItemsControlFromItemContainer(View)?.Items.Count ?? -1;
        }
    }

    /// <summary>A row of thin vertical bars (each 0…1), one per processor core.</summary>
    public class BarGraph : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(IReadOnlyList<double>), typeof(BarGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(BarGraph),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(Brush), typeof(BarGraph),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<double>? Values
        {
            get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush Track
        {
            get => (Brush)GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var values = Values;
            if (w <= 0 || h <= 0 || values == null || values.Count == 0) return;

            // Bars share the width with a gap between them that shrinks as cores are added.
            int n = values.Count;
            double gap = System.Math.Min(3, w / n * 0.25);
            double bw = System.Math.Max(1, (w - gap * (n - 1)) / n);
            for (int i = 0; i < n; i++)
            {
                double x = i * (bw + gap);
                dc.DrawRectangle(Track, null, new Rect(x, 0, bw, h));
                double bh = h * System.Math.Clamp(values[i], 0, 1);
                if (bh > 0) dc.DrawRectangle(Fill, null, new Rect(x, h - bh, bw, bh));
            }
        }
    }

    /// <summary>A thin rounded fill bar (0…1), for disk space and battery tiles.</summary>
    public class MeterBar : FrameworkElement
    {
        public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
            nameof(Fraction), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(MeterBar),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
            nameof(Track), typeof(Brush), typeof(MeterBar),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Fraction
        {
            get => (double)GetValue(FractionProperty);
            set => SetValue(FractionProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush Track
        {
            get => (Brush)GetValue(TrackProperty);
            set => SetValue(TrackProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = h / 2;
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
            double f = System.Math.Clamp(Fraction, 0, 1);
            if (f > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, System.Math.Max(h, w * f), h), r, r);
        }
    }
}
