using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public static class LsParser
    {
        private static readonly Regex ProgHdr = new Regex(@"^/PROG\s+(\S+)(?:[ \t]+(\S+))?", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex CommentRe = new Regex(@"COMMENT\s*=\s*""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex LinePrefix = new Regex(@"^\s*(\d+)\s*:\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex LineCountRe = new Regex(@"(LINE_COUNT\s*=\s*)(\d+)", RegexOptions.Compiled);
        private static readonly Regex CallRe = new Regex(@"\b(CALL|RUN)\s+([A-Za-z][A-Za-z0-9_]{0,35})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DataRe = new Regex(
            @"\b(UALM|PAYLOAD|TIMER|UFRAME|UTOOL|PNS|RSR|PR|SR|VR|AR|IR|GI|GO|DI|DO|RI|RO|UI|UO|SI|SO|WI|WO|AI|AO|PL|F|M|R|P)\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)(?:\s*,\s*\d+)?\s*(?::([^\]]+))?\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MessageRe = new Regex(
            @"\bMESSAGE\s*\[\s*([^\]]+?)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumAssignRe = new Regex(
            @"\b(UFRAME_NUM|UTOOL_NUM|PAYLOAD_NUM)\s*=\s*(?:(?:UFRAME|UTOOL|PAYLOAD)\s*\[\s*)?(\d+|R\s*\[\s*\d+\s*\])\s*\]?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> IoKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DI", "DO", "RI", "RO", "GI", "GO", "UI", "UO", "SI", "SO", "WI", "WO", "AI", "AO", "F", "M"
        };
        private static readonly Regex LblRe = new Regex(@"\b(?:(JMP)\s+)?LBL\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)\s*(?::([^\]]+))?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TimeoutLblRe = new Regex(@"TIMEOUT\s*,\s*LBL\s*\[\s*(R\s*\[\s*\d+\s*\]|\d+)\s*(?::([^\]]+))?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SectionRe = new Regex(@"^/[A-Z]", RegexOptions.Compiled);
        private static readonly Regex PosBlockRe = new Regex(
            @"P\[(\d+)(?:\s*:\s*[""']?([^\]""']*)[""']?)?\]\s*\{([\s\S]*?)\}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ON", "OFF", "MAX_SPEED", "FINE", "CNT", "SEC", "msec"
        };

        public static LsProgram ParseFile(string path)
        {
            string text = File.ReadAllText(path, Encoding.Default);
            return Parse(path, text);
        }

        public static LsProgram Parse(string path, string text)
        {
            var prog = new LsProgram();
            prog.Path = path;
            string subtype;
            ParseHeader(text, path, out prog.Name, out prog.Comment, out subtype);
            prog.IsMacro = string.Equals(subtype, "Macro", StringComparison.OrdinalIgnoreCase);

            foreach (var line in EnumerateMnLines(text))
            {
                string body = StripTpComment(line.Body);
                if (string.IsNullOrWhiteSpace(body) || body.StartsWith("!"))
                    continue;

                CollectCalls(prog, line, body);
                CollectData(prog, line, body);
                CollectLabels(prog, line, body);
            }

            foreach (var pos in ParsePosSection(text, prog.Name))
                prog.Positions.Add(pos);

            return prog;
        }

        public static IEnumerable<MnLine> EnumerateMnLines(string text)
        {
            if (string.IsNullOrEmpty(text)) yield break;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool inMn = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();
                if (trimmed.Equals("/MN", StringComparison.OrdinalIgnoreCase))
                {
                    inMn = true;
                    continue;
                }
                if (inMn && trimmed.StartsWith("/") && SectionRe.IsMatch(trimmed))
                    yield break;
                if (!inMn) continue;

                var m = LinePrefix.Match(raw);
                var line = new MnLine();
                line.FileLine = i + 1;
                line.Raw = raw;
                if (m.Success)
                {
                    int tp;
                    int.TryParse(m.Groups[1].Value, out tp);
                    line.TpLine = tp;
                    line.Body = m.Groups[2].Value;
                }
                else
                {
                    line.TpLine = 0;
                    line.Body = raw.Trim();
                }
                yield return line;
            }
        }

        public static bool TryResolveAtPosition(string line, int column, out CursorSymbol symbol)
        {
            return TryResolveAtPosition(line, column, null, out symbol);
        }

        public static bool TryResolveAtPosition(string line, int column, IList<MacroEntry> macros, out CursorSymbol symbol)
        {
            symbol = null;
            if (string.IsNullOrEmpty(line)) return false;

            string work = line;
            int bodyCol = column;
            var pref = LinePrefix.Match(line);
            if (pref.Success)
            {
                work = pref.Groups[2].Value;
                bodyCol = column - pref.Groups[2].Index;
            }
            if (bodyCol < 0) bodyCol = 0;
            if (bodyCol > work.Length) bodyCol = work.Length;

            foreach (Match m in DataRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                string kind = m.Groups[1].Value.ToUpperInvariant();
                symbol = new CursorSymbol();
                symbol.Kind = IoKinds.Contains(kind) ? "IO" : "DATA";
                symbol.Symbol = kind + "[" + NormalizeNum(m.Groups[2].Value) + "]";
                symbol.Display = symbol.Symbol + (m.Groups[3].Success ? ":" + m.Groups[3].Value.Trim() : "");
                return true;
            }

            foreach (Match m in NumAssignRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                string kind = MapAssignKind(m.Groups[1].Value);
                string num = NormalizeNum(m.Groups[2].Value);
                symbol = new CursorSymbol();
                symbol.Kind = "DATA";
                symbol.Symbol = kind + "[" + num + "]";
                symbol.Display = m.Groups[1].Value.ToUpperInvariant() + "=" + num + "  →  " + symbol.Symbol;
                return true;
            }

            foreach (Match m in MessageRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                string text = (m.Groups[1].Value ?? "").Trim();
                symbol = new CursorSymbol();
                symbol.Kind = "DATA";
                symbol.Symbol = "MESSAGE[" + text + "]";
                symbol.Display = symbol.Symbol;
                return true;
            }

            foreach (Match m in TimeoutLblRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                symbol = new CursorSymbol();
                symbol.Kind = "LBL";
                symbol.Symbol = "LBL[" + NormalizeNum(m.Groups[1].Value) + "]";
                symbol.Display = symbol.Symbol;
                return true;
            }

            foreach (Match m in LblRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                symbol = new CursorSymbol();
                symbol.Kind = "LBL";
                symbol.Symbol = "LBL[" + NormalizeNum(m.Groups[2].Value) + "]";
                symbol.Display = symbol.Symbol + (m.Groups[3].Success ? ":" + m.Groups[3].Value.Trim() : "");
                return true;
            }

            foreach (Match m in CallRe.Matches(work))
            {
                if (!ContainsColumn(m, bodyCol)) continue;
                string name = m.Groups[2].Value;
                if (Reserved.Contains(name)) continue;
                symbol = new CursorSymbol();
                symbol.Kind = "CALL";
                symbol.Symbol = name.ToUpperInvariant();
                symbol.Display = m.Groups[1].Value.ToUpperInvariant() + " " + name;
                return true;
            }

            if (macros != null)
            {
                foreach (var mac in macros)
                {
                    if (string.IsNullOrEmpty(mac.Name) || mac.Name.Length < 3) continue;
                    var rx = new Regex(Regex.Escape(mac.Name).Replace(@"\ ", @"\s+"), RegexOptions.IgnoreCase);
                    var m = rx.Match(work);
                    if (!m.Success || !ContainsColumn(m, bodyCol)) continue;
                    symbol = new CursorSymbol();
                    symbol.Kind = "MACRO";
                    symbol.Symbol = string.IsNullOrEmpty(mac.KeyProg) ? mac.KeyName : mac.KeyProg;
                    symbol.Display = mac.Name + " → " + mac.ProgName;
                    return true;
                }
            }

            return false;
        }

        public static string RenumberMn(string text, out int newLineCount)
        {
            newLineCount = 0;
            if (string.IsNullOrEmpty(text)) return text;

            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder(text.Length + 64);
            bool inMn = false;
            int step = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();
                string outLine = raw;

                if (trimmed.Equals("/MN", StringComparison.OrdinalIgnoreCase))
                {
                    inMn = true;
                }
                else if (inMn && trimmed.StartsWith("/") && SectionRe.IsMatch(trimmed))
                {
                    inMn = false;
                }
                else if (inMn)
                {
                    step++;
                    string body = LinePrefix.IsMatch(raw)
                        ? LinePrefix.Match(raw).Groups[2].Value
                        : raw.Trim();
                    outLine = FormatTpLine(step, body);
                }

                if (i > 0) sb.Append(nl);
                sb.Append(outLine);
            }

            newLineCount = step;
            string result = sb.ToString();
            int countValue = newLineCount;
            result = LineCountRe.Replace(result, delegate(Match m) { return m.Groups[1].Value + countValue; });
            return result;
        }

        public static string RenumberLabels(string text, int start, int step, out int changed)
        {
            changed = 0;
            if (step <= 0) step = 1;
            if (start < 1) start = 1;

            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in EnumerateMnLines(text))
            {
                foreach (Match m in LblRe.Matches(line.Body ?? ""))
                {
                    string id = NormalizeNum(m.Groups[2].Value);
                    int n;
                    if (!int.TryParse(id, out n)) continue;
                    if (seen.Add(id)) ids.Add(id);
                }
            }

            if (ids.Count == 0) return text;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int next = start;
            foreach (string oldId in ids)
            {
                map[oldId] = next.ToString();
                if (oldId != next.ToString()) changed++;
                next += step;
            }

            var lblAny = new Regex(@"\bLBL\s*\[\s*(\d+)\s*(:[^\]]+)?\]", RegexOptions.IgnoreCase);
            return lblAny.Replace(text, m =>
            {
                string oldId = m.Groups[1].Value;
                string mapped;
                if (!map.TryGetValue(oldId, out mapped)) return m.Value;
                string comment = m.Groups[2].Success ? m.Groups[2].Value : "";
                string prefix = m.Value.Substring(0, m.Groups[1].Index - m.Index);
                return prefix + mapped + comment + "]";
            });
        }

        public static int CountMnSteps(string text)
        {
            int n = 0;
            foreach (var line in EnumerateMnLines(text))
            {
                if (LinePrefix.IsMatch(line.Raw)) n++;
            }
            return n;
        }

        public static bool NeedsRenumber(string text)
        {
            int expected = 1;
            foreach (var line in EnumerateMnLines(text))
            {
                var m = LinePrefix.Match(line.Raw ?? "");
                if (!m.Success) return true;
                int tp;
                if (!int.TryParse(m.Groups[1].Value, out tp) || tp != expected)
                    return true;
                expected++;
            }
            return false;
        }

        public static bool LooksLikeLs(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".LS", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".ls", StringComparison.OrdinalIgnoreCase);
        }

        public static bool BodyUsesMacro(string body, MacroEntry mac)
        {
            if (string.IsNullOrEmpty(body) || mac == null) return false;
            string work = StripTpComment(body);
            if (string.IsNullOrEmpty(work) || work.StartsWith("!")) return false;

            string norm = MacroEntry.NormalizeWs(work);
            string name = mac.KeyName;
            string prog = mac.KeyProg;

            if (!string.IsNullOrEmpty(prog))
            {
                if (Regex.IsMatch(work, @"\b(?:CALL|RUN)\s+" + Regex.Escape(mac.ProgName) + @"\b", RegexOptions.IgnoreCase))
                    return true;
            }

            if (!string.IsNullOrEmpty(name) && name.Length >= 3)
            {
                if (norm.StartsWith(name) &&
                    (norm.Length == name.Length || !char.IsLetterOrDigit(norm[name.Length])))
                    return true;
            }

            if (mac.AssignId > 0 &&
                !string.IsNullOrEmpty(mac.AssignType) &&
                mac.AssignType != "--")
            {
                string tok = mac.AssignType.Trim().ToUpperInvariant();
                if (Regex.IsMatch(work, @"\b" + Regex.Escape(tok) + @"\s*\[\s*" + mac.AssignId + @"\s*\]", RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        public static string HowUsesMacro(string body, MacroEntry mac)
        {
            string work = StripTpComment(body);
            if (!string.IsNullOrEmpty(mac.KeyProg) &&
                Regex.IsMatch(work, @"\b(?:CALL|RUN)\s+" + Regex.Escape(mac.ProgName) + @"\b", RegexOptions.IgnoreCase))
                return "CALL " + mac.KeyProg;
            if (mac.AssignId > 0 && !string.IsNullOrEmpty(mac.AssignType) && mac.AssignType != "--")
            {
                string tok = mac.AssignType.Trim().ToUpperInvariant();
                if (Regex.IsMatch(work, @"\b" + Regex.Escape(tok) + @"\s*\[\s*" + mac.AssignId + @"\s*\]", RegexOptions.IgnoreCase))
                    return tok + "[" + mac.AssignId + "]";
            }
            return "INSTR " + mac.Name;
        }

        public static string FormatTpLine(int number, string body)
        {
            return string.Format("   {0,3}:  {1}", number, body ?? "");
        }

        public static string StripTpComment(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            return body.Trim().TrimEnd(';').Trim();
        }

        private static void ParseHeader(string text, string path, out string name, out string comment, out string subtype)
        {
            name = Path.GetFileNameWithoutExtension(path ?? "");
            comment = "";
            subtype = "";
            if (string.IsNullOrEmpty(text)) return;
            var m = ProgHdr.Match(text);
            if (m.Success)
            {
                name = m.Groups[1].Value.Trim();
                if (m.Groups[2].Success) subtype = m.Groups[2].Value.Trim();
            }
            var c = CommentRe.Match(text);
            if (c.Success) comment = c.Groups[1].Value.Trim();
        }

        private static void CollectCalls(LsProgram prog, MnLine line, string body)
        {
            foreach (Match m in CallRe.Matches(body))
            {
                string name = m.Groups[2].Value;
                if (Reserved.Contains(name)) continue;
                var call = new ProgramCall();
                call.LineNo = line.FileLine;
                call.TpLine = line.TpLine;
                call.Kind = m.Groups[1].Value.ToUpperInvariant();
                call.Program = name.ToUpperInvariant();
                call.Raw = line.Raw.Trim();
                prog.Calls.Add(call);
            }
        }

        private static void CollectData(LsProgram prog, MnLine line, string body)
        {
            foreach (Match m in DataRe.Matches(body))
            {
                var io = new IoReference();
                io.LineNo = line.FileLine;
                io.TpLine = line.TpLine;
                io.Kind = m.Groups[1].Value.ToUpperInvariant();
                io.Number = NormalizeNum(m.Groups[2].Value);
                io.Comment = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
                io.Raw = line.Raw.Trim();
                if (IoKinds.Contains(io.Kind))
                    prog.IoRefs.Add(io);
                else
                    prog.DataRefs.Add(io);
            }
            foreach (Match m in NumAssignRe.Matches(body))
            {
                var io = new IoReference();
                io.LineNo = line.FileLine;
                io.TpLine = line.TpLine;
                io.Kind = MapAssignKind(m.Groups[1].Value);
                io.Number = NormalizeNum(m.Groups[2].Value);
                io.Comment = m.Groups[1].Value.ToUpperInvariant() + "=" + io.Number;
                io.Raw = line.Raw.Trim();
                prog.DataRefs.Add(io);
            }
            foreach (Match m in MessageRe.Matches(body))
            {
                var io = new IoReference();
                io.LineNo = line.FileLine;
                io.TpLine = line.TpLine;
                io.Kind = "MESSAGE";
                io.Number = (m.Groups[1].Value ?? "").Trim();
                io.Comment = io.Number;
                io.Raw = line.Raw.Trim();
                prog.DataRefs.Add(io);
            }
        }

        public static string MapAssignKind(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            switch (raw.Trim().ToUpperInvariant())
            {
                case "UFRAME_NUM": return "UFRAME";
                case "UTOOL_NUM": return "UTOOL";
                case "PAYLOAD_NUM": return "PAYLOAD";
                default: return raw.Trim().ToUpperInvariant();
            }
        }

        private static void CollectLabels(LsProgram prog, MnLine line, string body)
        {
            foreach (Match m in LblRe.Matches(body))
            {
                var lbl = new LblReference();
                lbl.LineNo = line.FileLine;
                lbl.TpLine = line.TpLine;
                lbl.Kind = m.Groups[1].Success ? "JMP" : "LBL";
                lbl.LabelId = NormalizeNum(m.Groups[2].Value);
                lbl.Comment = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "";
                lbl.Raw = line.Raw.Trim();
                prog.LblRefs.Add(lbl);
            }

            foreach (Match m in TimeoutLblRe.Matches(body))
            {
                var lbl = new LblReference();
                lbl.LineNo = line.FileLine;
                lbl.TpLine = line.TpLine;
                lbl.Kind = "TIMEOUT";
                lbl.LabelId = NormalizeNum(m.Groups[1].Value);
                lbl.Comment = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                lbl.Raw = line.Raw.Trim();
                prog.LblRefs.Add(lbl);
            }
        }

        private static bool ContainsColumn(Match m, int col)
        {
            return col >= m.Index && col <= m.Index + m.Length;
        }

        private static string NormalizeNum(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            return Regex.Replace(raw, @"\s+", "").ToUpperInvariant();
        }

        public static List<RegisterDef> ParsePosSection(string text, string programName)
        {
            var list = new List<RegisterDef>();
            if (string.IsNullOrEmpty(text)) return list;

            int start = IndexOfSection(text, "/POS");
            if (start < 0) return list;
            int end = IndexOfSection(text, "/END", start + 4);
            string block = end > start ? text.Substring(start, end - start) : text.Substring(start);

            foreach (Match m in PosBlockRe.Matches(block))
            {
                var d = new RegisterDef();
                d.Kind = "P";
                d.Number = m.Groups[1].Value;
                d.Source = programName ?? "";
                d.Comment = (m.Groups[2].Value ?? "").Trim();
                string body = m.Groups[3].Value ?? "";
                RegTable.ApplyMeta(d, body);
                RegTable.CollectAxes(d, body);
                if (body.IndexOf("****", StringComparison.Ordinal) >= 0 && d.Axes.Count == 0)
                    d.Value = "********";
                else if (d.Axes.Count == 0)
                    d.Value = "uninit";
                else
                    d.Value = "taught";
                RegTable.FinishPose(d);
                list.Add(d);
            }
            return list;
        }

        private static int IndexOfSection(string text, string tag, int from = 0)
        {
            int i = text.IndexOf(tag, from, StringComparison.OrdinalIgnoreCase);
            return i;
        }

        public struct MnLine
        {
            public int FileLine;
            public int TpLine;
            public string Body;
            public string Raw;
        }
    }
}
