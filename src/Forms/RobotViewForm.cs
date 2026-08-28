using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FanucNav.Fanuc;

namespace FanucNav.Forms
{
    public sealed class RobotViewForm : Form
    {
        private readonly RobotIdent _ident;
        private readonly DcsConfig _dcs;
        private readonly List<CartPose> _path = new List<CartPose>();
        private readonly TrackBar[] _bars = new TrackBar[6];
        private readonly Label[] _barLbl = new Label[6];
        private readonly Panel _xy;
        private readonly Panel _xz;
        private readonly Label _info;
        private readonly ListBox _pathList;
        private double[] _q = new double[6];
        private int _playIndex = -1;
        private Timer _play;

        public RobotViewForm(RobotIdent ident, DcsConfig dcs, IEnumerable<CartPose> path)
        {
            _ident = ident ?? new RobotIdent();
            _dcs = dcs ?? new DcsConfig();
            if (path != null) _path.AddRange(path);

            Text = "FanucNav — DCS / robot view  (" + _ident.Model + ")";
            Width = 980;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 249);
            Font = new Font("Segoe UI", 9F);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            var views = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            views.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            views.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _xy = MakeView("Top  XY");
            _xz = MakeView("Side  XZ");
            _xy.Paint += (s, e) => DrawView(e.Graphics, _xy, true);
            _xz.Paint += (s, e) => DrawView(e.Graphics, _xz, false);
            views.Controls.Add(_xy, 0, 0);
            views.Controls.Add(_xz, 0, 1);

