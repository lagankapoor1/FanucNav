using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public sealed class MapStep
    {
        public string Kind;
        public string Text;
        public string Condition;
        public string Target;
        public int LineNo;
        public int TpLine;
        public string FilePath;
        public string Raw;
        public string Flag;
        public readonly List<MapStep> Children = new List<MapStep>();

        public string Display
        {
            get
            {
                if (!string.IsNullOrEmpty(Condition))
                    return Condition + "  →  " + Text;
                return Text ?? "";
            }
        }
    }

    public static class ProgramMap
    {
        private static readonly Regex IfThen = new Regex(
            @"^IF\s+(.+?),\s*(CALL|RUN|JMP)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CallRe = new Regex(
            @"\b(CALL|RUN)\s+([A-Za-z][A-Za-z0-9_]{0,35})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JmpRe = new Regex(
            @"\bJMP\s+LBL\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)\s*(?::([^\]]+))?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LblDefRe = new Regex(
            @"^\s*LBL\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)\s*(?::([^\]]+))?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TimeoutRe = new Regex(
            @"TIMEOUT\s*,\s*LBL\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)\s*(?::([^\]]+))?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MsgRe = new Regex(
            @"\bMESSAGE\s*\[\s*([^\]]+?)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UalmRe = new Regex(
            @"\bUALM\s*\[\s*(\d+)\s*(?::([^\]]+))?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SelectorNameRe = new Regex(
            @"^(PNS|RSR)\d+|^STYLE|^JOB\d|^MAIN$|^PROD",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsSelectorName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return SelectorNameRe.IsMatch(name.Trim());
        }

        public static bool IsSelectorAssign(string assignType)
        {
            if (string.IsNullOrEmpty(assignType)) return false;
            string t = assignType.Trim().ToUpperInvariant();
            return t == "PNS" || t == "RSR" || t == "UK" || t == "SU" || t == "MF";
        }

        public static MapStep Build(LsProgram prog, string text, int expandCalls, RobotIndex index)
        {
            var root = new MapStep();
            root.Kind = "PROG";
            root.Text = prog != null ? prog.Name : "(program)";
            root.Target = prog != null ? prog.Name : "";
            root.FilePath = prog != null ? prog.Path : "";
            if (prog == null) return root;

            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(prog.Path) && File.Exists(prog.Path))
            {
                try { text = File.ReadAllText(prog.Path, Encoding.Default); }
                catch { text = ""; }
            }

            MapStep section = root;
            foreach (var line in LsParser.EnumerateMnLines(text ?? ""))
            {
                var step = FromLine(line, prog.Path);
                if (step == null) continue;
                if (step.Kind == "LBL")
                {
                    root.Children.Add(step);
                    section = step;
                }
                else
                    section.Children.Add(step);
            }

            AnnotateLabels(root);

            if (expandCalls > 0 && index != null)
                ExpandCalls(root, index, expandCalls, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            return root;
        }

        public static string ProgramForSelector(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            var m = Regex.Match(symbol.Trim(), @"^(RSR|PNS)\s*\[\s*(\d+)\s*\]$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int n;
            if (!int.TryParse(m.Groups[2].Value, out n)) return null;
            return m.Groups[1].Value.ToUpperInvariant() + n.ToString("0000");
        }

        public static void AnnotateLabels(MapStep root)
        {
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var jumped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(root, s =>
            {
                if (s.Kind == "LBL" && !string.IsNullOrEmpty(s.Target))
                    defined.Add(s.Target);
                if ((s.Kind == "JMP" || s.Kind == "TIMEOUT") && !string.IsNullOrEmpty(s.Target))
                    jumped.Add(s.Target);
            });
            Walk(root, s =>
            {
                if (s.Kind == "LBL" && !jumped.Contains(s.Target))
                    s.Flag = "UNUSED";
                else if ((s.Kind == "JMP" || s.Kind == "TIMEOUT") && !defined.Contains(s.Target))
                    s.Flag = "MISSING";
            });
        }

        public static void Walk(MapStep node, Action<MapStep> fn)
        {
            if (node == null || fn == null) return;
            fn(node);
            foreach (var c in node.Children)
                Walk(c, fn);
        }

        public sealed class SelectRow
        {
            public string Signal;
            public string Op;
            public string Value;
            public string Action;
            public string Target;
            public int LineNo;
            public int TpLine;
            public string FilePath;
            public string Raw;
        }

        private static readonly Regex SelectIf = new Regex(
            @"^IF\s+((?:GI|GO|DI|DO|RI|RO|UI|UO|SI|SO|R|SR)\s*\[[^\]]+\])\s*(<>|>=|<=|=|>|<)\s*([^\s,]+)\s*,\s*(CALL|RUN|JMP)\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<SelectRow> ExtractSelectTable(LsProgram prog, string text)
        {
            var list = new List<SelectRow>();
            if (prog == null) return list;
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(prog.Path) && File.Exists(prog.Path))
            {
                try { text = File.ReadAllText(prog.Path, Encoding.Default); }
                catch { return list; }
            }
            foreach (var line in LsParser.EnumerateMnLines(text ?? ""))
            {
                string body = LsParser.StripTpComment(line.Body);
                if (string.IsNullOrEmpty(body)) continue;
                var m = SelectIf.Match(body);
                if (!m.Success) continue;
                var row = new SelectRow();
                row.Signal = Regex.Replace(m.Groups[1].Value, @"\s+", "");
                row.Op = m.Groups[2].Value;
                row.Value = m.Groups[3].Value.Trim();
                string act = m.Groups[4].Value.ToUpperInvariant();
                string rest = m.Groups[5].Value.Trim().TrimEnd(';').Trim();
                if (act == "JMP")
                {
                    var jm = Regex.Match(rest, @"LBL\s*\[\s*([^\]]+)\]", RegexOptions.IgnoreCase);
                    row.Target = jm.Success ? "LBL[" + jm.Groups[1].Value.Trim() + "]" : rest;
                    row.Action = "JMP " + row.Target;
                }
                else
                {
                    var cm = Regex.Match(rest, @"^([A-Za-z][A-Za-z0-9_]{0,35})");
                    row.Target = cm.Success ? cm.Groups[1].Value.ToUpperInvariant() : rest;
                    row.Action = act + " " + row.Target;
                }
                row.LineNo = line.FileLine;
                row.TpLine = line.TpLine;
                row.FilePath = prog.Path;
                row.Raw = line.Raw;
                list.Add(row);
            }
            return list;
        }

        public static string ToText(MapStep root, List<SelectRow> table)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FanucNav program map");
            sb.AppendLine(root != null ? root.Text : "");
            sb.AppendLine(new string('-', 48));
            WriteTree(sb, root, 0);
            if (table != null && table.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Select / style table");
                sb.AppendLine("Signal\tOp\tValue\tAction");
                foreach (var r in table)
                    sb.AppendLine(r.Signal + "\t" + r.Op + "\t" + r.Value + "\t" + r.Action);
            }
            return sb.ToString();
        }

        public static string ToCsv(List<SelectRow> table)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Signal,Op,Value,Action,Target,Line");
            if (table == null) return sb.ToString();
            foreach (var r in table)
            {
                sb.Append(Csv(r.Signal)).Append(',')
                  .Append(Csv(r.Op)).Append(',')
                  .Append(Csv(r.Value)).Append(',')
                  .Append(Csv(r.Action)).Append(',')
                  .Append(Csv(r.Target)).Append(',')
                  .Append(r.TpLine).AppendLine();
            }
            return sb.ToString();
        }

        private static void WriteTree(StringBuilder sb, MapStep node, int indent)
        {
            if (node == null) return;
            if (node.Kind != "PROG")
            {
                string flag = string.IsNullOrEmpty(node.Flag) ? "" : "  [" + node.Flag + "]";
                sb.Append(' ', indent * 2).Append(node.Display).AppendLine(flag);
            }
            foreach (var c in node.Children)
                WriteTree(sb, c, node.Kind == "PROG" ? indent : indent + 1);
        }

        private static string Csv(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        private static void ExpandCalls(MapStep node, RobotIndex index, int depth, HashSet<string> stack)
        {
            if (node == null || depth <= 0) return;
            var kids = new List<MapStep>(node.Children);
            foreach (var child in kids)
            {
                if (child.Kind != "CALL" || string.IsNullOrEmpty(child.Target))
                {
                    ExpandCalls(child, index, depth, stack);
                    continue;
                }
                if (!stack.Add(child.Target)) continue;
                var dest = index.Resolve(child.Target);
                if (dest == null)
                {
                    var miss = new MapStep();
                    miss.Kind = "MISS";
                    miss.Text = child.Target + "  (missing .LS)";
                    miss.Target = child.Target;
                    child.Children.Add(miss);
                    continue;
                }
                string text = "";
                try
                {
                    if (!string.IsNullOrEmpty(dest.Path) && File.Exists(dest.Path))
                        text = File.ReadAllText(dest.Path, Encoding.Default);
                }
                catch { }
                var built = Build(dest, text, 0, null);
                foreach (var c in built.Children)
                    child.Children.Add(c);
                ExpandCalls(child, index, depth - 1, new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase));
            }
        }

        public static MapStep FromLine(LsParser.MnLine line, string filePath)
        {
            string body = LsParser.StripTpComment(line.Body);
            if (string.IsNullOrWhiteSpace(body) || body.StartsWith("!"))
                return null;

            string cond = "";
            var ifm = IfThen.Match(body);
            if (ifm.Success) cond = "IF " + ifm.Groups[1].Value.Trim();

            if (LblDefRe.IsMatch(body) && body.IndexOf("JMP", StringComparison.OrdinalIgnoreCase) < 0)
            {
                var m = LblDefRe.Match(body);
                var s = Base(line, filePath, "LBL");
                s.Target = m.Groups[1].Value.Trim();
                string cmt = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                s.Text = "LBL[" + s.Target + (string.IsNullOrEmpty(cmt) ? "" : ":" + cmt) + "]";
                return s;
            }

            var jmp = JmpRe.Match(body);
            if (jmp.Success)
            {
                var s = Base(line, filePath, "JMP");
                s.Target = jmp.Groups[1].Value.Trim();
                string cmt = jmp.Groups[2].Success ? jmp.Groups[2].Value.Trim() : "";
                s.Text = "JMP LBL[" + s.Target + (string.IsNullOrEmpty(cmt) ? "" : ":" + cmt) + "]";
                s.Condition = cond;
                return s;
            }

            var to = TimeoutRe.Match(body);
            if (to.Success)
            {
                var s = Base(line, filePath, "TIMEOUT");
                s.Target = to.Groups[1].Value.Trim();
                s.Text = "TIMEOUT, LBL[" + s.Target + "]";
                return s;
            }

            var call = CallRe.Match(body);
            if (call.Success)
            {
                var s = Base(line, filePath, "CALL");
                s.Target = call.Groups[2].Value.ToUpperInvariant();
                s.Text = call.Groups[1].Value.ToUpperInvariant() + " " + s.Target;
                s.Condition = cond;
                return s;
            }

            var msg = MsgRe.Match(body);
            if (msg.Success)
            {
                var s = Base(line, filePath, "MSG");
                s.Text = "MESSAGE[" + msg.Groups[1].Value.Trim() + "]";
                s.Target = msg.Groups[1].Value.Trim();
                return s;
            }

            var ualm = UalmRe.Match(body);
            if (ualm.Success)
            {
                var s = Base(line, filePath, "UALM");
                s.Target = ualm.Groups[1].Value;
                string cmt = ualm.Groups[2].Success ? ualm.Groups[2].Value.Trim() : "";
                s.Text = "UALM[" + s.Target + (string.IsNullOrEmpty(cmt) ? "" : ":" + cmt) + "]";
                return s;
            }

            if (Regex.IsMatch(body, @"^\s*ABORT\b", RegexOptions.IgnoreCase))
            {
                var s = Base(line, filePath, "ABORT");
                s.Text = "ABORT";
                return s;
            }
            if (Regex.IsMatch(body, @"^\s*END\b", RegexOptions.IgnoreCase))
            {
                var s = Base(line, filePath, "END");
                s.Text = "END";
                return s;
            }
            if (Regex.IsMatch(body, @"^\s*PAUSE\b", RegexOptions.IgnoreCase))
            {
                var s = Base(line, filePath, "PAUSE");
                s.Text = "PAUSE";
                return s;
            }

            return null;
        }

        private static MapStep Base(LsParser.MnLine line, string path, string kind)
        {
            var s = new MapStep();
            s.Kind = kind;
            s.LineNo = line.FileLine;
            s.TpLine = line.TpLine;
            s.FilePath = path;
            s.Raw = line.Raw;
            return s;
        }
    }
}
