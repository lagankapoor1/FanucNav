using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FanucNav.Fanuc;
using FanucNav.Forms;
using FanucNav.PluginInfrastructure;

namespace FanucNav
{
    internal static class Main
    {
        internal const string PluginName = "FanucNav";
        internal static int IdShowPanel = 0;
        private static NavPanel _panel;
        private static bool _isShuttingDown;
        private static bool _renumbering;
        private static Timer _renumberTimer;

        internal static void CommandMenuInit()
        {
            PluginBase.SetCommand(0, "FanucNav panel", ShowPanel);
            PluginBase.SetCommand(1, "Go to CALL / LBL definition", GoToDefinition,
                new ShortcutKey(true, true, false, Keys.G));
            PluginBase.SetCommand(2, "Find IO / LBL / CALL usages", FindRefs,
                new ShortcutKey(true, true, false, Keys.R));
            PluginBase.SetCommand(3, "Check macro table", CheckMacros);
            PluginBase.SetCommand(4, "Renumber current program…", Renumber);
            PluginBase.SetCommand(5, "Re-index robot backup", Reindex);
            PluginBase.SetCommand(6, "Float / dock panel", ToggleFloat);
            PluginBase.SetCommand(7, "---", null);
            PluginBase.SetCommand(8, "Hide CR/LF markers", HideEol);
            PluginBase.SetCommand(9, "About FanucNav", ShowAbout);
        }

        private static Bitmap _tbBmp;
        private static IntPtr _tbHbmp;

