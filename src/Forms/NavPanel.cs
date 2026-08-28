using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FanucNav.Fanuc;
using FanucNav.PluginInfrastructure;

namespace FanucNav.Forms
{
    public sealed class NavPanel : Form
    {
        private static readonly Color Bg = UiTheme.Bg;
        private static readonly Color PanelBg = UiTheme.Panel;
        private static readonly Color Accent = UiTheme.Accent;
        private static readonly Color Fg = UiTheme.Fg;
        private static readonly Color Dim = UiTheme.Dim;

        private readonly TextBox _folderBox;
        private readonly TextBox _filterBox;
        private readonly TextBox _xrefSearch;
        private readonly TextBox _dataFilter;
        private readonly TreeView _progTree;
        private readonly TreeView _mapTree;
        private readonly ComboBox _mapSelect;
        private readonly FlowCanvas _mapFlow;
        private readonly ListView _selectView;
        private readonly Panel _mapHost;
        private ListView _cmpView;
        private TextBox _cmpFolder;
        private MapStep _lastMap;
        private List<ProgramMap.SelectRow> _lastSelect = new List<ProgramMap.SelectRow>();
        private string _mapMode = "tree";
        private readonly Label _robotHdr;
        private string _lastSymbol;
        private Form _floatHost;
        private bool _dockingBack;
        private readonly ListBox _callList;
        private readonly ListBox _callerList;
        private readonly ListBox _lblList;
        private readonly ListBox _ioList;
        private readonly ListView _dataView;
        private readonly ComboBox _dataKind;
        private readonly TextBox _dataDetail;
        private readonly ListBox _macroList;
        private readonly ListBox _xrefList;
        private readonly Label _status;
        private readonly Label _xrefTitle;
        private readonly Button _backBtn;
        private readonly Button _floatBtn;
        private readonly TabControl _tabs;
        private readonly SplitContainer _mainSplit;
        private readonly SplitContainer _lowerSplit;
        private readonly SplitContainer _dataSplit;
        public bool ForceClose;
        private readonly List<Form> _undocked = new List<Form>();

        private RobotIndex _index = new RobotIndex();
        private readonly List<LsProgram> _visiblePrograms = new List<LsProgram>();
        private readonly List<ProgramCall> _visibleCalls = new List<ProgramCall>();
        private readonly List<ProgramCall> _visibleCallers = new List<ProgramCall>();
        private readonly List<LblReference> _visibleLbls = new List<LblReference>();
        private readonly List<IoReference> _visibleIo = new List<IoReference>();

        private readonly List<object> _visibleMacros = new List<object>();
        private readonly List<CrossRefHit> _visibleXrefs = new List<CrossRefHit>();
        private readonly Stack<string> _history = new Stack<string>();
        private bool _suppress;
        private bool _registered;

        public RobotIndex Index { get { return _index; } }

        public NavPanel()
        {
            Text = "FanucNav";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ControlBox = false;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(280, 280);
            Padding = new Padding(0);

            var title = new Label
            {
                Text = "FANUC NAV",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 11F),
                ForeColor = Accent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _robotHdr = new Label
            {
                Text = "Open a robot backup to see model · software · version",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Dim,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, BackColor = Bg };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            _folderBox = MakeBox();
            _folderBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ReloadFolder(); };
            var browse = MakeButton("Browse");
            browse.Click += Browse_Click;
            var refresh = MakeButton("Index");
            refresh.Click += (s, e) => ReloadFolder();
            var renum = MakeButton("Renumber");
            renum.Click += (s, e) => RenumberCurrentProgram();
            _floatBtn = MakeButton("Float");
            _floatBtn.Click += (s, e) => ToggleFloat();
            folderRow.Controls.Add(_folderBox, 0, 0);
            folderRow.Controls.Add(browse, 1, 0);
            folderRow.Controls.Add(refresh, 2, 0);
            folderRow.Controls.Add(renum, 3, 0);
            folderRow.Controls.Add(_floatBtn, 4, 0);

            var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Bg };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            _filterBox = MakeBox();
            _filterBox.ForeColor = Dim;
            _filterBox.Text = "Filter programs…";
            _filterBox.GotFocus += (s, e) =>
            {
                if (_filterBox.Text == "Filter programs…") { _filterBox.Text = ""; _filterBox.ForeColor = Fg; }
            };
            _filterBox.TextChanged += (s, e) =>
            {
                if (_filterBox.Focused) ApplyFilter();
            };
            _backBtn = MakeButton("Back");
            _backBtn.Enabled = false;
            _backBtn.Click += (s, e) => GoBack();
            filterRow.Controls.Add(_filterBox, 0, 0);
            filterRow.Controls.Add(_backBtn, 1, 0);

