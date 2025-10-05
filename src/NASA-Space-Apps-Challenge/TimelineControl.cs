using System;
using System.ComponentModel;
using System.Drawing;
using WF =System.Windows.Forms;

namespace TimelineWinForms {
    public enum TimelineStepUnit {
        Day,
        Week,
        Month
    }

    public class TimelineControl : UserControl {
        // --- public api ---
        private DateTime _rangeStart = new DateTime(DateTime.Today.Year, 1, 1);
        private DateTime _rangeEnd = new DateTime(DateTime.Today.Year, 12, 31);
        private DateTime _current = DateTime.Today;

        private double _pxPerDay = 10; // zoom (higher => more detailed)
        private TimelineStepUnit _stepUnit = TimelineStepUnit.Day;
        private bool _isPlaying = false;

        [Category("Timeline"), Description("Start of the entire timeline range")]
        public DateTime RangeStart { get => _rangeStart; set { _rangeStart = value; ClampCurrent(); Invalidate(); } }

        [Category("Timeline"), Description("End of the entire timeline range")]
        public DateTime RangeEnd { get => _rangeEnd; set { _rangeEnd = value; ClampCurrent(); Invalidate(); } }

        [Category("Timeline"), Description("Current date marker")]
        public DateTime Current { get => _current; set { _current = value; ClampCurrent(); Invalidate(); DateChanged?.Invoke(this, EventArgs.Empty); } }

        [Category("Timeline"), Description("Pixels per day (zoom)")]
        public double PixelsPerDay { get => _pxPerDay; set { _pxPerDay = Math.Max(0.2, Math.Min(60, value)); Invalidate(); OnSpanChanged?.Invoke(this, EventArgs.Empty); } }

        [Category("Timeline"), Description("Navigation step unit for prev/next")]
        public TimelineStepUnit StepUnit { get => _stepUnit; set { _stepUnit = value; Invalidate(); } }

        [Category("Timeline"), Description("If true, play button shows as active and auto-advances Current")]
        public bool IsPlaying { get => _isPlaying; set { _isPlaying = value; playTimer.Enabled = value; Invalidate(); OnPlayToggled?.Invoke(this, EventArgs.Empty); } }

        // events
        public event EventHandler DateChanged;
        public event EventHandler OnSpanChanged;
        public event EventHandler OnPlayToggled;

        // internals
        private readonly WF.Timer playTimer = new WF.Timer();
        private readonly StringFormat centerSF = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        private Rectangle rcChrome;    // left HUD like in screenshot
        private Rectangle rcPrev;
        private Rectangle rcNext;
        private Rectangle rcZoomOut;
        private Rectangle rcZoomIn;
        private Rectangle rcPlay;
        private Rectangle rcCam;
        private Rectangle rcScroll;    // main timeline band

        private bool dragging;
        private int dragStartX;
        private DateTime dragStartDate;

        public TimelineControl() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            this.Font = new Font("Segoe UI", 9f);

            playTimer.Interval = 350; // not too fast
            playTimer.Tick += (s, e) => Step(+1);

            this.MinimumSize = new Size(420, 60);
        }

        // --- helper mapping ---
        private double DaysTotal => (_rangeEnd - _rangeStart).TotalDays;
        private double TimelinePixelWidth => Math.Max(1, Width - 24); // small margin

        private double DaysPerPixel => 1.0 / _pxPerDay;

        private double DateToX(DateTime dt) {
            var clamped = dt < _rangeStart ? _rangeStart : (dt > _rangeEnd ? _rangeEnd : dt);
            return 12 + (clamped - _rangeStart).TotalDays * _pxPerDay;
        }

        private DateTime XToDate(int x) {
            double days = (x - 12) * DaysPerPixel;
            if (days < 0) days = 0;
            if (days > DaysTotal) days = DaysTotal;
            return _rangeStart.AddDays(days);
        }

        private void ClampCurrent() {
            if (_current < _rangeStart) _current = _rangeStart;
            if (_current > _rangeEnd) _current = _rangeEnd;
        }

        // --- navigation api ---
        public void Step(int dir) {
            // dir: -1 prev, +1 next
            var delta = _stepUnit switch {
                TimelineStepUnit.Day => TimeSpan.FromDays(1),
                TimelineStepUnit.Week => TimeSpan.FromDays(7),
                TimelineStepUnit.Month => TimeSpan.FromDays(30), // ehh good enough for nav
                _ => TimeSpan.FromDays(1)
            };
            Current = Current.AddDays(dir * delta.TotalDays);
        }

