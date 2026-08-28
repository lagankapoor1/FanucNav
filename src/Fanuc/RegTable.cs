using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public sealed class RegisterDef
    {
        public string Kind;
        public string Number;
        public string Comment;
        public string Value;
        public string Detail;
        public string Source;
        public string Config;
        public string Uf;
        public string Ut;
        public int Group;
        public readonly System.Collections.Generic.Dictionary<string, string> Axes =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public string BaseKey
        {
            get { return Kind + "[" + Number + "]"; }
        }

        public string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(Source) &&
                    string.Equals(Kind, "P", StringComparison.OrdinalIgnoreCase))
                    return BaseKey + "@" + Source;
                return BaseKey;
            }
        }

        public string Display
        {
            get
            {
                string c = string.IsNullOrEmpty(Comment) ? "" : "  " + Comment;
                string v = string.IsNullOrEmpty(Value) ? "" : "  = " + Value;
                return Key + c + v;
            }
        }

        public bool HasName
        {
            get { return !string.IsNullOrWhiteSpace(Comment); }
        }
    }

    public static class RegTable
    {
        private static readonly Regex NumLine = new Regex(
            @"^\s*\[(\d+)\]\s*=\s*(\S+)\s+'([^']*)'",
            RegexOptions.Compiled);
        private static readonly Regex PosLine = new Regex(
            @"^\s*\[(\d+)\s*,\s*(\d+)\]\s*=\s*'([^']*)'(.*)$",
            RegexOptions.Compiled);
        private static readonly Regex JointLine = new Regex(
            @"\b(J[1-9]|E[1-3]|X|Y|Z|W|P|R)\s*[:=]\s*([-\d.*]+)\s*(deg|mm)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FrameSection = new Regex(
            @"\[\*SYSTEM\*\]\$(MNUFRAME|MNUTOOL|MNUFRAMENUM|MNUTOOLNUM)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AnySystem = new Regex(
            @"\[\*SYSTEM\*\]\$",
            RegexOptions.Compiled);
        private static readonly Regex FrameItem = new Regex(
            @"^\s*\[(\d+)\s*,\s*(\d+)\]\s*=",
            RegexOptions.Compiled);
        private static readonly Regex ConfigRe = new Regex(
            @"Config(?:\s*:|\s+)\s*([^\r\n]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UfUtRe = new Regex(
            @"\bUF\s*:\s*([0-9F]+)\s*,\s*UT\s*:\s*([0-9F]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PayloadLine = new Regex(
            @"^\[CBPARAM\]PAYLOAD(\d+)(?:_([A-Z]+))?\s+.*?\s=\s+(\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ActiveNum = new Regex(
            @"^\s*\[(\d+)\]\s*=\s*(\S+)",
            RegexOptions.Compiled);

        public static List<RegisterDef> LoadFromFolder(string folder)
        {
            var list = new List<RegisterDef>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return list;

            AddRange(list, LoadNum(Find(folder, "NUMREG.VA"), "R"));
            AddRange(list, LoadPos(Find(folder, "POSREG.VA")));
            AddRange(list, LoadNum(Find(folder, "STRREG.VA"), "SR"));
            AddRange(list, LoadFrames(Find(folder, "SYSFRAME.VA")));
            AddRange(list, LoadPayloads(Find(folder, "CBPARAM.VA")));
            return list;
        }

        public static List<RegisterDef> LoadNum(string path, string kind)
        {
            var list = new List<RegisterDef>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            string text;
            try { text = File.ReadAllText(path, Encoding.Default); }
            catch { return list; }

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var m = NumLine.Match(raw);
                if (!m.Success) continue;
                var d = new RegisterDef();
                d.Kind = kind;
                d.Number = m.Groups[1].Value;
                d.Value = m.Groups[2].Value.Trim();
                d.Comment = (m.Groups[3].Value ?? "").Trim();
                if (d.Value == "0" && string.IsNullOrEmpty(d.Comment)) continue;
                list.Add(d);
            }
            return list;
        }

        public static List<RegisterDef> LoadPos(string path)
        {
            var list = new List<RegisterDef>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.Default); }
            catch { return list; }

            RegisterDef cur = null;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = PosLine.Match(lines[i]);
                if (m.Success)
                {
                    FlushPos(list, cur);
                    cur = new RegisterDef();
                    cur.Kind = "PR";
                    int g;
                    int.TryParse(m.Groups[1].Value, out g);
                    cur.Group = g;
                    cur.Number = m.Groups[2].Value;
                    cur.Comment = (m.Groups[3].Value ?? "").Trim();
                    string rest = m.Groups[4].Value ?? "";
                    if (rest.IndexOf("Uninitialized", StringComparison.OrdinalIgnoreCase) >= 0)
                        cur.Value = "uninit";
                    else
                        cur.Value = "taught";
                    ApplyMeta(cur, rest);
                    continue;
                }
                if (cur == null) continue;
                ApplyMeta(cur, lines[i]);
                CollectAxes(cur, lines[i]);
            }
            FlushPos(list, cur);
            return list;
        }

        public static List<RegisterDef> LoadFrames(string path)
        {
            var list = new List<RegisterDef>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return list;
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.Default); }
            catch { return list; }

            string section = null;
            RegisterDef cur = null;
            int activeFrame = -1;
            int activeTool = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                var sec = FrameSection.Match(line);
                if (sec.Success)
                {
                    FlushFrame(list, cur);
                    cur = null;
                    section = sec.Groups[1].Value.ToUpperInvariant();
                    continue;
                }
                if (AnySystem.IsMatch(line) && line.IndexOf("$MNU", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    FlushFrame(list, cur);
                    cur = null;
                    section = null;
                    continue;
                }

                if (section == "MNUFRAMENUM" || section == "MNUTOOLNUM")
                {
                    var an = ActiveNum.Match(line);
                    int n, v;
                    if (an.Success && int.TryParse(an.Groups[1].Value, out n) && n == 1 &&
                        int.TryParse(an.Groups[2].Value, out v))
                    {
                        if (section == "MNUFRAMENUM") activeFrame = v;
                        else activeTool = v;
                    }
                    continue;
                }

                if (section != "MNUFRAME" && section != "MNUTOOL") continue;

                var item = FrameItem.Match(line);
                if (item.Success)
                {
                    FlushFrame(list, cur);
                    cur = new RegisterDef();
                    cur.Kind = section == "MNUFRAME" ? "UFRAME" : "UTOOL";
                    int g;
                    int.TryParse(item.Groups[1].Value, out g);
                    cur.Group = g;
                    cur.Number = item.Groups[2].Value;
                    if (line.IndexOf("Uninitialized", StringComparison.OrdinalIgnoreCase) >= 0)
                        cur.Value = "uninit";
                    ApplyMeta(cur, line);
                    continue;
                }
                if (cur == null) continue;
                if (line.IndexOf("Uninitialized", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    cur.Axes.Count == 0)
                    cur.Value = "uninit";
                ApplyMeta(cur, line);
                CollectAxes(cur, line);
            }
            FlushFrame(list, cur);

            foreach (var r in list)
            {
                int n;
                if (!int.TryParse(r.Number, out n)) continue;
                if (r.Kind == "UFRAME" && n == activeFrame)
                    r.Comment = string.IsNullOrEmpty(r.Comment) ? "selected" : r.Comment + "  (selected)";
                if (r.Kind == "UTOOL" && n == activeTool)
                    r.Comment = string.IsNullOrEmpty(r.Comment) ? "selected" : r.Comment + "  (selected)";
            }
            return list;
        }

        public static List<RegisterDef> LoadPayloads(string path)
        {
            var map = new Dictionary<int, RegisterDef>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<RegisterDef>();
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.Default); }
            catch { return new List<RegisterDef>(); }

            foreach (string line in lines)
            {
                var m = PayloadLine.Match(line);
                if (!m.Success) continue;
                int n;
                if (!int.TryParse(m.Groups[1].Value, out n)) continue;
                RegisterDef d;
                if (!map.TryGetValue(n, out d))
                {
                    d = new RegisterDef();
                    d.Kind = "PAYLOAD";
                    d.Number = n.ToString();
                    map[n] = d;
                }
                string field = m.Groups[2].Success ? m.Groups[2].Value.ToUpperInvariant() : "MASS";
                string raw = m.Groups[3].Value;
                if (raw.IndexOf("Uninit", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string pretty = PrettyNum(raw);
                if (field == "MASS")
                {
                    d.Value = pretty + " kg";
                    d.Axes["MASS"] = pretty;
                }
                else
                {
                    d.Axes[field] = pretty;
                }
            }

            var list = new List<RegisterDef>();
            var nums = new List<int>(map.Keys);
            nums.Sort();
            foreach (int n in nums)
            {
                var d = map[n];
                var sb = new StringBuilder();
                if (d.Axes.ContainsKey("MASS"))
                    sb.Append("Mass = ").Append(d.Axes["MASS"]).Append(" kg");
                string[] cg = new string[] { "X", "Y", "Z" };
                bool anyCg = false;
                foreach (string a in cg)
                {
                    string v;
                    if (!d.Axes.TryGetValue(a, out v)) continue;
                    if (!anyCg)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.AppendLine("Center of gravity:");
                        anyCg = true;
                    }
                    sb.Append("  ").Append(a.PadRight(4)).Append("= ").Append(v).Append(" mm").AppendLine();
                }
                string[] iner = new string[] { "IX", "IY", "IZ" };
                bool anyI = false;
                foreach (string a in iner)
                {
                    string v;
                    if (!d.Axes.TryGetValue(a, out v)) continue;
                    if (!anyI)
                    {
                        sb.AppendLine("Inertia:");
                        anyI = true;
                    }
                    sb.Append("  ").Append(a.PadRight(4)).Append("= ").Append(v).AppendLine();
                }
                d.Detail = sb.ToString();
                if (string.IsNullOrEmpty(d.Value) && d.Axes.Count == 0) continue;
                list.Add(d);
            }
            return list;
        }

        public static string FormatPose(RegisterDef d)
        {
            if (d == null) return "";
            var sb = new StringBuilder();
            if (string.Equals(d.Kind, "PAYLOAD", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(d.BaseKey);
                if (!string.IsNullOrEmpty(d.Comment)) sb.Append("   ").Append(d.Comment);
                sb.AppendLine();
                if (!string.IsNullOrEmpty(d.Detail)) sb.Append(d.Detail);
                else if (!string.IsNullOrEmpty(d.Value)) sb.Append("Mass: ").AppendLine(d.Value);
                return sb.ToString();
            }

            sb.Append(d.BaseKey);
            if (!string.IsNullOrEmpty(d.Comment)) sb.Append("   ").Append(d.Comment);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(d.Source)) sb.Append("Program: ").AppendLine(d.Source);
            if (d.Group > 0) sb.Append("Group: ").Append(d.Group).AppendLine();
            if (!string.IsNullOrEmpty(d.Uf) || !string.IsNullOrEmpty(d.Ut))
            {
                sb.Append("UF: ").Append(string.IsNullOrEmpty(d.Uf) ? "-" : d.Uf);
                sb.Append("    UT: ").Append(string.IsNullOrEmpty(d.Ut) ? "-" : d.Ut).AppendLine();
            }
            if (!string.IsNullOrEmpty(d.Config)) sb.Append("Config: ").AppendLine(d.Config.Trim());
            if (!string.IsNullOrEmpty(d.Value)) sb.Append("Value: ").AppendLine(d.Value);
            sb.AppendLine();

            bool anyJ = false;
            string[] jnames = new string[] { "J1", "J2", "J3", "J4", "J5", "J6", "J7", "J8", "J9", "E1", "E2", "E3" };
            foreach (string j in jnames)
            {
                string v;
                if (!d.Axes.TryGetValue(j, out v)) continue;
                if (!anyJ)
                {
                    sb.AppendLine("Joints:");
                    anyJ = true;
                }
                string extra = "";
                if (j == "J7") extra = "   (E1 ext)";
                else if (j == "J8") extra = "   (E2 ext)";
                else if (j == "J9") extra = "   (E3 ext)";
                else if (j.StartsWith("E")) extra = "   (external)";
                sb.Append("  ").Append(j.PadRight(4)).Append("= ").Append(v).Append(extra).AppendLine();
            }

            bool anyC = false;
            string[] cnames = new string[] { "X", "Y", "Z", "W", "P", "R" };
            foreach (string c in cnames)
            {
                string v;
                if (!d.Axes.TryGetValue(c, out v)) continue;
                if (!anyC)
                {
                    if (anyJ) sb.AppendLine();
                    sb.AppendLine("Cartesian:");
                    anyC = true;
                }
                string unit = (c == "X" || c == "Y" || c == "Z") ? " mm" : " deg";
                if (v.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    v.IndexOf("deg", StringComparison.OrdinalIgnoreCase) >= 0)
                    unit = "";
                sb.Append("  ").Append(c.PadRight(4)).Append("= ").Append(v).Append(unit).AppendLine();
            }

            if (!anyJ && !anyC && !string.IsNullOrEmpty(d.Detail))
                sb.AppendLine(d.Detail);
            return sb.ToString();
        }

        private static void FlushPos(List<RegisterDef> list, RegisterDef cur)
        {
            if (cur == null) return;
            FinishPose(cur);
            if (!cur.HasName && cur.Axes.Count == 0 && cur.Value == "uninit")
                return;
            list.Add(cur);
        }

        private static void FlushFrame(List<RegisterDef> list, RegisterDef cur)
        {
            if (cur == null) return;
            FinishPose(cur);
            list.Add(cur);
        }

        public static void FinishPose(RegisterDef cur)
        {
            if (cur == null) return;
            if (cur.Axes.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var kv in cur.Axes)
                {
                    if (sb.Length > 0) sb.Append("  ");
                    sb.Append(kv.Key).Append("=").Append(kv.Value);
                }
                cur.Detail = sb.ToString();
                if (cur.Value == "taught" || string.IsNullOrEmpty(cur.Value) || cur.Value == "********")
                    cur.Value = ShortValue(cur);
            }
        }

        public static void CollectAxes(RegisterDef cur, string line)
        {
            if (cur == null || string.IsNullOrEmpty(line)) return;
            foreach (Match j in JointLine.Matches(line))
            {
                string val = j.Groups[2].Value;
                if (val.IndexOf('*') >= 0) continue;
                string ax = j.Groups[1].Value.ToUpperInvariant();
                string unit = j.Groups[3].Success ? j.Groups[3].Value : "";
                cur.Axes[ax] = string.IsNullOrEmpty(unit) ? val : val + " " + unit;
            }
        }

        public static void ApplyMeta(RegisterDef cur, string line)
        {
            if (cur == null || string.IsNullOrEmpty(line)) return;
            var uf = UfUtRe.Match(line);
            if (uf.Success)
            {
                cur.Uf = uf.Groups[1].Value;
                cur.Ut = uf.Groups[2].Value;
            }
            var cfg = ConfigRe.Match(line);
            if (cfg.Success)
            {
                string c = cfg.Groups[1].Value.Trim().TrimEnd(',');
                if (c.StartsWith("'") && c.EndsWith("'") && c.Length >= 2)
                    c = c.Substring(1, c.Length - 2);
                cur.Config = c.Trim();
            }
        }

        public static string ShortValue(RegisterDef d)
        {
            string v;
            if (d.Axes.TryGetValue("X", out v))
                return "X=" + StripUnit(v);
            if (d.Axes.TryGetValue("J1", out v))
                return "J1=" + StripUnit(v);
            if (d.Axes.TryGetValue("MASS", out v))
                return v + " kg";
            if (!string.IsNullOrEmpty(d.Uf) || !string.IsNullOrEmpty(d.Ut))
                return "UF " + (d.Uf ?? "-") + "  UT " + (d.Ut ?? "-");
            return string.IsNullOrEmpty(d.Value) ? "" : d.Value;
        }

        private static string StripUnit(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            int sp = v.IndexOf(' ');
            return sp > 0 ? v.Substring(0, sp) : v;
        }

        public static string PrettyNum(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            double v;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                if (Math.Abs(v) >= 1000) return v.ToString("0.###", CultureInfo.InvariantCulture);
                return v.ToString("0.###", CultureInfo.InvariantCulture);
            }
            return raw;
        }

        private static void AddRange(List<RegisterDef> dest, List<RegisterDef> src)
        {
            if (src != null) dest.AddRange(src);
        }

        private static string Find(string folder, string name)
        {
            string p = Path.Combine(folder, name);
            if (File.Exists(p)) return p;
            try
            {
                foreach (string f in Directory.GetFiles(folder, name))
                    return f;
            }
            catch { }
            return null;
        }
    }
}
