using System;
using System.Drawing;
using System.Windows.Forms;

namespace FanucNav.Forms
{
    internal static class UiTheme
    {
        public static readonly Color Bg = Color.FromArgb(246, 247, 249);
        public static readonly Color Panel = Color.White;
        public static readonly Color Accent = Color.FromArgb(168, 108, 8);
        public static readonly Color Fg = Color.FromArgb(32, 36, 42);
        public static readonly Color Dim = Color.FromArgb(96, 102, 110);
        public static readonly Color Zebra = Color.FromArgb(248, 246, 242);
        public static readonly Color SelBg = Color.FromArgb(168, 108, 8);
        public static readonly Color SelFg = Color.White;

        public static Color ForeForKind(string kind)
        {
            switch ((kind ?? "").ToUpperInvariant())
            {
                case "DI": case "RI": case "GI": case "UI": case "SI": case "WI": case "AI": return Color.FromArgb(20, 120, 70);
                case "DO": case "RO": case "GO": case "UO": case "SO": case "WO": case "AO": return Color.FromArgb(170, 40, 40);
                case "PNS": case "RSR": return Color.FromArgb(70, 50, 140);
                case "UALM": return Color.FromArgb(160, 20, 20);
                case "MESSAGE": return Color.FromArgb(140, 90, 0);
                case "PR": case "P": return Color.FromArgb(160, 90, 10);
                case "R": case "AR": return Color.FromArgb(30, 80, 150);
                case "UFRAME": case "UTOOL": return Color.FromArgb(10, 110, 130);
                case "PAYLOAD": return Color.FromArgb(80, 50, 140);
                case "F": case "M": return Color.FromArgb(90, 70, 20);
                case "SR": return Color.FromArgb(70, 90, 40);
                default: return Fg;
            }
        }

        public static Color BackForKind(string kind, int row)
        {
            Color tint;
            switch ((kind ?? "").ToUpperInvariant())
            {
                case "DI": case "RI": case "GI": case "UI": case "SI": case "WI": case "AI": tint = Color.FromArgb(232, 248, 236); break;
                case "DO": case "RO": case "GO": case "UO": case "SO": case "WO": case "AO": tint = Color.FromArgb(255, 236, 236); break;
                case "PNS": case "RSR": tint = Color.FromArgb(236, 232, 252); break;
                case "UALM": tint = Color.FromArgb(255, 228, 224); break;
                case "MESSAGE": tint = Color.FromArgb(255, 246, 220); break;
                case "PR": case "P": tint = Color.FromArgb(255, 244, 230); break;
                case "R": tint = Color.FromArgb(232, 240, 252); break;
                case "UFRAME": case "UTOOL": tint = Color.FromArgb(230, 244, 248); break;
                case "PAYLOAD": tint = Color.FromArgb(238, 236, 252); break;
                default: tint = (row % 2 == 0) ? Panel : Zebra; break;
            }
            return tint;
        }

        public static Color ForeForLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return Fg;
            string t = text.ToUpperInvariant();
            if (t.Contains("MISS") || t.StartsWith("MISSING")) return Color.FromArgb(170, 30, 30);
            if (t.Contains("UALM")) return ForeForKind("UALM");
            if (t.Contains("MESSAGE")) return ForeForKind("MESSAGE");
            if (t.Contains("UTOOL") || t.Contains("UFRAME")) return ForeForKind("UTOOL");
            if (t.Contains("PAYLOAD")) return ForeForKind("PAYLOAD");
            if (t.Contains("PR[")) return ForeForKind("PR");
            if (t.Contains("DI[")) return ForeForKind("DI");
            if (t.Contains("DO[")) return ForeForKind("DO");
            if (t.Contains("R[")) return ForeForKind("R");
            if (t.Contains("USED")) return Color.FromArgb(20, 110, 60);
            if (t.Contains("FREE")) return Dim;
            return Fg;
        }

        public static void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var box = (ListBox)sender;
            string text = box.Items[e.Index].ToString();
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Color bg = sel ? SelBg : (e.Index % 2 == 0 ? Panel : Zebra);
            Color fg = sel ? SelFg : ForeForLine(text);
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            var rect = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, box.Font, rect, fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        public static void ColorDataRow(ListViewItem item, string kind, int row)
        {
            if (item == null) return;
            item.BackColor = BackForKind(kind, row);
            item.ForeColor = ForeForKind(kind);
            item.UseItemStyleForSubItems = true;
        }
    }
}