        internal static void SetToolBarIcons()
        {
            try
            {
                if (_tbBmp == null)
                {
                    _tbBmp = new Bitmap(16, 16);
                    using (var g = Graphics.FromImage(_tbBmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                        g.Clear(Color.FromArgb(168, 108, 8));
                        using (var f = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var b = new SolidBrush(Color.White))
                            g.DrawString("F", f, b, 3, 1);
                    }
                    _tbHbmp = _tbBmp.GetHbitmap();
                }
                var icons = new Win32.ToolbarIconsWithDarkMode();
                icons.hToolbarBmp = _tbHbmp;
                icons.hToolbarIcon = IntPtr.Zero;
                icons.hToolbarIconDarkMode = IntPtr.Zero;
                int cmd = PluginBase._funcItems.Items[0]._cmdID;
                Win32.SendMessage(PluginBase.nppData._nppHandle,
                    (uint)NppMsg.NPPM_ADDTOOLBARICON_FORDARKMODE, (IntPtr)cmd, ref icons);
            }
            catch { }
        }

        internal static void PluginCleanUp()
        {
            _isShuttingDown = true;
            if (_renumberTimer != null)
            {
                _renumberTimer.Stop();
                _renumberTimer.Dispose();
                _renumberTimer = null;
            }
            if (_panel != null && !_panel.IsDisposed)
            {
                try { _panel.ForceClose = true; _panel.Close(); _panel.Dispose(); } catch { }
            }
            _panel = null;
        }

        internal static void OnNotification(ScNotification notification)
        {
            if (_isShuttingDown) return;
            uint code = notification.Header.Code;

            if (code == (uint)NppMsg.NPPN_READY)
            {
                NppEditor.HideEolMarkers();
                NppEditor.ClearEolStatus();
                ShowPanel();
                return;
            }

            if (code == (uint)NppMsg.NPPN_BUFFERACTIVATED)
            {
                string active = NppEditor.GetCurrentPath();
                NppEditor.HideEolMarkers();
                NppEditor.ClearEolStatus();
                if (_panel != null && !_panel.IsDisposed)
                    _panel.OnBufferActivated(active);
                return;
            }

            if (code == (uint)SciMsg.SCN_UPDATEUI)
            {
                NppEditor.ClearEolStatus();
                return;
            }

            if (code == (uint)SciMsg.SCN_DOUBLECLICK)
            {
                string path = NppEditor.GetCurrentPath();
                if (LsParser.LooksLikeLs(path))
                {
                    EnsurePanel();
                    _panel.OnEditorDoubleClick();
                }
                return;
            }

            if (code == (uint)SciMsg.SCN_MODIFIED)
            {
                if (_renumbering) return;
                int mod = notification.modificationType;
                if ((mod & (SciMod.UNDO | SciMod.REDO)) != 0) return;
                if ((mod & (SciMod.INSERTTEXT | SciMod.DELETETEXT)) == 0) return;
                int added = notification.linesAdded.ToInt32();
                if (added == 0) return;
                if (!LsParser.LooksLikeLs(NppEditor.GetCurrentPath())) return;
                ScheduleLiveRenumber();
            }
        }

        internal static void ShowPanel()
        {
            EnsurePanel();
        }

        internal static void EnsurePanel()
        {
            if (_panel == null || _panel.IsDisposed)
            {
                _panel = new NavPanel();
                _panel.EnsureRegistered(IdShowPanel);
                try { _panel.LoadLastFolder(); } catch { }
                try { _panel.OnBufferActivated(NppEditor.GetCurrentPath()); } catch { }
            }
            if (_panel.IsFloating)
            {
                _panel.BringFloatToFront();
                return;
            }
            NppEditor.ShowDock(_panel.Handle);
            try { _panel.Visible = true; } catch { }
        }

        private static void ScheduleLiveRenumber()
        {
            if (_renumberTimer == null)
            {
                _renumberTimer = new Timer();
                _renumberTimer.Interval = 180;
                _renumberTimer.Tick += (s, e) =>
                {
                    _renumberTimer.Stop();
                    AutoRenumberIfNeeded();
                };
            }
            _renumberTimer.Stop();
            _renumberTimer.Start();
        }

        private static void AutoRenumberIfNeeded()
        {
            if (_renumbering || _isShuttingDown) return;
            string path = NppEditor.GetCurrentPath();
            if (!LsParser.LooksLikeLs(path)) return;

            string text;
            try { text = NppEditor.GetText(); }
            catch { return; }
            if (!LsParser.NeedsRenumber(text)) return;

            int pos = NppEditor.GetCurrentPos();
            int line = NppEditor.LineFromPosition(pos);
            int col = NppEditor.GetColumn(pos);

            int dummy;
            string updated = LsParser.RenumberMn(text, out dummy);
            if (updated == text) return;

            _renumbering = true;
            try
            {
                NppEditor.BeginUndo();
                NppEditor.SetText(updated);
                NppEditor.EndUndo();
                NppEditor.HideEolMarkers();

                string newLine = NppEditor.GetLine(line);
                int lineStart = NppEditor.PositionFromLine(line);
                int newCol = col;
                var m = Regex.Match(newLine ?? "", @"^\s*\d+\s*:\s*");
                if (m.Success && col <= 1)
                    newCol = m.Length;
                else if (newCol > (newLine ?? "").Length)
                    newCol = (newLine ?? "").Length;
                NppEditor.SetCaret(lineStart + newCol);
            }
            catch { }
            finally
            {
                _renumbering = false;
            }
        }

        private static void GoToDefinition()
        {
            EnsurePanel();
            _panel.GoToCallUnderCursor();
        }

        private static void FindRefs()
        {
            EnsurePanel();
            _panel.FindRefsUnderCursor();
        }

        private static void CheckMacros()
        {
            EnsurePanel();
            _panel.CheckMacroTable();
        }

        private static void Renumber()
        {
            EnsurePanel();
            _panel.RenumberCurrentProgram();
        }

        private static void Reindex()
        {
            EnsurePanel();
            _panel.ReloadFolder();
        }

        private static void ToggleFloat()
        {
            EnsurePanel();
            _panel.ToggleFloat();
        }

        private static void HideEol()
        {
            NppEditor.HideEolMarkers();
        }

        private static void ShowAbout()
        {
            MessageBox.Show(
                "FanucNav — FANUC robot backup navigator for Notepad++\r\n\r\n" +
                "• Open a robot backup folder (or a .zip)\r\n" +
                "• Cross-reference CALL / RUN programs\r\n" +
                "• Jump labels and I/O\r\n" +
                "• Double-click IO, JMP LBL or CALL to see usages\r\n" +
                "• Auto-renumber TP lines on Enter / Delete\r\n" +
                "• Data table: R / PR / P / UFRAME / UTOOL / PAYLOAD\r\n\r\n" +
                "Shortcuts:\r\n" +
                "  Ctrl+Alt+G   Go to definition\r\n" +
                "  Ctrl+Alt+R   Find usages",
                "About FanucNav",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
