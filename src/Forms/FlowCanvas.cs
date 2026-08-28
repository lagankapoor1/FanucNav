using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FanucNav.Fanuc;

namespace FanucNav.Forms
{
    internal sealed class FlowCanvas : Panel
    {
        private MapStep _root;
        private readonly List<Box> _boxes = new List<Box>();

        public event Action<MapStep> StepClick;

        public FlowCanvas()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(252, 250, 246);
            Font = new Font("Segoe UI", 8.5F);
            MouseClick += OnClick;
        }

        public void SetRoot(MapStep root)
        {
            _root = root;
            LayoutBoxes();
            Invalidate();
        }

        private sealed class Box
        {
            public MapStep Step;
            public Rectangle Bounds;
        }

        private void LayoutBoxes()
        {
            _boxes.Clear();
            int y = 12;
            int x = 16;
            int w = Math.Max(300, ClientSize.Width - 56);
            if (_root != null)
            {
                AddBox(_root, x, ref y, w, 0);
                foreach (var child in _root.Children)
                    LayoutNode(child, x, ref y, w, 0);
            }
            AutoScrollMinSize = new Size(w + 80, y + 24);
        }

        private void LayoutNode(MapStep step, int x, ref int y, int w, int indent)
        {
            AddBox(step, x, ref y, w, indent);
            foreach (var c in step.Children)
                LayoutNode(c, x, ref y, w, indent + 1);
        }

        private void AddBox(MapStep step, int x, ref int y, int w, int indent)
        {
            int ix = x + indent * 18;
            int h = step.Kind == "LBL" || step.Kind == "PROG" ? 30 : 24;
            var box = new Box();
            box.Step = step;
            box.Bounds = new Rectangle(ix, y, Math.Max(120, w - indent * 18), h);
            _boxes.Add(box);
            y += h + 6;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_boxes.Count == 0 && _root != null)
                LayoutBoxes();

            DrawArrows(e.Graphics);

            foreach (var b in _boxes)
            {
                var step = b.Step;
                Color fill = Color.White;
                Color border = Color.FromArgb(180, 176, 168);
                Color text = UiTheme.Fg;
                if (step.Kind == "PROG") { fill = Color.FromArgb(168, 108, 8); text = Color.White; border = fill; }
                else if (step.Kind == "LBL") { fill = Color.FromArgb(224, 238, 248); border = Color.FromArgb(20, 90, 140); text = Color.FromArgb(20, 90, 140); }
                else if (step.Kind == "JMP") { fill = Color.FromArgb(255, 244, 220); border = UiTheme.Accent; text = UiTheme.Accent; }
                else if (step.Kind == "CALL") { fill = Color.FromArgb(228, 246, 232); border = Color.FromArgb(20, 110, 70); text = Color.FromArgb(20, 110, 70); }
                else if (step.Kind == "MISS" || step.Flag == "MISSING") { fill = Color.FromArgb(255, 228, 224); border = Color.FromArgb(170, 30, 30); text = Color.FromArgb(170, 30, 30); }
                else if (step.Flag == "UNUSED") { fill = Color.FromArgb(240, 240, 240); border = UiTheme.Dim; text = UiTheme.Dim; }
                else if (step.Kind == "UALM" || step.Kind == "ABORT") { fill = Color.FromArgb(255, 228, 224); border = Color.FromArgb(160, 20, 20); text = Color.FromArgb(160, 20, 20); }
                else if (step.Kind == "MSG") { fill = Color.FromArgb(255, 246, 220); border = Color.FromArgb(140, 90, 0); }

                using (var path = Round(b.Bounds, 5))
                using (var br = new SolidBrush(fill))
                using (var pen = new Pen(border, step.Kind == "LBL" || step.Kind == "PROG" ? 1.8f : 1f))
                {
                    e.Graphics.FillPath(br, path);
                    e.Graphics.DrawPath(pen, path);
                }
                string label = step.Display ?? "";
                if (!string.IsNullOrEmpty(step.Flag))
                    label += "  [" + step.Flag + "]";
                var rect = new Rectangle(b.Bounds.X + 8, b.Bounds.Y, b.Bounds.Width - 12, b.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, label, Font, rect, text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private void DrawArrows(Graphics g)
        {
            var lbl = new Dictionary<string, Box>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in _boxes)
            {
                if (b.Step.Kind == "LBL" && !string.IsNullOrEmpty(b.Step.Target))
                    lbl[b.Step.Target] = b;
            }
            using (var pen = new Pen(UiTheme.Accent, 1.4f))
            {
                pen.CustomEndCap = new AdjustableArrowCap(4, 6);
                foreach (var b in _boxes)
                {
                    if (b.Step.Kind != "JMP" && b.Step.Kind != "TIMEOUT") continue;
                    Box dest;
                    if (string.IsNullOrEmpty(b.Step.Target) || !lbl.TryGetValue(b.Step.Target, out dest))
                        continue;
                    int x = Math.Max(b.Bounds.Right, dest.Bounds.Right) + 18;
                    var p1 = new Point(b.Bounds.Right, b.Bounds.Y + b.Bounds.Height / 2);
                    var p2 = new Point(x, p1.Y);
                    var p3 = new Point(x, dest.Bounds.Y + dest.Bounds.Height / 2);
                    var p4 = new Point(dest.Bounds.Right, p3.Y);
                    g.DrawLines(pen, new[] { p1, p2, p3, p4 });
                }
            }
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void OnClick(object sender, MouseEventArgs e)
        {
            var pt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            foreach (var b in _boxes)
            {
                if (!b.Bounds.Contains(pt)) continue;
                if (StepClick != null) StepClick(b.Step);
                return;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBoxes();
            Invalidate();
        }
    }
}
