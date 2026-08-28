using System;
using System.Drawing;
using System.Windows.Forms;

namespace FanucNav.Forms
{
    public sealed class RenumberResult
    {
        public bool Ok;
        public bool DoLines;
        public bool DoLabels;
        public int LabelStart = 10;
        public int LabelStep = 10;
        public bool AllFiles;
    }

    public sealed class RenumberDialog : Form
    {
        private readonly CheckBox _lines;
        private readonly CheckBox _labels;
        private readonly NumericUpDown _start;
        private readonly NumericUpDown _step;
        private readonly RadioButton _current;
        private readonly RadioButton _all;
        public RenumberResult Result = new RenumberResult();

        public RenumberDialog()
        {
            Text = "Renumber FANUC program";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 250);
            BackColor = Color.FromArgb(246, 247, 249);
            ForeColor = Color.FromArgb(32, 36, 42);
            Font = new Font("Segoe UI", 9F);

            _lines = MakeCheck("Renumber TP line numbers (1:, 2:, 3: …) and LINE_COUNT", true);
            _lines.Location = new Point(18, 18);
            _lines.Width = 380;

            _labels = MakeCheck("Renumber LBL / JMP LBL / TIMEOUT,LBL consistently", false);
            _labels.Location = new Point(18, 48);
            _labels.Width = 380;

            var startLbl = MakeLabel("Label start");
            startLbl.Location = new Point(36, 84);
            _start = new NumericUpDown { Location = new Point(130, 80), Width = 70, Minimum = 1, Maximum = 32766, Value = 10 };
            var stepLbl = MakeLabel("Step");
            stepLbl.Location = new Point(220, 84);
            _step = new NumericUpDown { Location = new Point(270, 80), Width = 70, Minimum = 1, Maximum = 1000, Value = 10 };

            _current = new RadioButton { Text = "Current program only", Checked = true, AutoSize = true, Location = new Point(18, 126), ForeColor = ForeColor, BackColor = Color.Transparent };
            _all = new RadioButton { Text = "Every .LS in the indexed backup", AutoSize = true, Location = new Point(18, 152), ForeColor = ForeColor, BackColor = Color.Transparent };

            var ok = MakeBtn("Apply", true);
            ok.Location = new Point(210, 200);
            ok.Click += (s, e) =>
            {
                Result.Ok = true;
                Result.DoLines = _lines.Checked;
                Result.DoLabels = _labels.Checked;
                Result.LabelStart = (int)_start.Value;
                Result.LabelStep = (int)_step.Value;
                Result.AllFiles = _all.Checked;
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancel = MakeBtn("Cancel", false);
            cancel.Location = new Point(310, 200);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { _lines, _labels, startLbl, _start, stepLbl, _step, _current, _all, ok, cancel });
        }

        private CheckBox MakeCheck(string text, bool on)
        {
            return new CheckBox
            {
                Text = text,
                Checked = on,
                AutoSize = true,
                ForeColor = ForeColor,
                BackColor = Color.Transparent
            };
        }

        private Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = ForeColor, BackColor = Color.Transparent };
        }

        private Button MakeBtn(string text, bool primary)
        {
            return new Button
            {
                Text = text,
                Width = 88,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(232, 185, 35) : Color.FromArgb(232, 234, 238),
                ForeColor = Color.FromArgb(32, 36, 42),
                DialogResult = DialogResult.None
            };
        }
    }
}
