using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public static class MacroTable
    {
        private static readonly Regex DgSlot = new Regex(@"^\[\s*(\d+)\s*\]\s*NAME\s*:\s*(.*)\s*$", RegexOptions.Compiled);
        private static readonly Regex DgProg = new Regex(@"^\s*PROG NAME\s*:\s*(.*)\s*$", RegexOptions.Compiled);
        private static readonly Regex DgType = new Regex(@"^\s*Assign type\s*:\s*(.*)\s*$", RegexOptions.Compiled);
        private static readonly Regex DgId = new Regex(@"^\s*Assign ID\s*:\s*(\d+)\s*$", RegexOptions.Compiled);
        private static readonly Regex VaName = new Regex(
            @"\$MACROTABLE\[(\d+)\]\.\$MACRO_NAME[^=]*=\s*'([^']*)'",
            RegexOptions.Compiled);
        private static readonly Regex VaProg = new Regex(
            @"\$MACROTABLE\[(\d+)\]\.\$PROG_NAME[^=]*=\s*'([^']*)'",
            RegexOptions.Compiled);
        private static readonly Regex VaType = new Regex(
            @"\$MACROTABLE\[(\d+)\]\.\$ASSIGN_TYPE[^=]*=\s*(\d+)",
            RegexOptions.Compiled);
        private static readonly Regex VaId = new Regex(
            @"\$MACROTABLE\[(\d+)\]\.\$ASSIGN_ID[^=]*=\s*(-?\d+)",
            RegexOptions.Compiled);

        public static List<MacroEntry> LoadFromFolder(string folder)
        {
            var list = new List<MacroEntry>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return list;

            string dg = FindFirst(folder, "MACRO.DG", "macro.dg");
            if (dg != null)
            {
                try { list = ParseDg(File.ReadAllText(dg, Encoding.Default)); }
                catch { }
            }

            if (list.Count == 0)
            {
                string va = FindFirst(folder, "SYSMACRO.VA", "sysmacro.va");
                if (va != null)
                {
                    try { list = ParseVa(File.ReadAllText(va, Encoding.Default)); }
                    catch { }
                }
            }

            return list;
        }

        public static List<MacroEntry> ParseDg(string text)
        {
            var list = new List<MacroEntry>();
            if (string.IsNullOrEmpty(text)) return list;

            MacroEntry cur = null;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                var slot = DgSlot.Match(line);
                if (slot.Success)
                {
                    if (IsUseful(cur)) list.Add(cur);
                    cur = new MacroEntry();
                    int n;
                    int.TryParse(slot.Groups[1].Value, out n);
                    cur.Slot = n;
                    cur.Name = slot.Groups[2].Value.Trim();
                    continue;
                }
                if (cur == null) continue;
                if (line.IndexOf("SYSTEM MACRO", StringComparison.OrdinalIgnoreCase) >= 0)
                    cur.SystemMacro = true;
                var p = DgProg.Match(line);
                if (p.Success) { cur.ProgName = p.Groups[1].Value.Trim(); continue; }
                var t = DgType.Match(line);
                if (t.Success) { cur.AssignType = t.Groups[1].Value.Trim(); continue; }
                var id = DgId.Match(line);
                if (id.Success)
                {
                    int n;
                    int.TryParse(id.Groups[1].Value, out n);
                    cur.AssignId = n;
                }
            }
            if (IsUseful(cur)) list.Add(cur);
            return list;
        }

        public static List<MacroEntry> ParseVa(string text)
        {
            var map = new Dictionary<int, MacroEntry>();
            if (string.IsNullOrEmpty(text)) return new List<MacroEntry>();

            foreach (Match m in VaName.Matches(text))
                Get(map, m.Groups[1].Value).Name = m.Groups[2].Value.Trim();
            foreach (Match m in VaProg.Matches(text))
                Get(map, m.Groups[1].Value).ProgName = m.Groups[2].Value.Trim();
            foreach (Match m in VaType.Matches(text))
            {
                int n;
                int.TryParse(m.Groups[2].Value, out n);
                Get(map, m.Groups[1].Value).AssignType = TypeName(n);
            }
            foreach (Match m in VaId.Matches(text))
            {
                int n;
                int.TryParse(m.Groups[2].Value, out n);
                Get(map, m.Groups[1].Value).AssignId = n;
            }

            var list = new List<MacroEntry>();
            var keys = new List<int>(map.Keys);
            keys.Sort();
            foreach (int k in keys)
                if (IsUseful(map[k])) list.Add(map[k]);
            return list;
        }

        private static MacroEntry Get(Dictionary<int, MacroEntry> map, string slot)
        {
            int n;
            int.TryParse(slot, out n);
            MacroEntry e;
            if (!map.TryGetValue(n, out e))
            {
                e = new MacroEntry { Slot = n };
                map[n] = e;
            }
            return e;
        }

        private static bool IsUseful(MacroEntry e)
        {
            if (e == null) return false;
            bool hasName = !string.IsNullOrWhiteSpace(e.Name);
            bool hasProg = !string.IsNullOrWhiteSpace(e.ProgName);
            return hasName || hasProg;
        }

        private static string TypeName(int assignType)
        {
            switch (assignType)
            {
                case 1: return "--";
                case 2: return "UK";
                case 3: return "SU";
                case 4: return "MF";
                case 5: return "DI";
                case 6: return "RI";
                case 7: return "UI";
                default: return assignType.ToString();
            }
        }

        private static string FindFirst(string folder, params string[] names)
        {
            foreach (string name in names)
            {
                string p = Path.Combine(folder, name);
                if (File.Exists(p)) return p;
            }
            try
            {
                foreach (string f in Directory.GetFiles(folder, "MACRO.DG"))
                    return f;
                foreach (string f in Directory.GetFiles(folder, "SYSMACRO.VA"))
                    return f;
            }
            catch { }
            return null;
        }
    }
}