            var side = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3 };
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            var sliders = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 2 };
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
            sliders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                _barLbl[i] = new Label { Text = "J" + (i + 1), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
                _bars[i] = new TrackBar
                {
                    Minimum = (int)_ident.Dh.Jmin[i],
                    Maximum = (int)_ident.Dh.Jmax[i],
                    TickFrequency = 30,
                    Dock = DockStyle.Fill,
                    Value = 0
                };
                _bars[i].Scroll += (s, e) =>
                {
                    _q[idx] = _bars[idx].Value;
                    _barLbl[idx].Text = "J" + (idx + 1) + " " + _q[idx].ToString("0");
                    Redraw();
                };
                sliders.Controls.Add(_barLbl[i], 0, i);
                sliders.Controls.Add(_bars[i], 1, i);
            }

            _pathList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var p in _path) _pathList.Items.Add(p.ToString());
            _pathList.DoubleClick += (s, e) =>
            {
                int i = _pathList.SelectedIndex;
                if (i >= 0) GoToPose(_path[i]);
            };

            var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            btns.Controls.Add(MkBtn("Home", delegate { SetJoints(new double[6]); }));
            btns.Controls.Add(MkBtn("IK to PR/P", delegate { IkSelected(); }));
            btns.Controls.Add(MkBtn("Play path", delegate { Play(); }));

            side.Controls.Add(sliders, 0, 0);
            side.Controls.Add(_pathList, 0, 1);
            side.Controls.Add(btns, 0, 2);

            _info = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            UpdateInfo();

            root.Controls.Add(views, 0, 0);
            root.Controls.Add(side, 1, 0);
            root.SetColumnSpan(_info, 2);
            root.Controls.Add(_info, 0, 1);
            Controls.Add(root);

            _play = new Timer { Interval = 400 };
            _play.Tick += PlayTick;
        }

        private void Play()
        {
            if (_path.Count == 0)
            {
                MessageBox.Show("No taught P[] / PR[] with real numbers in this backup.", "FanucNav");
                return;
            }
            _playIndex = 0;
            _play.Start();
        }

        private void PlayTick(object sender, EventArgs e)
        {
            if (_playIndex < 0 || _playIndex >= _path.Count)
            {
                _play.Stop();
                return;
            }
            _pathList.SelectedIndex = _playIndex;
            GoToPose(_path[_playIndex]);
            _playIndex++;
        }

        private void IkSelected()
        {
            if (_pathList.SelectedIndex < 0)
            {
                if (_path.Count > 0) _pathList.SelectedIndex = 0;
                else return;
            }
            var pose = _path[_pathList.SelectedIndex];
            if (pose.HasCart)
            {
                double[] q;
                if (Kinematics.IkXyz(_ident.Dh, pose.X, pose.Y, pose.Z, _q, out q))
                    SetJoints(q);
                else
                    MessageBox.Show("IK did not converge for " + pose.Name + " (out of reach for this approximate model).", "FanucNav");
                return;
            }
            GoToPose(pose);
        }

        private void GoToPose(CartPose pose)
        {
            if (pose == null) return;
            if (pose.HasJoints) { SetJoints(pose.Joints); return; }
            if (pose.HasCart)
            {
                double[] q;
                if (Kinematics.IkXyz(_ident.Dh, pose.X, pose.Y, pose.Z, _q, out q))
                    SetJoints(q);
            }
        }

        private void SetJoints(double[] q)
        {
            for (int i = 0; i < 6 && i < q.Length; i++)
            {
                int v = (int)Math.Round(q[i]);
                if (v < _bars[i].Minimum) v = _bars[i].Minimum;
                if (v > _bars[i].Maximum) v = _bars[i].Maximum;
                _bars[i].Value = v;
                _q[i] = v;
                _barLbl[i].Text = "J" + (i + 1) + " " + v;
            }
            Redraw();
        }

        private void Redraw()
        {
            UpdateInfo();
            _xy.Invalidate();
            _xz.Invalidate();
        }

        private void UpdateInfo()
        {
            double[] x, y, z;
            Kinematics.JointOrigins(_ident.Dh, _q, out x, out y, out z);
            string hit = "";
            foreach (var zone in _dcs.Zones)
            {
                if (!zone.Enabled || !zone.HasBox) continue;
                if (Inside(x[6], y[6], z[6], zone))
                    hit += "  INSIDE " + zone.Comment;
            }
            _info.Text = _ident + "   DH≈" + _ident.Dh.Name +
                         "   TCP  X=" + x[6].ToString("0") + " Y=" + y[6].ToString("0") + " Z=" + z[6].ToString("0") +
                         " mm" + hit +
                         "    (approx. kinematics — not a certified DCS/RoboGuide check)";
        }

        private static bool Inside(double x, double y, double z, DcsZone zone)
        {
            return Between(x, zone.X1, zone.X2) && Between(y, zone.Y1, zone.Y2) && Between(z, zone.Z1, zone.Z2);
        }

        private static bool Between(double v, double a, double b)
        {
            return v >= Math.Min(a, b) && v <= Math.Max(a, b);
        }

        private void DrawView(Graphics g, Panel panel, bool top)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            float w = panel.ClientSize.Width;
            float h = panel.ClientSize.Height;
            if (w < 20 || h < 20) return;

            double reach = Math.Max(1200, _ident.Dh.ReachMm);
            foreach (var z in _dcs.Zones)
            {
                if (!z.HasBox) continue;
                reach = Math.Max(reach, Math.Abs(z.X1));
                reach = Math.Max(reach, Math.Abs(z.X2));
                reach = Math.Max(reach, Math.Abs(z.Y1));
                reach = Math.Max(reach, Math.Abs(z.Y2));
                reach = Math.Max(reach, Math.Abs(z.Z1));
                reach = Math.Max(reach, Math.Abs(z.Z2));
            }
            double scale = Math.Min(w, h) * 0.42 / reach;
            float cx = w / 2f;
            float cy = h / 2f + (top ? 0 : 10);

            using (var grid = new Pen(Color.FromArgb(230, 230, 232)))
            {
                g.DrawLine(grid, 0, cy, w, cy);
                g.DrawLine(grid, cx, 0, cx, h);
            }

            using (var reachPen = new Pen(Color.FromArgb(210, 210, 220), 1) { DashStyle = DashStyle.Dash })
                g.DrawEllipse(reachPen, cx - (float)(_ident.Dh.ReachMm * scale), cy - (float)(_ident.Dh.ReachMm * scale),
                    (float)(_ident.Dh.ReachMm * scale * 2), (float)(_ident.Dh.ReachMm * scale * 2));

            foreach (var zone in _dcs.Zones)
            {
                if (!zone.HasBox) continue;
                float x1 = Map(zone.X1, true, cx, cy, scale, top);
                float y1 = Map(top ? zone.Y1 : zone.Z1, false, cx, cy, scale, top);
                float x2 = Map(zone.X2, true, cx, cy, scale, top);
                float y2 = Map(top ? zone.Y2 : zone.Z2, false, cx, cy, scale, top);
                var rect = Rect(x1, y1, x2, y2);
                using (var fill = new SolidBrush(zone.Enabled ? Color.FromArgb(30, 200, 60, 40) : Color.FromArgb(20, 120, 120, 120)))
                using (var pen = new Pen(zone.Enabled ? Color.FromArgb(200, 180, 40, 30) : Color.Gray, 2))
                {
                    g.FillRectangle(fill, rect);
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
                if (zone.Enabled)
                    g.DrawString(zone.Comment, Font, Brushes.Maroon, rect.X + 2, rect.Y + 2);
            }

            foreach (var el in _dcs.Elements)
            {
                using (var pen = new Pen(Color.FromArgb(180, 40, 90, 180), 2))
                {
                    float ax = Map(el.X1, true, cx, cy, scale, top);
                    float ay = Map(top ? el.Y1 : el.Z1, false, cx, cy, scale, top);
                    float bx = Map(el.X2, true, cx, cy, scale, top);
                    float by = Map(top ? el.Y2 : el.Z2, false, cx, cy, scale, top);
                    if (el.Shape.IndexOf("Line", StringComparison.OrdinalIgnoreCase) >= 0)
                        g.DrawLine(pen, ax, ay, bx, by);
                    else
                        g.FillEllipse(Brushes.SteelBlue, ax - 3, ay - 3, 6, 6);
                }
            }

            foreach (var p in _path)
            {
                if (!p.HasCart && !p.HasJoints) continue;
                double px, py, pz;
                if (p.HasJoints)
                {
                    double[] xx, yy, zz;
                    Kinematics.JointOrigins(_ident.Dh, p.Joints, out xx, out yy, out zz);
                    px = xx[6]; py = yy[6]; pz = zz[6];
                }
                else { px = p.X; py = p.Y; pz = p.Z; }
                float sx = Map(px, true, cx, cy, scale, top);
                float sy = Map(top ? py : pz, false, cx, cy, scale, top);
                g.FillEllipse(Brushes.DarkOrange, sx - 3, sy - 3, 6, 6);
            }

            double[] jx, jy, jz;
            Kinematics.JointOrigins(_ident.Dh, _q, out jx, out jy, out jz);
            var pts = new PointF[7];
            for (int i = 0; i < 7; i++)
            {
                pts[i] = new PointF(
                    Map(jx[i], true, cx, cy, scale, top),
                    Map(top ? jy[i] : jz[i], false, cx, cy, scale, top));
            }
            using (var arm = new Pen(Color.FromArgb(40, 40, 48), 5) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(arm, pts);
            g.FillEllipse(Brushes.Black, pts[0].X - 6, pts[0].Y - 6, 12, 12);
            g.FillEllipse(Brushes.Gold, pts[6].X - 5, pts[6].Y - 5, 10, 10);

            g.DrawString(top ? "X →    Y ↑" : "X →    Z ↑", Font, Brushes.Gray, 8, 8);
        }

        private static float Map(double v, bool isX, float cx, float cy, double scale, bool top)
        {
            if (isX) return (float)(cx + v * scale);
            return (float)(cy - v * scale);
        }

        private static RectangleF Rect(float x1, float y1, float x2, float y2)
        {
            float l = Math.Min(x1, x2), t = Math.Min(y1, y2);
            return new RectangleF(l, t, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        }

        private static Panel MakeView(string title)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(2) };
            p.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(8, 4), ForeColor = Color.Gray });
            return p;
        }

        private static Button MkBtn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(2) };
            b.Click += click;
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _play != null) _play.Dispose();
            base.Dispose(disposing);
        }
    }
}