        public void Zoom(int dir, Point pivot) {
            // simple geometric zoom around pivot (1.2x)
            double factor = dir > 0 ? 1.2 : 1.0 / 1.2;
            var pivotDate = XToDate(pivot.X);
            var before = PixelsPerDay;
            PixelsPerDay = PixelsPerDay * factor;

            // adjust range to keep pivot fixed visually
            var daysOffset = (pivot.X - 12) * (1.0 / before - 1.0 / PixelsPerDay);
            _rangeStart = _rangeStart.AddDays(daysOffset);
            _rangeEnd = _rangeEnd.AddDays(daysOffset);
            ClampCurrent();
            Invalidate();
        }

        // --- paint ---
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // layout measurements
            int h = Height;
            int w = Width;

            rcChrome = new Rectangle(8, 8, Math.Min(360, w / 3), 44);
            rcScroll = new Rectangle(0, 0, w, h);

            DrawTimelineBand(g, rcScroll);
            DrawHud(g, rcChrome);
        }

        private void DrawTimelineBand(Graphics g, Rectangle rc) {
            var bandH = Math.Max(24, rc.Height - 10);
            var bandY = rc.Height - bandH - 2;
            var band = new Rectangle(0, bandY, rc.Width, bandH);

            using var dark = new SolidBrush(Color.FromArgb(160, 30, 30, 30));
            g.FillRectangle(dark, band);

            // blue fill strip at top of band
            var topStrip = new Rectangle(0, bandY, rc.Width, 14);
            using var blue = new SolidBrush(Color.FromArgb(220, 21, 115, 191));
            g.FillRectangle(blue, topStrip);

            // draw day ticks across full band
            var firstDay = _rangeStart.Date;
            var lastDay = _rangeEnd.Date;
            for (var d = firstDay; d <= lastDay; d = d.AddDays(1)) {
                float x = (float)DateToX(d);
                if (x < 0 || x > rc.Width) continue;

                // major ticks at month boundaries
                bool isMonth = d.Day == 1;
                bool is5 = d.Day % 5 == 0;
                int tickH = isMonth ? band.Height - 2 : (is5 ? 18 : 12);
                int y = bandY + (isMonth ? 0 : 6);
                using var tickPen = new Pen(Color.FromArgb(isMonth ? 255 : 180, 230, 230, 230), isMonth ? 2f : 1f);
                g.DrawLine(tickPen, x, y, x, y + tickH);

                // month labels near bottom
                if (isMonth) {
                    var label = d.ToString("MMM yyyy").ToUpper();
                    var r = new RectangleF(x + 6, band.Bottom - 20, 160, 18);
                    using var sb = new SolidBrush(Color.FromArgb(240, 230, 230, 230));
                    using var f = new Font(Font.FontFamily, 10, FontStyle.Bold);
                    g.DrawString(label, f, sb, r);
                }
            }

            // current marker
            float cx = (float)DateToX(Current);
            using var white = new Pen(Color.White, 2f);
            g.DrawLine(white, cx, bandY - 8, cx, band.Bottom);
            // draw caret-like cap
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddPolygon(new[] { new PointF(cx, bandY - 10), new PointF(cx - 8, bandY - 2), new PointF(cx + 8, bandY - 2) });
            using var cap = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
            g.FillPath(cap, path);
        }

        private void DrawHud(Graphics g, Rectangle rc) {
            // hud panel
            using var bg = new SolidBrush(Color.FromArgb(210, 30, 30, 30));
            using var outline = new Pen(Color.FromArgb(80, 255, 255, 255));
            g.FillRoundedRectangle(bg, rc, 6);
            g.DrawRoundedRectangle(outline, rc, 6);

            // date block on the left
            string dateText = Current.ToString("yyyy MMM dd").ToUpper();
            using var fDate = new Font("Segoe UI", 14, FontStyle.Bold);
            var rcDate = new Rectangle(rc.X + 8, rc.Y + 6, 180, rc.Height - 12);
            TextRenderer.DrawText(g, dateText, fDate, rcDate, Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // little label for step span
            var stepLabel = _stepUnit switch { TimelineStepUnit.Day => "1 DAY", TimelineStepUnit.Week => "1 WEEK", _ => "1 MONTH" };
            using var fSmall = new Font("Segoe UI", 8, FontStyle.Regular);
            TextRenderer.DrawText(g, stepLabel, fSmall, new Rectangle(rcDate.Right + 2, rc.Y + 2, 60, 14), Color.Gainsboro);

            // buttons cluster
            int bx = rcDate.Right + 18; int by = rc.Y + 10; int sz = 24; int gap = 6;

            rcPrev = new Rectangle(bx, by, sz, sz); bx += sz + 2;
            rcNext = new Rectangle(bx, by, sz, sz); bx += sz + 10;
            rcZoomOut = new Rectangle(bx, by, sz, sz); bx += sz + 4;
            rcZoomIn = new Rectangle(bx, by, sz, sz); bx += sz + 10;
            rcPlay = new Rectangle(bx, by, sz, sz); bx += sz + 10;
            rcCam = new Rectangle(bx, by, sz, sz);

            DrawIconButton(g, rcPrev, "<");
            DrawIconButton(g, rcNext, ">");
            DrawIconButton(g, rcZoomOut, "–"); // en dash as minus
            DrawIconButton(g, rcZoomIn, "+");
            DrawPlayButton(g, rcPlay, _isPlaying);
            DrawCamBadge(g, rcCam);
        }

        private void DrawIconButton(Graphics g, Rectangle r, string glyph) {
            using var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
            using var bHot = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            bool hot = r.Contains(PointToClient(MousePosition));
            g.FillRoundedRectangle(hot ? bHot : b, r, 4);
            using var f = new Font("Segoe UI", 12, FontStyle.Bold);
            TextRenderer.DrawText(g, glyph, f, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawPlayButton(Graphics g, Rectangle r, bool playing) {
            using var b = new SolidBrush(Color.FromArgb(playing ? 170 : 90, 71, 196, 108));
            g.FillRoundedRectangle(b, r, 4);
            using var pen = new Pen(Color.Black, 2f);
            if (!playing) {
                // triangle
                var p1 = new Point(r.X + 8, r.Y + 6);
                var p2 = new Point(r.Right - 8, r.Y + r.Height / 2);
                var p3 = new Point(r.X + 8, r.Bottom - 6);
                g.FillPolygon(Brushes.Black, new[] { p1, p2, p3 });
            }
            else {
                // pause bars
                var barW = 5; var pad = 6;
                g.FillRectangle(Brushes.Black, new Rectangle(r.X + pad, r.Y + 6, barW, r.Height - 12));
                g.FillRectangle(Brushes.Black, new Rectangle(r.Right - pad - barW, r.Y + 6, barW, r.Height - 12));
            }
        }

        private void DrawCamBadge(Graphics g, Rectangle r) {
            using var b = new SolidBrush(Color.FromArgb(100, 255, 255, 255));
            g.FillRoundedRectangle(b, r, 6);
            // quick n dirty camera glyph
            var body = new Rectangle(r.X + 5, r.Y + 7, r.Width - 10, r.Height - 14);
            g.FillRectangle(Brushes.Black, body);
            var lens = new Rectangle(r.X + r.Width / 2 - 4, r.Y + r.Height / 2 - 4, 8, 8);
            g.FillEllipse(Brushes.White, lens);
        }

        // --- mouse ---
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (rcPrev.Contains(e.Location)) { Step(-1); return; }
            if (rcNext.Contains(e.Location)) { Step(+1); return; }
            if (rcZoomIn.Contains(e.Location)) { Zoom(+1, e.Location); return; }
            if (rcZoomOut.Contains(e.Location)) { Zoom(-1, e.Location); return; }
            if (rcPlay.Contains(e.Location)) { IsPlaying = !IsPlaying; return; }
            if (rcScroll.Contains(e.Location)) {
                dragging = true;
                dragStartX = e.X;
                dragStartDate = Current;
                this.Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            if (dragging) {
                int dx = e.X - dragStartX;
                double dDays = dx * DaysPerPixel;
                Current = dragStartDate.AddDays(dDays);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (dragging) {
                dragging = false;
                this.Capture = false;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            Zoom(e.Delta > 0 ? +1 : -1, e.Location);
        }
    }

    internal static class GraphicsExt {
        // tiny helpers, keep gc pressure low
        public static void FillRoundedRectangle(this Graphics g, Brush b, Rectangle r, int radius) {
            using var gp = Rounded(r, radius);
            g.FillPath(b, gp);
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen p, Rectangle r, int radius) {
            using var gp = Rounded(r, radius);
            g.DrawPath(p, gp);
        }

        private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius) {
            var d = radius * 2;
            var gp = new System.Drawing.Drawing2D.GraphicsPath();
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }
}
