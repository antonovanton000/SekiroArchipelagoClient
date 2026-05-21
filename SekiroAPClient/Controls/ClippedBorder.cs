using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace InjustUILibrary.Controls;
public class ClippedBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == CornerRadiusProperty ||
            e.Property == BorderThicknessProperty)
        {
            UpdateClip();
        }
    }

    private void UpdateClip()
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);

        if (rect.IsEmpty)
        {
            Clip = null;
            return;
        }

        var r = CornerRadius;

        double tl = Math.Max(0, r.TopLeft);
        double tr = Math.Max(0, r.TopRight);
        double br = Math.Max(0, r.BottomRight);
        double bl = Math.Max(0, r.BottomLeft);

        double maxRadiusX = rect.Width / 2;
        double maxRadiusY = rect.Height / 2;

        tl = Math.Min(Math.Min(tl, maxRadiusX), maxRadiusY);
        tr = Math.Min(Math.Min(tr, maxRadiusX), maxRadiusY);
        br = Math.Min(Math.Min(br, maxRadiusX), maxRadiusY);
        bl = Math.Min(Math.Min(bl, maxRadiusX), maxRadiusY);

        var geo = new StreamGeometry();

        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(
                new Point(rect.X + tl, rect.Y), // startPoint
                isFilled: true,
                isClosed: true);

            ctx.LineTo(new Point(rect.Right - tr, rect.Y), isStroked: true, isSmoothJoin: false);
            if (tr > 0)
            {
                ctx.ArcTo(
                    new Point(rect.Right, rect.Y + tr),
                    new Size(tr, tr),
                    rotationAngle: 0,
                    isLargeArc: false,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }

            ctx.LineTo(new Point(rect.Right, rect.Bottom - br), isStroked: true, isSmoothJoin: false);
            if (br > 0)
            {
                ctx.ArcTo(
                    new Point(rect.Right - br, rect.Bottom),
                    new Size(br, br),
                    rotationAngle: 0,
                    isLargeArc: false,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }

            ctx.LineTo(new Point(rect.X + bl, rect.Bottom), isStroked: true, isSmoothJoin: false);
            if (bl > 0)
            {
                ctx.ArcTo(
                    new Point(rect.X, rect.Bottom - bl),
                    new Size(bl, bl),
                    rotationAngle: 0,
                    isLargeArc: false,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }

            ctx.LineTo(new Point(rect.X, rect.Y + tl), isStroked: true, isSmoothJoin: false);
            if (tl > 0)
            {
                ctx.ArcTo(
                    new Point(rect.X + tl, rect.Y),
                    new Size(tl, tl),
                    rotationAngle: 0,
                    isLargeArc: false,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }
        }

        geo.Freeze();
        Clip = geo;
    }
}