            _progTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                HideSelection = false,
                FullRowSelect = true,
                ItemHeight = 20,
                Indent = 16,
                Font = new Font("Segoe UI", 9F)
            };
            _progTree.AfterSelect += ProgTree_AfterSelect;
            _progTree.NodeMouseDoubleClick += (s, e) => OpenSelectedProgram();

            _tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons };
            _callList = MakeList();
            _callerList = MakeList();
            _lblList = MakeList();
            _ioList = MakeList();
            _dataKind = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = PanelBg,
                ForeColor = Fg,
                FlatStyle = FlatStyle.Flat
            };
            _dataKind.Items.AddRange(new object[]
            {
                "All types", "R  numeric", "PR  position", "P  program pos",
                "UFRAME", "UTOOL", "PAYLOAD", "SR  string",
                "F  flags", "M  markers",
                "DI", "DO", "GI", "GO", "RI", "RO", "UI", "UO",
                "SI", "SO", "WI", "WO", "AI", "AO",
                "PNS", "RSR",
                "UALM", "MESSAGE", "TIMER", "AR", "VR"
            });
            _dataKind.SelectedIndex = 0;
            _dataKind.SelectedIndexChanged += (s, e) => FillDataTable();

            _dataFilter = MakeBox();
            _dataFilter.ForeColor = Dim;
            _dataFilter.Text = "Filter data…";
            _dataFilter.GotFocus += (s, e) =>
            {
                if (_dataFilter.Text == "Filter data…") { _dataFilter.Text = ""; _dataFilter.ForeColor = Fg; }
            };
            _dataFilter.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_dataFilter.Text))
                {
                    _dataFilter.Text = "Filter data…";
                    _dataFilter.ForeColor = Dim;
                }
            };
            _dataFilter.TextChanged += (s, e) =>
            {
                if (_dataFilter.Focused) FillDataTable();
            };

            _dataView = new ListView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                ShowItemToolTips = true,
                HeaderStyle = ColumnHeaderStyle.Clickable,
                Scrollable = true
            };
            _dataView.Columns.Add("Type", 56);
            _dataView.Columns.Add("Symbol", 90);
            _dataView.Columns.Add("Name", 140);
            _dataView.Columns.Add("Value", 140);
            _dataView.Columns.Add("Uses", 48);
            _dataView.SelectedIndexChanged += (s, e) => ShowSelectedDataDetail();
            _dataView.DoubleClick += (s, e) => OpenSelectedData();

            _dataDetail = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(255, 252, 240),
                ForeColor = Fg,
                Font = new Font("Consolas", 9F),
                Text = "Select R, PR, F… then click a row to see its value."
            };

            var dataPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bg };
            dataPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            dataPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var kindRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = Bg };
            kindRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            kindRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            kindRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
            kindRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            kindRow.Controls.Add(new Label
            {
                Text = "Show",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg
            }, 0, 0);
            kindRow.Controls.Add(_dataKind, 1, 0);
            kindRow.Controls.Add(new Label
            {
                Text = "Find",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg,
                Padding = new Padding(6, 0, 0, 0)
            }, 2, 0);
            kindRow.Controls.Add(_dataFilter, 3, 0);
            _dataSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(210, 214, 220),
                Panel1MinSize = 40,
                Panel2MinSize = 36
            };
            _dataSplit.Panel1.Controls.Add(_dataView);
            _dataSplit.Panel2.Controls.Add(_dataDetail);
            dataPanel.Controls.Add(kindRow, 0, 0);
            dataPanel.Controls.Add(_dataSplit, 0, 1);

            _macroList = MakeList();
            _callList.DoubleClick += (s, e) => OpenSelectedCall();
            _callerList.DoubleClick += (s, e) => OpenSelectedCaller();
            _lblList.DoubleClick += (s, e) => OpenSelectedLabel();
            _ioList.DoubleClick += (s, e) => OpenSelectedIo();
            _macroList.DoubleClick += (s, e) => OpenSelectedMacro();

            _mapSelect = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = PanelBg,
                ForeColor = Fg,
                FlatStyle = FlatStyle.Flat
            };
            _mapSelect.SelectedIndexChanged += (s, e) => FillProgramMap();
            _mapTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                ShowLines = true,
                ShowPlusMinus = true,
                HideSelection = false,
                FullRowSelect = true,
                ItemHeight = 20,
                Indent = 18,
                Font = new Font("Consolas", 9F)
            };
            _mapTree.NodeMouseDoubleClick += (s, e) => OpenMapNode(e.Node);
            _mapFlow = new FlowCanvas { Dock = DockStyle.Fill, Visible = false };
            _mapFlow.StepClick += step =>
            {
                var n = new TreeNode();
                n.Tag = step;
                OpenMapNode(n);
            };
            _selectView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                Visible = false,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            _selectView.Columns.Add("Signal", 140);
            _selectView.Columns.Add("Op", 40);
            _selectView.Columns.Add("Value", 70);
            _selectView.Columns.Add("Action", 180);
            _selectView.DoubleClick += (s, e) =>
            {
                if (_selectView.SelectedItems.Count == 0) return;
                var row = _selectView.SelectedItems[0].Tag as ProgramMap.SelectRow;
                if (row == null) return;
                if (!string.IsNullOrEmpty(row.FilePath) && row.LineNo > 0)
                    OpenPath(row.FilePath, row.LineNo, true);
                if (!string.IsNullOrEmpty(row.Target) && row.Target.StartsWith("LBL", StringComparison.OrdinalIgnoreCase))
                    ShowCrossRefs(row.Target, false);
                else if (!string.IsNullOrEmpty(row.Target))
                {
                    ShowCrossRefs(row.Target, false);
                    OpenProgramByName(row.Target, true);
                }
            };
            _mapHost = new Panel { Dock = DockStyle.Fill, BackColor = PanelBg };
            _mapHost.Controls.Add(_mapTree);
            _mapHost.Controls.Add(_mapFlow);
            _mapHost.Controls.Add(_selectView);

            var mapPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bg };
            mapPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            mapPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var mapRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, BackColor = Bg };
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            mapRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            mapRow.Controls.Add(new Label
            {
                Text = "Show",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg
            }, 0, 0);
            mapRow.Controls.Add(_mapSelect, 1, 0);
            var treeBtn = MakeButton("Tree");
            treeBtn.Click += (s, e) => SetMapMode("tree");
            var flowBtn = MakeButton("Flow");
            flowBtn.Click += (s, e) => SetMapMode("flow");
            var tblBtn = MakeButton("Table");
            tblBtn.Click += (s, e) => SetMapMode("table");
            var expBtn = MakeButton("Export");
            expBtn.Click += (s, e) => ExportMap();
            mapRow.Controls.Add(treeBtn, 2, 0);
            mapRow.Controls.Add(flowBtn, 3, 0);
            mapRow.Controls.Add(tblBtn, 4, 0);
            mapRow.Controls.Add(expBtn, 5, 0);
            mapPanel.Controls.Add(mapRow, 0, 0);
            mapPanel.Controls.Add(_mapHost, 0, 1);

            var cmpPanel = BuildComparePanel();

            _tabs.TabPages.Add(WrapTab("Data", dataPanel));
            _tabs.TabPages.Add(WrapTab("Map", mapPanel));
            _tabs.TabPages.Add(WrapTab("Compare", cmpPanel));
            _tabs.TabPages.Add(WrapTab("Calls", _callList));
            _tabs.TabPages.Add(WrapTab("Callers", _callerList));
            _tabs.TabPages.Add(WrapTab("Labels", _lblList));
            _tabs.TabPages.Add(WrapTab("I/O", _ioList));
            _tabs.TabPages.Add(WrapTab("Macros", _macroList));
            _tabs.MouseUp += Tabs_MouseUp;
            _tabs.MouseDoubleClick += (s, e) => UndockSelectedTab();

            var xrefPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bg };
            xrefPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            xrefPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _xrefTitle = new Label
            {
                Text = "Usages  —  double-click IO / JMP LBL / CALL in the editor",
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Accent,
                Font = new Font("Segoe UI Semibold", 8.5F)
            };
            _xrefSearch = MakeBox();
            _xrefSearch.Dock = DockStyle.Bottom;
            _xrefSearch.Height = 24;
            _xrefSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ShowCrossRefs(_xrefSearch.Text.Trim(), false);
                    e.Handled = true;
                }
            };
            var xrefHead = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            xrefHead.Controls.Add(_xrefSearch);
            xrefHead.Controls.Add(_xrefTitle);
            _xrefList = MakeList();
            _xrefList.DoubleClick += (s, e) => OpenSelectedXref();
            xrefPanel.Controls.Add(xrefHead, 0, 0);
            xrefPanel.Controls.Add(_xrefList, 0, 1);

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                ForeColor = Dim,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                Text = "Open a backup  ·  double-click a symbol to highlight every use  ·  drag splitters to resize"
            };

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 108,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Bg,
                Padding = new Padding(8, 6, 8, 2)
            };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            header.Controls.Add(title, 0, 0);
            header.Controls.Add(_robotHdr, 0, 1);
            header.Controls.Add(folderRow, 0, 2);
            header.Controls.Add(filterRow, 0, 3);

            var progHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 0), BackColor = Bg };
            var progLbl = new Label
            {
                Text = "Call tree  —  unused and missing CALLs at the bottom",
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Accent,
                Font = new Font("Segoe UI Semibold", 8.5F)
            };
            progHost.Controls.Add(_progTree);
            progHost.Controls.Add(progLbl);

            _lowerSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(210, 214, 220),
                Panel1MinSize = 80,
                Panel2MinSize = 70
            };
            _lowerSplit.Panel1.Padding = new Padding(8, 4, 8, 4);
            _lowerSplit.Panel2.Padding = new Padding(8, 4, 8, 4);
            _lowerSplit.Panel1.Controls.Add(_tabs);
            _lowerSplit.Panel2.Controls.Add(xrefPanel);

            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(210, 214, 220),
                Panel1MinSize = 60,
                Panel2MinSize = 120
            };
            _mainSplit.Panel1.Controls.Add(progHost);
            _mainSplit.Panel2.Controls.Add(_lowerSplit);

            Controls.Add(_mainSplit);
            Controls.Add(_status);
            Controls.Add(header);

            StyleTabs();
            Shown += (s, e) => InitSplitters();
        }

        private void InitSplitters()
        {
            try
            {
                if (_mainSplit.Height > 180)
                    _mainSplit.SplitterDistance = Math.Max(80, _mainSplit.Height * 28 / 100);
                if (_lowerSplit.Height > 160)
                    _lowerSplit.SplitterDistance = Math.Max(90, _lowerSplit.Height * 55 / 100);
                if (_dataSplit != null && _dataSplit.Height > 80)
                    _dataSplit.SplitterDistance = Math.Max(50, _dataSplit.Height * 58 / 100);
            }
            catch { }
        }

        public void EnsureRegistered(int dlgId)
        {
            if (_registered) return;
            NppEditor.RegisterModeless(Handle);
            var data = new NppTbData
            {
                hClient = Handle,
                pszName = "FanucNav",
                dlgID = dlgId,
                uMask = NppTbMsg.DWS_DF_CONT_RIGHT | NppTbMsg.DWS_ICONTAB | NppTbMsg.DWS_ICONBAR,
                pszModuleName = "FanucNav.dll"
            };
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(
                System.Runtime.InteropServices.Marshal.SizeOf(data));
            System.Runtime.InteropServices.Marshal.StructureToPtr(data, ptr, false);
            Win32.SendMessage(PluginBase.nppData._nppHandle, (uint)NppMsg.NPPM_DMMREGASDCKDLG, IntPtr.Zero, ptr);
            _registered = true;
        }

        public void OnBufferActivated(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!LooksLikeFanuc(path)) return;
            EnsureIndexForCurrentFile(path);
            SelectProgramByPath(path, true);
            ReapplyHighlight();
        }

        public void OnEditorDoubleClick()
        {
            try
            {
                int pos = NppEditor.GetCurrentPos();
                int line = NppEditor.LineFromPosition(pos);
                int col = NppEditor.GetColumn(pos);
                string text = NppEditor.GetLine(line);
                CursorSymbol sym;
                if (!LsParser.TryResolveAtPosition(text, col, _index.Macros, out sym) || sym == null)
                {
                    _status.Text = "No symbol under the cursor (IO / R / PR / LBL / CALL / macro).";
                    return;
                }

                JumpResolved(sym);
                HighlightSymbol(sym.Symbol);
            }
            catch (Exception ex)
            {
                _status.Text = "Lookup failed: " + ex.Message;
            }
        }

        public void HighlightSymbol(string symbol)
        {
            _lastSymbol = symbol;
            NppEditor.HighlightSymbol(symbol);
        }

        public void ReapplyHighlight()
        {
            if (!string.IsNullOrEmpty(_lastSymbol) && LsParser.LooksLikeLs(NppEditor.GetCurrentPath()))
                NppEditor.HighlightSymbol(_lastSymbol);
        }

        private void JumpResolved(CursorSymbol sym)
        {
            if (sym.Kind == "MACRO")
            {
                var mac = _index.FindMacroBySymbol(sym.Symbol)
                       ?? _index.FindMacroBySymbol(sym.Display);
                if (mac == null)
                {
                    foreach (var m in _index.Macros)
                    {
                        if (string.Equals(m.KeyProg, sym.Symbol, StringComparison.OrdinalIgnoreCase) ||
                            (sym.Display != null && sym.Display.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0))
                        { mac = m; break; }
                    }
                }
                if (mac != null)
                {
                    OpenProgramByName(mac.ProgName, true);
                    ShowMacroUses(mac);
                    _status.Text = "Macro " + mac.Name + " → " + mac.ProgName;
                    return;
                }
            }

            if (sym.Kind == "CALL")
                OpenProgramByName(sym.Symbol, true);

            string selProg = ProgramMap.ProgramForSelector(sym.Symbol);
            if (!string.IsNullOrEmpty(selProg))
            {
                OpenProgramByName(selProg, true);
                ShowCrossRefs(sym.Symbol, false);
                HighlightSymbol(sym.Symbol);
                _status.Text = (sym.Display ?? sym.Symbol) + "  →  " + selProg;
                return;
            }

            if (sym.Kind == "LBL")
            {
                string path = NppEditor.GetCurrentPath();
                var hits = _index.FindRefs(sym.Symbol, path);
                var def = hits.FirstOrDefault(h => h.Kind == "LBL") ?? hits.FirstOrDefault();
                if (def != null) OpenPath(def.FilePath, def.LineNo, true);
                ShowCrossRefs(sym.Symbol, true);
                HighlightSymbol(sym.Symbol);
                _status.Text = "Usages of " + (sym.Display ?? sym.Symbol);
                return;
            }

            ShowCrossRefs(sym.Symbol, false);
            RevealDataSymbol(sym.Symbol);
            HighlightSymbol(sym.Symbol);
            _status.Text = "Usages of " + (sym.Display ?? sym.Symbol);
        }

        private void RevealDataSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || _dataKind == null) return;
            string kind = symbol;
            int br = kind.IndexOf('[');
            if (br > 0) kind = kind.Substring(0, br);
            kind = kind.ToUpperInvariant();

            string current = SelectedDataKind();
            if (current.Length > 0 && !string.Equals(current, kind, StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < _dataKind.Items.Count; i++)
                {
                    string s = _dataKind.Items[i].ToString();
                    if (s.StartsWith(kind + " ", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s, kind, StringComparison.OrdinalIgnoreCase))
                    {
                        _dataKind.SelectedIndex = i;
                        break;
                    }
                }
            }

            var prog = SelectedProgram();
            string want = symbol;
            if (prog != null)
            {
                var spec = _index.FindRegister(symbol, prog.Name);
                if (spec != null) want = spec.Key;
            }

            SelectTabByName("Data");
            string wantNorm = _index.NormalizeSymbolKey(want);
            string symNorm = _index.NormalizeSymbolKey(symbol);
            foreach (ListViewItem it in _dataView.Items)
            {
                string tag = it.Tag as string;
                string tagNorm = _index.NormalizeSymbolKey(tag);
                if (string.Equals(tagNorm, wantNorm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tagNorm, symNorm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(DataBaseKey(tag), symbol, StringComparison.OrdinalIgnoreCase))
                {
                    it.Selected = true;
                    it.EnsureVisible();
                    break;
                }
            }
            ShowSelectedDataDetail();
        }

        public void FindRefsUnderCursor()
        {
            OnEditorDoubleClick();
        }

        public void GoToCallUnderCursor()
        {
            try
            {
                int pos = NppEditor.GetCurrentPos();
                int line = NppEditor.LineFromPosition(pos);
                int col = NppEditor.GetColumn(pos);
                string text = NppEditor.GetLine(line);
                CursorSymbol sym;
                if (!LsParser.TryResolveAtPosition(text, col, _index.Macros, out sym) || sym == null)
                {
                    _status.Text = "Nothing to jump to under the cursor.";
                    return;
                }
                JumpResolved(sym);
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
            }
        }

        public void RenumberCurrentProgram()
        {
            using (var dlg = new RenumberDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.Result.Ok) return;
                var opt = dlg.Result;
                if (!opt.DoLines && !opt.DoLabels) return;

                if (opt.AllFiles)
                {
                    int files = 0;
                    foreach (var prog in _index.Files.ToList())
                    {
                        if (string.IsNullOrEmpty(prog.Path) || !File.Exists(prog.Path)) continue;
                        if (ApplyRenumberToFile(prog.Path, opt)) files++;
                    }
                    ReloadFolder();
                    _status.Text = "Renumbered " + files + " program file(s).";
                    return;
                }

                string path = NppEditor.GetCurrentPath();
                if (string.IsNullOrEmpty(path) || !LooksLikeFanuc(path))
                {
                    MessageBox.Show("Open a .LS program first.", "FanucNav", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string text = NppEditor.GetText();
                string updated = TransformText(text, opt);
                if (updated == text)
                {
                    _status.Text = "Nothing to renumber.";
                    return;
                }
                NppEditor.BeginUndo();
                NppEditor.SetText(updated);
                NppEditor.EndUndo();
                EnsureIndexForCurrentFile(path);
                SelectProgramByPath(path, true);
                _status.Text = "Renumbered current program.";
            }
        }

        public void ReloadFolder()
        {
            string folder = (_folderBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(folder))
            {
                _status.Text = "Pick a robot backup folder first.";
                return;
            }
            try
            {
                _index = RobotIndex.Build(folder);
                SaveLastFolder(folder);
                ApplyFilter();
                FillMacroTab(null);
                FillDataTable();
                FillMapCombo();
                int missing = _index.MissingCallTargets().Count;
                int unused = _index.UnusedPrograms().Count;
                _robotHdr.Text = _index.Ident != null ? _index.Ident.Header : "FANUC robot backup";
                _status.Text = _index.Files.Count + " programs  ·  " +
                               _index.Registers.Count + " values  ·  " +
                               unused + " unused  ·  " +
                               missing + " missing CALL" + (missing == 1 ? "" : "s");
            }
            catch (Exception ex)
            {
                _status.Text = "Index failed: " + ex.Message;
            }
        }

        private void EnsureIndexForCurrentFile(string path)
        {
            string folder = RobotIndex.GuessRobotFolder(path);
            if (string.IsNullOrEmpty(folder)) return;
            if (!string.Equals(_index.Root, folder, StringComparison.OrdinalIgnoreCase) || _index.Files.Count == 0)
            {
                _folderBox.Text = folder;
                ReloadFolder();
            }
        }

        private void ApplyFilter()
        {
            string q = (_filterBox.Text ?? "").Trim();
            if (!_filterBox.Focused && q == "Filter programs…") q = "";
            if (q == "Filter programs…") q = "";
            _visiblePrograms.Clear();
            foreach (var p in _index.Files.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (q.Length == 0 ||
                    (p.Name != null && p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Comment != null && p.Comment.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                    _visiblePrograms.Add(p);
            }
            BuildProgramTree(q);
        }

        private void BuildProgramTree(string q)
        {
            if (_progTree == null) return;
            _suppress = true;
            _progTree.BeginUpdate();
            _progTree.Nodes.Clear();
            var groupFont = new Font(_progTree.Font, FontStyle.Bold);

            if (q.Length > 0)
            {
                var matches = _progTree.Nodes.Add("Matches (" + _visiblePrograms.Count + ")");
                matches.NodeFont = groupFont;
                matches.ForeColor = Dim;
                foreach (var p in _visiblePrograms)
                    matches.Nodes.Add(MakeProgNode(p));
                matches.Expand();
            }
            else
            {
                var entries = _index.EntryPrograms();
                var unused = _index.UnusedPrograms();
                var missing = _index.MissingCallTargets();
                var tree = _progTree.Nodes.Add("Call tree  (" + entries.Count + ")");
                tree.NodeFont = groupFont;
                tree.ForeColor = Accent;
                foreach (var p in entries)
                    AddCallNode(tree, p, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
                tree.Expand();

                if (unused.Count > 0)
                {
                    var u = _progTree.Nodes.Add("Unused  (" + unused.Count + ")");
                    u.NodeFont = groupFont;
                    u.ForeColor = Dim;
                    foreach (var p in unused)
                        u.Nodes.Add(MakeProgNode(p, true));
                }

                if (missing.Count > 0)
                {
                    var m = _progTree.Nodes.Add("Missing CALL  (" + missing.Count + ")");
                    m.NodeFont = groupFont;
                    m.ForeColor = Color.FromArgb(170, 30, 30);
                    foreach (string name in missing)
                    {
                        var n = m.Nodes.Add(name + "  (no .LS in backup)");
                        n.Tag = "MISS:" + name;
                        n.ForeColor = Color.FromArgb(170, 30, 30);
                    }
                }

                int macrosMiss = 0;
                foreach (var mac in _index.Macros)
                {
                    if (string.IsNullOrEmpty(mac.KeyProg)) continue;
                    if (_index.Resolve(mac.ProgName) != null) continue;
                    if (macrosMiss == 0)
                    {
                        var mm = _progTree.Nodes.Add("Missing macros");
                        mm.NodeFont = groupFont;
                        mm.ForeColor = Color.FromArgb(170, 30, 30);
                        mm.Name = "macmiss";
                    }
                    var host = _progTree.Nodes["macmiss"] ?? _progTree.Nodes[_progTree.Nodes.Count - 1];
                    var n = host.Nodes.Add(mac.Name + "  →  " + mac.ProgName);
                    n.Tag = "MISS:" + mac.KeyProg;
                    n.ForeColor = Color.FromArgb(170, 30, 30);
                    macrosMiss++;
                }
            }

            _progTree.EndUpdate();
            _suppress = false;
        }

        private void AddCallNode(TreeNode parent, LsProgram prog, HashSet<string> stack, int depth)
        {
            if (prog == null || depth > 12) return;
            var node = MakeProgNode(prog);
            parent.Nodes.Add(node);
            if (!stack.Add(prog.Name))
            {
                node.Text += "  ↺";
                return;
            }
            var seenChild = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in prog.Calls)
            {
                if (string.IsNullOrEmpty(c.Program) || !seenChild.Add(c.Program)) continue;
                var dest = _index.Resolve(c.Program);
                if (dest == null)
                {
                    var miss = node.Nodes.Add(c.Program + "  (missing)");
                    miss.Tag = "MISS:" + c.Program;
                    miss.ForeColor = Color.FromArgb(170, 30, 30);
                }
                else
                    AddCallNode(node, dest, new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase), depth + 1);
            }
        }

        private TreeNode MakeProgNode(LsProgram p, bool unused = false)
        {
            string text = p.Name ?? "";
            if (p.IsMacro) text += "  [M]";
            if (!string.IsNullOrEmpty(p.Comment)) text += "  —  " + p.Comment;
            var n = new TreeNode(text);
            n.Tag = p;
            if (unused) n.ForeColor = Dim;
            else if (p.IsMacro) n.ForeColor = Accent;
            return n;
        }

        private void ProgTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_suppress) return;
            var prog = SelectedProgram();
            if (prog != null)
            {
                FillDetails(prog);
                return;
            }
            string miss = SelectedMissingName();
            if (!string.IsNullOrEmpty(miss))
            {
                FillDetails(null);
                ShowCrossRefs(miss, false);
                _status.Text = "CALL " + miss + "  —  program file is not in this backup";
            }
        }

        private void FillDetails(LsProgram prog)
        {
            _visibleCalls.Clear();
            _visibleCallers.Clear();
            _visibleLbls.Clear();
            _visibleIo.Clear();
            _callList.Items.Clear();
            _callerList.Items.Clear();
            _lblList.Items.Clear();
            _ioList.Items.Clear();
            if (prog == null) return;

            foreach (var c in prog.Calls)
            {
                _visibleCalls.Add(c);
                _callList.Items.Add(c.Display);
            }
            foreach (var c in _index.CallersOf(prog.Name))
            {
                _visibleCallers.Add(c);
                _callerList.Items.Add(c.Display + "  →  " + prog.Name);
            }
            foreach (var l in prog.LblRefs)
            {
                _visibleLbls.Add(l);
                _lblList.Items.Add(l.Display);
            }
            foreach (var io in prog.IoRefs)
            {
                _visibleIo.Add(io);
                _ioList.Items.Add(io.Display);
            }
            foreach (var io in prog.DataRefs)
            {
                if (!string.Equals(io.Kind, "MESSAGE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(io.Kind, "UALM", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(io.Kind, "UFRAME", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(io.Kind, "UTOOL", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(io.Kind, "PAYLOAD", StringComparison.OrdinalIgnoreCase))
                    continue;
                _visibleIo.Add(io);
                _ioList.Items.Add(io.Display);
            }
            FillMacroTab(prog);
            FitList(_callList);
            FitList(_callerList);
            FitList(_lblList);
            FitList(_ioList);
            if (_mapSelect != null && _mapSelect.SelectedIndex <= 0)
                FillProgramMap();
        }

        private void FillMapCombo()
        {
            if (_mapSelect == null) return;
            object keep = _mapSelect.SelectedItem;
            _mapSelect.BeginUpdate();
            _mapSelect.Items.Clear();
            _mapSelect.Items.Add("This program");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _index.SelectorPrograms())
            {
                if (p == null || !seen.Add(p.Name)) continue;
                _mapSelect.Items.Add(p);
            }
            foreach (var p in _index.Files)
            {
                if (p == null || string.IsNullOrEmpty(p.Name) || !seen.Add(p.Name)) continue;
                if (_index.CallersOf(p.Name).Count == 0 && p.Calls.Count > 0)
                    _mapSelect.Items.Add(p);
            }
            _mapSelect.SelectedIndex = 0;
            if (keep is LsProgram)
            {
                for (int i = 1; i < _mapSelect.Items.Count; i++)
                {
                    var p = _mapSelect.Items[i] as LsProgram;
                    if (p != null && string.Equals(p.Name, ((LsProgram)keep).Name, StringComparison.OrdinalIgnoreCase))
                    {
                        _mapSelect.SelectedIndex = i;
                        break;
                    }
                }
            }
            _mapSelect.EndUpdate();
            FillProgramMap();
        }

        private void FillProgramMap()
        {
            if (_mapTree == null) return;
            LsProgram prog = null;
            if (_mapSelect != null && _mapSelect.SelectedItem is LsProgram)
                prog = (LsProgram)_mapSelect.SelectedItem;
            if (prog == null) prog = SelectedProgram();

            _mapTree.BeginUpdate();
            _mapTree.Nodes.Clear();
            if (prog == null)
            {
                _lastMap = null;
                _lastSelect = new List<ProgramMap.SelectRow>();
                _mapTree.Nodes.Add("(select a PNS / STYLE / RSR / program)");
                _mapTree.EndUpdate();
                if (_mapFlow != null) _mapFlow.SetRoot(null);
                FillSelectTable();
                return;
            }

            var root = ProgramMap.Build(prog, null, 1, _index);
            _lastMap = root;
            _lastSelect = ProgramMap.ExtractSelectTable(prog, null);
            var top = _mapTree.Nodes.Add(MapLabel(root));
            top.Tag = root;
            top.ForeColor = Accent;
            top.NodeFont = new Font(_mapTree.Font, FontStyle.Bold);
            AddMapNodes(top, root);
            top.Expand();
            foreach (TreeNode n in top.Nodes)
                n.Expand();
            _mapTree.EndUpdate();

            if (_mapFlow != null) _mapFlow.SetRoot(root);
            FillSelectTable();
        }

        private void FillSelectTable()
        {
            if (_selectView == null) return;
            _selectView.BeginUpdate();
            _selectView.Items.Clear();
            foreach (var r in _lastSelect)
            {
                var it = new ListViewItem(r.Signal);
                it.SubItems.Add(r.Op);
                it.SubItems.Add(r.Value);
                it.SubItems.Add(r.Action);
                it.Tag = r;
                it.ForeColor = r.Action != null && r.Action.StartsWith("JMP", StringComparison.OrdinalIgnoreCase)
                    ? UiTheme.Accent : Color.FromArgb(20, 110, 70);
                _selectView.Items.Add(it);
            }
            _selectView.EndUpdate();
            if (_selectView.Items.Count > 0)
            {
                try { _selectView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize); } catch { }
            }
        }

        private void SetMapMode(string mode)
        {
            _mapMode = mode ?? "tree";
            if (_mapTree != null)
            {
                _mapTree.Visible = _mapMode == "tree";
                if (_mapTree.Visible) _mapTree.BringToFront();
            }
            if (_mapFlow != null)
            {
                _mapFlow.Visible = _mapMode == "flow";
                if (_mapFlow.Visible)
                {
                    _mapFlow.BringToFront();
                    if (_lastMap != null) _mapFlow.SetRoot(_lastMap);
                }
            }
            if (_selectView != null)
            {
                _selectView.Visible = _mapMode == "table";
                if (_selectView.Visible) _selectView.BringToFront();
            }
        }

        private void ExportMap()
        {
            if (_lastMap == null)
            {
                _status.Text = "Nothing to export — pick a program on the Map tab first.";
                return;
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Export program map";
                dlg.Filter = "Text (*.txt)|*.txt|CSV select table (*.csv)|*.csv";
                dlg.FileName = (_lastMap.Text ?? "map") + "-map";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.FileName;
                string body = path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    ? ProgramMap.ToCsv(_lastSelect)
                    : ProgramMap.ToText(_lastMap, _lastSelect);
                System.IO.File.WriteAllText(path, body);
                _status.Text = "Exported " + path;
            }
        }

        private Control BuildComparePanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Bg };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = Bg };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            row.Controls.Add(new Label
            {
                Text = "Other",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Fg
            }, 0, 0);
            _cmpFolder = MakeBox();
            row.Controls.Add(_cmpFolder, 1, 0);
            var br = MakeButton("Browse");
            br.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Select the other robot backup to compare";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _cmpFolder.Text = dlg.SelectedPath;
                        RunCompare();
                    }
                }
            };
            var go = MakeButton("Compare");
            go.Click += (s, e) => RunCompare();
            row.Controls.Add(br, 2, 0);
            row.Controls.Add(go, 3, 0);
            _cmpView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                ShowItemToolTips = true
            };
            _cmpView.Columns.Add("Status", 70);
            _cmpView.Columns.Add("Type", 80);
            _cmpView.Columns.Add("Name", 140);
            _cmpView.Columns.Add("This backup", 160);
            _cmpView.Columns.Add("Other backup", 160);
            _cmpView.DoubleClick += (s, e) =>
            {
                if (_cmpView.SelectedItems.Count == 0) return;
                string name = _cmpView.SelectedItems[0].SubItems[2].Text;
                OpenProgramByName(name, true);
            };
            panel.Controls.Add(row, 0, 0);
            panel.Controls.Add(_cmpView, 0, 1);
            return panel;
        }

        private void RunCompare()
        {
            string other = (_cmpFolder != null ? _cmpFolder.Text : "") ?? "";
            other = other.Trim();
            if (string.IsNullOrEmpty(other) || !System.IO.Directory.Exists(other))
            {
                _status.Text = "Pick the other backup folder, then Compare.";
                return;
            }
            if (_index == null || _index.Files.Count == 0)
            {
                _status.Text = "Index this backup first.";
                return;
            }
            RobotIndex right;
            try { right = RobotIndex.Build(other); }
            catch (Exception ex)
            {
                _status.Text = "Could not index other backup: " + ex.Message;
                return;
            }
            var diffs = BackupCompare.Compare(_index, right);
            _cmpView.BeginUpdate();
            _cmpView.Items.Clear();
            foreach (var d in diffs)
            {
                var it = new ListViewItem(d.Status);
                it.SubItems.Add(d.Kind);
                it.SubItems.Add(d.Name);
                it.SubItems.Add(d.Left);
                it.SubItems.Add(d.Right);
                it.ToolTipText = d.Left + "  vs  " + d.Right;
                if (d.Status == "added") it.ForeColor = Color.FromArgb(20, 110, 70);
                else if (d.Status == "removed") it.ForeColor = Color.FromArgb(170, 30, 30);
                else it.ForeColor = UiTheme.Accent;
                _cmpView.Items.Add(it);
            }
            _cmpView.EndUpdate();
            _status.Text = diffs.Count + " difference(s) vs " + other;
        }

        private void AddMapNodes(TreeNode parent, MapStep step)
        {
            if (step == null) return;
            foreach (var child in step.Children)
            {
                var n = parent.Nodes.Add(MapLabel(child));
                n.Tag = child;
                n.ForeColor = child.Flag == "MISSING" || child.Kind == "MISS"
                    ? Color.FromArgb(170, 30, 30)
                    : (child.Flag == "UNUSED" ? Dim : MapColor(child.Kind));
                AddMapNodes(n, child);
            }
        }

        private static string MapLabel(MapStep s)
        {
            if (s == null) return "";
            string prefix = "";
            if (s.TpLine > 0) prefix = s.TpLine.ToString().PadLeft(3) + "  ";
            string extra = string.IsNullOrEmpty(s.Flag) ? "" : "  [" + s.Flag + "]";
            switch (s.Kind)
            {
                case "LBL": return prefix + s.Display + extra;
                case "JMP": return prefix + "->  " + s.Display + extra;
                case "TIMEOUT": return prefix + "TO  " + s.Display + extra;
                case "CALL": return prefix + s.Display + extra;
                case "MISS": return prefix + s.Display + extra;
                case "MSG": return prefix + s.Display + extra;
                case "UALM": return prefix + s.Display + extra;
                case "PROG": return s.Display + extra;
                default: return prefix + s.Display + extra;
            }
        }

        private static Color MapColor(string kind)
        {
            switch ((kind ?? "").ToUpperInvariant())
            {
                case "LBL": return Color.FromArgb(20, 90, 140);
                case "JMP": return UiTheme.Accent;
                case "TIMEOUT": return Color.FromArgb(140, 80, 0);
                case "CALL": return Color.FromArgb(20, 110, 70);
                case "MISS": return Color.FromArgb(170, 30, 30);
                case "UALM": return Color.FromArgb(160, 20, 20);
                case "MSG": return Color.FromArgb(140, 90, 0);
                case "ABORT": return Color.FromArgb(120, 20, 20);
                default: return UiTheme.Fg;
            }
        }

        private void OpenMapNode(TreeNode node)
        {
            if (node == null) return;
            var step = node.Tag as MapStep;
            if (step == null) return;
            if (!string.IsNullOrEmpty(step.FilePath) && File.Exists(step.FilePath) && step.LineNo > 0)
            {
                OpenPath(step.FilePath, step.LineNo, true);
                if (step.Kind == "JMP" || step.Kind == "LBL" || step.Kind == "TIMEOUT")
                    ShowCrossRefs("LBL[" + step.Target + "]", false);
                else if (step.Kind == "CALL" || step.Kind == "MISS")
                    ShowCrossRefs(step.Target, false);
                else if (step.Kind == "UALM")
                    ShowCrossRefs("UALM[" + step.Target + "]", false);
                return;
            }
            if ((step.Kind == "CALL" || step.Kind == "MISS") && !string.IsNullOrEmpty(step.Target))
            {
                var dest = _index.Resolve(step.Target);
                if (dest != null) OpenPath(dest.Path, 1, true);
                else _status.Text = "Missing program: " + step.Target;
            }
        }

        public void CheckMacroTable()
        {
            if (_index.Files.Count == 0)
            {
                string path = NppEditor.GetCurrentPath();
                if (!string.IsNullOrEmpty(path))
                    EnsureIndexForCurrentFile(path);
            }
            if (_index.Macros.Count == 0)
            {
                _status.Text = "No MACRO.DG / SYSMACRO.VA in this backup.";
                MessageBox.Show(
                    "No macro table found.\r\n\r\nPut MACRO.DG or SYSMACRO.VA in the robot backup folder, then click Index.",
                    "FanucNav — macro table",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            FillMacroTab(null);
            SelectTabByName("Macros");
            int used = 0;
            var seen = new HashSet<MacroEntry>();
            foreach (var u in _index.MacroUses)
                if (seen.Add(u.Macro)) used++;
            int missing = 0;
            foreach (var m in _index.Macros)
                if (!string.IsNullOrEmpty(m.KeyProg) && _index.Resolve(m.ProgName) == null)
                    missing++;
            _status.Text = "Macro table: " + _index.Macros.Count + " entries, " + used +
                           " used in programs, " + missing + " program file(s) missing.";
        }

        private void FillMacroTab(LsProgram limit)
        {
            _visibleMacros.Clear();
            _macroList.Items.Clear();
            if (_index.Macros.Count == 0)
            {
                _macroList.Items.Add("(no MACRO.DG / SYSMACRO.VA loaded)");
                return;
            }

            foreach (var mac in _index.Macros)
            {
                var uses = _index.UsesOf(mac);
                if (limit != null)
                {
                    bool hit = false;
                    foreach (var u in uses)
                    {
                        if (string.Equals(u.ProgramName, limit.Name, StringComparison.OrdinalIgnoreCase))
                        { hit = true; break; }
                    }
                    if (!hit) continue;
                }

                bool haveFile = !string.IsNullOrEmpty(mac.KeyProg) && _index.Resolve(mac.ProgName) != null;
                string flag = uses.Count > 0 ? "USED" : (haveFile ? "FREE" : "MISS");
                string line = flag + "  " + mac.Display + "  (" + uses.Count + " use" + (uses.Count == 1 ? "" : "s") + ")";
                _visibleMacros.Add(mac);
                _macroList.Items.Add(line);
            }

            if (limit == null)
            {
                foreach (var prog in _index.Files)
                {
                    if (!prog.IsMacro) continue;
                    bool inTable = false;
                    foreach (var mac in _index.Macros)
                    {
                        if (string.Equals(mac.KeyProg, prog.Name, StringComparison.OrdinalIgnoreCase))
                        { inTable = true; break; }
                    }
                    if (inTable) continue;
                    _visibleMacros.Add(prog);
                    _macroList.Items.Add("PROG  " + prog.Name + "  is a Macro .LS but not in the macro table");
                }
            }

            if (_macroList.Items.Count == 0)
                _macroList.Items.Add(limit == null ? "(macro table is empty)" : "(this program does not use the macro table)");
            FitList(_macroList);
        }

        private void SelectTabByName(string namePart)
        {
            foreach (TabPage page in _tabs.TabPages)
            {
                if (page.Text.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _tabs.SelectedTab = page;
                    return;
                }
            }
        }

        public void ShowCrossRefs(string symbol, bool limitToCurrentProgram)
        {
            if (string.IsNullOrEmpty(symbol)) return;
            _xrefSearch.Text = symbol;
            string limit = null;
            if (limitToCurrentProgram)
                limit = NppEditor.GetCurrentPath();

            var hits = _index.FindRefs(symbol, limit);
            if (hits.Count == 0 && limit != null)
                hits = _index.FindRefs(symbol, null);

            _visibleXrefs.Clear();
            _xrefList.BeginUpdate();
            _xrefList.Items.Clear();
            foreach (var h in hits)
            {
                _visibleXrefs.Add(h);
                _xrefList.Items.Add(h.ListLabel);
            }
            _xrefList.EndUpdate();
            FitList(_xrefList);
            _xrefTitle.Text = "Usages of " + symbol + "  (" + hits.Count + ")";
            HighlightSymbol(symbol);
        }

        private LsProgram SelectedProgram()
        {
            if (_progTree == null || _progTree.SelectedNode == null) return null;
            return _progTree.SelectedNode.Tag as LsProgram;
        }

        private string SelectedMissingName()
        {
            if (_progTree == null || _progTree.SelectedNode == null) return null;
            string tag = _progTree.SelectedNode.Tag as string;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("MISS:", StringComparison.OrdinalIgnoreCase))
                return null;
            return tag.Substring(5);
        }

        private void OpenSelectedProgram()
        {
            var p = SelectedProgram();
            if (p != null) OpenPath(p.Path, 1, true);
        }

        private void OpenSelectedCall()
        {
            int i = _callList.SelectedIndex;
            if (i < 0 || i >= _visibleCalls.Count) return;
            OpenProgramByName(_visibleCalls[i].Program, true);
        }

        private void OpenSelectedCaller()
        {
            int i = _callerList.SelectedIndex;
            if (i < 0 || i >= _visibleCallers.Count) return;
            var c = _visibleCallers[i];
            var prog = _index.Resolve(c.Program);
            if (prog != null) OpenPath(prog.Path, c.LineNo, true);
        }

        private void OpenSelectedLabel()
        {
            int i = _lblList.SelectedIndex;
            if (i < 0 || i >= _visibleLbls.Count) return;
            var lbl = _visibleLbls[i];
            var prog = SelectedProgram();
            if (prog != null) OpenPath(prog.Path, lbl.LineNo, true);
            ShowCrossRefs(lbl.Key, false);
        }

        private void OpenSelectedIo()
        {
            int i = _ioList.SelectedIndex;
            if (i < 0 || i >= _visibleIo.Count) return;
            var io = _visibleIo[i];
            var prog = SelectedProgram();
            if (prog != null) OpenPath(prog.Path, io.LineNo, true);
            ShowCrossRefs(io.Key, false);
        }

        private string SelectedDataKind()
        {
            if (_dataKind == null || _dataKind.SelectedItem == null) return "";
            string s = _dataKind.SelectedItem.ToString().Trim();
            if (s.StartsWith("All", StringComparison.OrdinalIgnoreCase)) return "";
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s.Substring(0, sp);
            return s.ToUpperInvariant();
        }

        private void FillDataTable()
        {
            if (_dataView == null) return;
            _dataView.BeginUpdate();
            _dataView.Items.Clear();
            if (_dataDetail != null)
                _dataDetail.Text = "Select a type (PR, R, F…) then click a row to see X/Y/Z or J1–J6.";

            string only = SelectedDataKind();
            string[] kinds = new string[]
            {
                "R", "PR", "P", "UFRAME", "UTOOL", "PAYLOAD", "SR",
                "F", "M", "AR", "VR",
                "DI", "DO", "GI", "GO", "RI", "RO", "UI", "UO",
                "SI", "SO", "WI", "WO", "AI", "AO",
                "PNS", "RSR",
                "UALM", "MESSAGE", "TIMER"
            };
            foreach (string kind in kinds)
            {
                if (only.Length > 0 && !string.Equals(kind, only, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddDataRows(kind);
            }

            string q = DataFilterText();
            if (q.Length > 0)
            {
                for (int i = _dataView.Items.Count - 1; i >= 0; i--)
                {
                    if (!DataRowMatches(_dataView.Items[i], q))
                        _dataView.Items.RemoveAt(i);
                }
            }

            _dataView.EndUpdate();
            FitDataColumns();
        }

        private string DataFilterText()
        {
            if (_dataFilter == null) return "";
            string q = (_dataFilter.Text ?? "").Trim();
            if (q.Length == 0 || q == "Filter data…") return "";
            return q;
        }

        private static bool DataRowMatches(ListViewItem item, string q)
        {
            if (item == null || string.IsNullOrEmpty(q)) return true;
            foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
            {
                if (sub.Text != null && sub.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            string tag = item.Tag as string;
            return tag != null && tag.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ShowSelectedDataDetail()
        {
            if (_dataView.SelectedItems.Count == 0)
            {
                if (_dataDetail != null)
                    _dataDetail.Text = "Select a row to see its value.";
                return;
            }
            string key = _dataView.SelectedItems[0].Tag as string;
            var prog = SelectedProgram();
            RegisterDef def = _index.FindRegister(key, prog != null ? prog.Name : null);
            if (def != null && (def.Axes.Count > 0 || !string.IsNullOrEmpty(def.Detail) ||
                string.Equals(def.Kind, "PAYLOAD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Kind, "UFRAME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Kind, "UTOOL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Kind, "PR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(def.Kind, "P", StringComparison.OrdinalIgnoreCase)))
            {
                string pose = RegTable.FormatPose(def);
                int uses = 0;
                int.TryParse(_dataView.SelectedItems[0].SubItems[4].Text, out uses);
                _dataDetail.Text = pose + Environment.NewLine + uses + " use(s) in programs  —  double-click to jump";
                return;
            }
            string name = _dataView.SelectedItems[0].SubItems[2].Text;
            string val = _dataView.SelectedItems[0].SubItems[3].Text;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(DataBaseKey(key));
            if (!string.IsNullOrEmpty(name)) sb.Append("Name:  ").AppendLine(name);
            sb.Append("Value: ").AppendLine(string.IsNullOrEmpty(val) ? "(not stored in backup table)" : val);
            if (def != null && !string.IsNullOrEmpty(def.Detail))
            {
                sb.AppendLine();
                sb.AppendLine(def.Detail);
            }
            int useCount = 0;
            int.TryParse(_dataView.SelectedItems[0].SubItems[4].Text, out useCount);
            sb.AppendLine();
            sb.Append(useCount).Append(" use(s) in programs  —  double-click to jump");
            _dataDetail.Text = sb.ToString();
        }

        private void AddDataRows(string kind)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var haveNum = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<RegisterDef>();

            foreach (var r in _index.Registers)
            {
                if (!string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(r.Key)) continue;
                rows.Add(r);
                haveNum.Add(r.Number ?? "");
            }

            rows.Sort(CompareDataRegs);
            foreach (var r in rows)
                _dataView.Items.Add(MakeDataRow(kind, r.Key, r));

            string prefix = kind + "[";
            var extras = new List<string>();
            foreach (var pair in _index.CrossRefs)
            {
                if (pair.Key == null || !pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Contains(pair.Key)) continue;
                string num = pair.Key.Substring(prefix.Length).TrimEnd(']');
                if (haveNum.Contains(num)) continue;
                extras.Add(pair.Key);
                seen.Add(pair.Key);
            }
            extras.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string k in extras)
                _dataView.Items.Add(MakeDataRow(kind, k, null));
        }

        private static int CompareDataRegs(RegisterDef a, RegisterDef b)
        {
            int na, nb;
            int ia = int.TryParse(a.Number, out na) ? na : int.MaxValue;
            int ib = int.TryParse(b.Number, out nb) ? nb : int.MaxValue;
            int c = ia.CompareTo(ib);
            if (c != 0) return c;
            return string.Compare(a.Source ?? "", b.Source ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private ListViewItem MakeDataRow(string kind, string key, RegisterDef def)
        {
            if (def == null) def = _index.FindRegister(key);
            string xrefKey = DataBaseKey(key);
            string limitPath = null;
            if (def != null && !string.IsNullOrEmpty(def.Source))
            {
                var src = _index.Resolve(def.Source);
                if (src != null) limitPath = src.Path;
            }
            var refs = _index.FindRefs(xrefKey, limitPath);
            if (refs.Count == 0 && limitPath != null)
                refs = _index.FindRefs(xrefKey, null);

            string name = def != null ? (def.Comment ?? "") : "";
            if (def != null && !string.IsNullOrEmpty(def.Source) &&
                name.IndexOf(def.Source, StringComparison.OrdinalIgnoreCase) < 0)
            {
                name = string.IsNullOrEmpty(name) ? def.Source : name + "  [" + def.Source + "]";
            }
            if (string.IsNullOrEmpty(name) && refs.Count > 0)
                name = FirstComment(refs[0].Raw, xrefKey);
            if (string.IsNullOrEmpty(name) &&
                string.Equals(kind, "MESSAGE", StringComparison.OrdinalIgnoreCase) &&
                refs.Count > 0)
                name = MessageText(refs[0].Raw);

            string value = "";
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.Value) && def.Value != "uninit")
                    value = def.Value;
                else if (!string.IsNullOrEmpty(def.Detail))
                    value = def.Detail;
                else if (def.Value == "uninit")
                    value = "uninit";
            }
            if (string.IsNullOrEmpty(value))
                value = LastAssignedValue(refs);

            var item = new ListViewItem(kind);
            item.SubItems.Add(xrefKey);
            item.SubItems.Add(name);
            item.SubItems.Add(value);
            item.SubItems.Add(refs.Count.ToString());
            item.Tag = key;
            UiTheme.ColorDataRow(item, kind, _dataView.Items.Count);
            if (def != null)
            {
                string tip = RegTable.FormatPose(def);
                if (!string.IsNullOrEmpty(tip)) item.ToolTipText = tip;
            }
            return item;
        }

        private static string DataBaseKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int at = key.IndexOf('@');
            return at > 0 ? key.Substring(0, at) : key;
        }

        private static string LastAssignedValue(List<CrossRefHit> refs)
        {
            if (refs == null) return "";
            for (int i = refs.Count - 1; i >= 0; i--)
            {
                string raw = refs[i].Raw ?? "";
                int eq = raw.LastIndexOf('=');
                if (eq < 0 || eq + 1 >= raw.Length) continue;
                string rhs = raw.Substring(eq + 1).Trim().TrimEnd(';').Trim();
                if (rhs.Length == 0) continue;
                if (rhs.StartsWith("ON", StringComparison.OrdinalIgnoreCase)) return "ON";
                if (rhs.StartsWith("OFF", StringComparison.OrdinalIgnoreCase)) return "OFF";
                int sp = rhs.IndexOf(' ');
                if (sp > 0) rhs = rhs.Substring(0, sp);
                if (rhs.Length > 0 && rhs.Length < 24) return rhs;
            }
            return "";
        }

        private static string FirstComment(string raw, string key)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(key)) return "";
            int colon = raw.IndexOf(key.TrimEnd(']') + ":", StringComparison.OrdinalIgnoreCase);
            if (colon < 0) return "";
            int start = colon + key.Length;
            int end = raw.IndexOf(']', start);
            if (end < 0) return "";
            return raw.Substring(start, end - start).Trim();
        }

        private static string MessageText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            int a = raw.IndexOf("MESSAGE[", StringComparison.OrdinalIgnoreCase);
            if (a < 0) return "";
            a += 8;
            int b = raw.IndexOf(']', a);
            if (b <= a) return "";
            return raw.Substring(a, b - a).Trim();
        }

        private void OpenSelectedData()
        {
            if (_dataView.SelectedItems.Count == 0) return;
            string key = _dataView.SelectedItems[0].Tag as string;
            if (string.IsNullOrEmpty(key)) return;
            string xrefKey = DataBaseKey(key);
            RegisterDef def = _index.FindRegister(key);
            string limit = null;
            if (def != null && !string.IsNullOrEmpty(def.Source))
            {
                var src = _index.Resolve(def.Source);
                if (src != null) limit = src.Path;
            }
            ShowCrossRefs(xrefKey, limit != null);
            var refs = _index.FindRefs(xrefKey, limit);
            if (refs.Count == 0) refs = _index.FindRefs(xrefKey, null);
            if (refs.Count > 0)
                OpenPath(refs[0].FilePath, refs[0].LineNo, true);
            ShowSelectedDataDetail();
            string val = _dataView.SelectedItems[0].SubItems[3].Text;
            _status.Text = xrefKey + (string.IsNullOrEmpty(val) ? "" : "  = " + val) +
                           "  (" + refs.Count + " uses)";
            HighlightSymbol(xrefKey);
        }

        private void ShowMacroUses(MacroEntry mac)
        {
            var uses = _index.UsesOf(mac);
            _visibleXrefs.Clear();
            _xrefList.Items.Clear();
            foreach (var u in uses)
            {
                var hit = new CrossRefHit();
                hit.ProgramName = u.ProgramName;
                hit.FilePath = u.FilePath;
                hit.LineNo = u.LineNo;
                hit.TpLine = u.TpLine;
                hit.Kind = "MACRO";
                hit.Symbol = mac.Name;
                hit.Raw = u.Raw;
                _visibleXrefs.Add(hit);
                _xrefList.Items.Add(hit.ListLabel);
            }
            _xrefTitle.Text = "Usages of macro " + mac.Name + "  (" + uses.Count + ")";
            FitList(_xrefList);
            SelectTabByName("Macros");
        }

        private void OpenSelectedMacro()
        {
            int i = _macroList.SelectedIndex;
            if (i < 0 || i >= _visibleMacros.Count) return;
            var mac = _visibleMacros[i] as MacroEntry;
            if (mac != null)
            {
                var uses = _index.UsesOf(mac);
                if (uses.Count > 0)
                {
                    OpenPath(uses[0].FilePath, uses[0].LineNo, true);
                    ShowMacroUses(mac);
                    return;
                }
                var dest = _index.Resolve(mac.ProgName);
                if (dest != null) OpenPath(dest.Path, 1, true);
                else _status.Text = "No uses and program file missing: " + mac.ProgName;
                return;
            }
            var prog = _visibleMacros[i] as LsProgram;
            if (prog != null) OpenPath(prog.Path, 1, true);
        }

        private void OpenSelectedXref()
        {
            int i = _xrefList.SelectedIndex;
            if (i < 0 || i >= _visibleXrefs.Count) return;
            var h = _visibleXrefs[i];
            OpenPath(h.FilePath, h.LineNo, true);
        }

        public void OpenProgramByName(string name, bool pushHistory)
        {
            var prog = _index.Resolve(name);
            if (prog == null)
            {
                _status.Text = "Program not in backup: " + name;
                return;
            }
            OpenPath(prog.Path, 1, pushHistory);
        }

        private void OpenPath(string path, int lineNo, bool pushHistory)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("::"))
            {
                _status.Text = "That file is inside a ZIP — extract the backup to jump.";
                return;
            }
            if (!File.Exists(path))
            {
                _status.Text = "Missing file: " + path;
                return;
            }
            if (pushHistory)
            {
                string cur = NppEditor.GetCurrentPath();
                if (!string.IsNullOrEmpty(cur)) _history.Push(cur + "|" + (NppEditor.LineFromPosition(NppEditor.GetCurrentPos()) + 1));
                UpdateBack();
            }
            NppEditor.OpenFile(path);
            if (lineNo > 0) NppEditor.GotoLine(lineNo - 1);
            SelectProgramByPath(path, true);
        }

        private void GoBack()
        {
            if (_history.Count == 0) return;
            string token = _history.Pop();
            UpdateBack();
            string path = token;
            int line = 1;
            int bar = token.LastIndexOf('|');
            if (bar > 0)
            {
                path = token.Substring(0, bar);
                int.TryParse(token.Substring(bar + 1), out line);
            }
            OpenPath(path, line, false);
        }

        private void UpdateBack()
        {
            _backBtn.Enabled = _history.Count > 0;
        }

        private void SelectProgramByPath(string path, bool refresh)
        {
            if (string.IsNullOrEmpty(path) || _progTree == null) return;
            string baseName = Path.GetFileNameWithoutExtension(path);
            TreeNode found = FindProgNode(_progTree.Nodes, path, baseName, true);
            if (found == null)
                found = FindProgNode(_progTree.Nodes, path, baseName, false);
            if (found == null) return;
            _suppress = true;
            _progTree.SelectedNode = found;
            found.EnsureVisible();
            _suppress = false;
            if (refresh) FillDetails(found.Tag as LsProgram);
        }

        private static TreeNode FindProgNode(TreeNodeCollection nodes, string path, string baseName, bool matchPath)
        {
            foreach (TreeNode n in nodes)
            {
                var p = n.Tag as LsProgram;
                if (p != null)
                {
                    if (matchPath && !string.IsNullOrEmpty(p.Path) &&
                        string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))
                        return n;
                    if (!matchPath && string.Equals(p.Name, baseName, StringComparison.OrdinalIgnoreCase))
                        return n;
                }
                var child = FindProgNode(n.Nodes, path, baseName, matchPath);
                if (child != null) return child;
            }
            return null;
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select FANUC robot backup folder (or a folder of .LS files)";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(_folderBox.Text) && Directory.Exists(_folderBox.Text))
                    dlg.SelectedPath = _folderBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _folderBox.Text = dlg.SelectedPath;
                    ReloadFolder();
                }
            }
        }

        private static string TransformText(string text, RenumberResult opt)
        {
            string updated = text;
            if (opt.DoLabels)
            {
                int changed;
                updated = LsParser.RenumberLabels(updated, opt.LabelStart, opt.LabelStep, out changed);
            }
            if (opt.DoLines)
            {
                int count;
                updated = LsParser.RenumberMn(updated, out count);
            }
            return updated;
        }

        private static bool ApplyRenumberToFile(string path, RenumberResult opt)
        {
            string text = File.ReadAllText(path);
            string updated = TransformText(text, opt);
            if (updated == text) return false;
            File.WriteAllText(path, updated);
            return true;
        }

        private static bool LooksLikeFanuc(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".LS", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".KL", StringComparison.OrdinalIgnoreCase);
        }

        private string ConfigPath()
        {
            try
            {
                string dir = Path.Combine(NppEditor.GetConfigDirectory(), "FanucNav");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "lastfolder.txt");
            }
            catch { return null; }
        }

        public void LoadLastFolder()
        {
            try
            {
                string cfg = ConfigPath();
                if (cfg != null && File.Exists(cfg))
                {
                    string folder = File.ReadAllText(cfg).Trim();
                    if (Directory.Exists(folder) || File.Exists(folder))
                    {
                        _folderBox.Text = folder;
                        ReloadFolder();
                    }
                }
            }
            catch { }
        }

        private void SaveLastFolder(string folder)
        {
            try
            {
                string cfg = ConfigPath();
                if (cfg != null) File.WriteAllText(cfg, folder ?? "");
            }
            catch { }
        }

        private static TextBox MakeBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PanelBg,
                ForeColor = Fg,
                Margin = new Padding(1)
            };
        }

        private static Button MakeButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(232, 234, 238),
                ForeColor = Fg,
                Font = new Font("Segoe UI Semibold", 8.25F),
                Margin = new Padding(2, 1, 1, 1),
                Cursor = Cursors.Hand,
                FlatAppearance = { BorderColor = Color.FromArgb(176, 180, 188) }
            };
            btn.MouseEnter += (s, e) =>
            {
                btn.BackColor = Accent;
                btn.ForeColor = Color.White;
            };
            btn.MouseLeave += (s, e) =>
            {
                btn.BackColor = Color.FromArgb(232, 234, 238);
                btn.ForeColor = Fg;
            };
            return btn;
        }

        private static ListBox MakeList()
        {
            var box = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = PanelBg,
                ForeColor = Fg,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                ScrollAlwaysVisible = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20
            };
            box.DrawItem += UiTheme.DrawListItem;
            return box;
        }

        private static void FitList(ListBox box)
        {
            if (box == null) return;
            int max = box.ClientSize.Width;
            try
            {
                foreach (object item in box.Items)
                {
                    string s = item != null ? item.ToString() : "";
                    int w = TextRenderer.MeasureText(s, box.Font).Width + 24;
                    if (w > max) max = w;
                }
            }
            catch { }
            box.HorizontalExtent = max;
        }

        private void FitDataColumns()
        {
            if (_dataView == null || _dataView.Columns.Count == 0) return;
            try
            {
                _dataView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.HeaderSize);
                for (int i = 0; i < _dataView.Columns.Count; i++)
                {
                    int header = TextRenderer.MeasureText(_dataView.Columns[i].Text, _dataView.Font).Width + 28;
                    int content = header;
                    int n = Math.Min(_dataView.Items.Count, 400);
                    for (int r = 0; r < n; r++)
                    {
                        if (i >= _dataView.Items[r].SubItems.Count) continue;
                        int w = TextRenderer.MeasureText(_dataView.Items[r].SubItems[i].Text, _dataView.Font).Width + 20;
                        if (w > content) content = w;
                    }
                    _dataView.Columns[i].Width = Math.Min(Math.Max(header, content), 420);
                }
            }
            catch { }
        }

        private static TabPage WrapTab(string title, Control inner)
        {
            var page = new TabPage(title) { BackColor = PanelBg, UseVisualStyleBackColor = false };
            inner.Dock = DockStyle.Fill;
            page.Controls.Add(inner);
            return page;
        }

        private void StyleTabs()
        {
            _tabs.SizeMode = TabSizeMode.Normal;
            _tabs.ItemSize = new Size(0, 22);
            _tabs.BackColor = Bg;
            _tabs.ShowToolTips = true;
        }

        public bool IsFloating
        {
            get { return _floatHost != null && !_floatHost.IsDisposed; }
        }

        public void BringFloatToFront()
        {
            if (!IsFloating) return;
            try
            {
                _floatHost.Show();
                _floatHost.BringToFront();
                _floatHost.Activate();
            }
            catch { }
        }

        public void ToggleFloat()
        {
            if (IsFloating) DockToNpp();
            else FloatFromNpp();
        }

        private void FloatFromNpp()
        {
            try
            {
                int mainDist = 0, lowerDist = 0, dataDist = 0;
                try { mainDist = _mainSplit.SplitterDistance; } catch { }
                try { lowerDist = _lowerSplit.SplitterDistance; } catch { }
                try { dataDist = _dataSplit.SplitterDistance; } catch { }

                NppEditor.HideDock(Handle);

                _floatHost = new Form
                {
                    Text = "FanucNav",
                    FormBorderStyle = FormBorderStyle.SizableToolWindow,
                    ShowInTaskbar = false,
                    StartPosition = FormStartPosition.Manual,
                    Size = new Size(Math.Max(440, Math.Max(Width, 320)), Math.Max(640, Math.Max(Height, 400))),
                    Location = new Point(80, 80),
                    MinimumSize = new Size(300, 280),
                    BackColor = Bg,
                    Font = Font
                };
                MoveChildControls(this, _floatHost);
                _floatHost.FormClosing += FloatHost_FormClosing;
                _floatBtn.Text = "Dock";
                _floatHost.Shown += (s, e) => RestoreSplitters(mainDist, lowerDist, dataDist);
                _floatHost.Show();
                _status.Text = "Floating window — click Dock (or close it) to put the panel back.";
            }
            catch (Exception ex)
            {
                _status.Text = "Could not float panel: " + ex.Message;
                try { NppEditor.ShowDock(Handle); } catch { }
                _floatBtn.Text = "Float";
            }
        }

        private void FloatHost_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_dockingBack || ForceClose) return;
            e.Cancel = true;
            DockToNpp();
        }

        private void DockToNpp()
        {
            _dockingBack = true;
            try
            {
                int mainDist = 0, lowerDist = 0, dataDist = 0;
                try { mainDist = _mainSplit.SplitterDistance; } catch { }
                try { lowerDist = _lowerSplit.SplitterDistance; } catch { }
                try { dataDist = _dataSplit.SplitterDistance; } catch { }

                if (_floatHost != null)
                {
                    _floatHost.FormClosing -= FloatHost_FormClosing;
                    if (!_floatHost.IsDisposed)
                        MoveChildControls(_floatHost, this);
                    var host = _floatHost;
                    _floatHost = null;
                    try { host.Close(); host.Dispose(); } catch { }
                }

                _floatBtn.Text = "Float";
                NppEditor.ShowDock(Handle);
                try { Visible = true; } catch { }
                RestoreSplitters(mainDist, lowerDist, dataDist);
                BeginInvoke(new Action(() =>
                {
                    RestoreSplitters(mainDist, lowerDist, dataDist);
                    PerformLayout();
                    Invalidate(true);
                }));
                _status.Text = "Panel docked. Click Float for a separate window, or drag the Notepad++ tab.";
            }
            catch (Exception ex)
            {
                _status.Text = "Could not dock panel: " + ex.Message;
            }
            finally
            {
                _dockingBack = false;
            }
        }

        private static void MoveChildControls(Control from, Control to)
        {
            if (from == null || to == null) return;
            to.SuspendLayout();
            from.SuspendLayout();
            var items = new List<Control>();
            foreach (Control c in from.Controls)
                items.Add(c);
            from.Controls.Clear();
            foreach (Control c in items)
                to.Controls.Add(c);
            from.ResumeLayout(false);
            to.ResumeLayout(true);
        }

        private void RestoreSplitters(int mainDist, int lowerDist, int dataDist)
        {
            try
            {
                if (mainDist > 40 && _mainSplit.Height > mainDist + 40)
                    _mainSplit.SplitterDistance = mainDist;
                if (lowerDist > 40 && _lowerSplit.Height > lowerDist + 40)
                    _lowerSplit.SplitterDistance = lowerDist;
                if (dataDist > 30 && _dataSplit != null && _dataSplit.Height > dataDist + 30)
                    _dataSplit.SplitterDistance = dataDist;
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (IsFloating && !ForceClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                DockToNpp();
                return;
            }
            if (ForceClose && _floatHost != null && !_floatHost.IsDisposed)
            {
                try
                {
                    _dockingBack = true;
                    MoveChildControls(_floatHost, this);
                    _floatHost.Close();
                }
                catch { }
                _floatHost = null;
            }
            CloseUndocked();
            base.OnFormClosing(e);
        }

        private void Tabs_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            for (int i = 0; i < _tabs.TabCount; i++)
            {
                if (!_tabs.GetTabRect(i).Contains(e.Location)) continue;
                _tabs.SelectedIndex = i;
                var menu = new ContextMenuStrip();
                menu.Items.Add("Undock this tab", null, (s, a) => UndockSelectedTab());
                menu.Items.Add("Dock all floating tabs", null, (s, a) => CloseUndocked());
                menu.Show(_tabs, e.Location);
                return;
            }
        }

        private void UndockSelectedTab()
        {
            if (_tabs.SelectedTab == null || _tabs.SelectedTab.Controls.Count == 0) return;
            var page = _tabs.SelectedTab;
            var inner = page.Controls[0];
            var holderHint = inner as Label;
            if (holderHint != null && (holderHint.Text ?? "").IndexOf("is floating", StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            page.Controls.Remove(inner);

            var holder = new Label
            {
                Text = page.Text + " is floating.\r\nClose that window to dock it back.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Dim,
                BackColor = PanelBg
            };
            page.Controls.Add(holder);

            var floatForm = new Form
            {
                Text = "FanucNav — " + page.Text,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(Math.Max(340, inner.Width + 24), Math.Max(260, inner.Height + 48)),
                Location = PointToScreen(new Point(Math.Max(12, Width / 4), Math.Max(40, Height / 5))),
                BackColor = Bg,
                MinimumSize = new Size(240, 160)
            };
            inner.Dock = DockStyle.Fill;
            floatForm.Controls.Add(inner);
            floatForm.FormClosed += (s, e) =>
            {
                if (inner.Parent == floatForm) floatForm.Controls.Remove(inner);
                if (!page.IsDisposed)
                {
                    page.Controls.Clear();
                    inner.Dock = DockStyle.Fill;
                    page.Controls.Add(inner);
                    _tabs.SelectedTab = page;
                }
                _undocked.Remove(floatForm);
            };
            _undocked.Add(floatForm);
            floatForm.Show(this);
            _status.Text = page.Text + " undocked — close that window to dock it back.";
        }

        private void CloseUndocked()
        {
            var copy = _undocked.ToList();
            foreach (var f in copy)
            {
                try { if (!f.IsDisposed) f.Close(); } catch { }
            }
            _undocked.Clear();
        }
    }
}
