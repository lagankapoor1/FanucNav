using System;
using System.Collections.Generic;
using System.Linq;

namespace FanucNav.Fanuc
{
    public sealed class DiffRow
    {
        public string Status;
        public string Kind;
        public string Name;
        public string Left;
        public string Right;

        public string Display
        {
            get { return Status + "  " + Kind + "  " + Name; }
        }
    }

    public static class BackupCompare
    {
        public static List<DiffRow> Compare(RobotIndex left, RobotIndex right)
        {
            var rows = new List<DiffRow>();
            if (left == null) left = new RobotIndex();
            if (right == null) right = new RobotIndex();

            var leftNames = new HashSet<string>(left.Programs.Keys, StringComparer.OrdinalIgnoreCase);
            var rightNames = new HashSet<string>(right.Programs.Keys, StringComparer.OrdinalIgnoreCase);

            foreach (string name in leftNames.Union(rightNames).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                bool inL = leftNames.Contains(name);
                bool inR = rightNames.Contains(name);
                if (inL && !inR)
                {
                    rows.Add(Row("removed", "PROG", name, "present", "missing"));
                    continue;
                }
                if (!inL && inR)
                {
                    rows.Add(Row("added", "PROG", name, "missing", "present"));
                    continue;
                }
                var lp = left.Resolve(name);
                var rp = right.Resolve(name);
                var lc = CallSet(lp);
                var rc = CallSet(rp);
                if (!lc.SetEquals(rc))
                {
                    rows.Add(Row("changed",
                        ProgramMap.IsSelectorName(name) ? "PNS/STYLE" : "CALLS",
                        name,
                        string.Join(", ", lc.OrderBy(s => s).ToArray()),
                        string.Join(", ", rc.OrderBy(s => s).ToArray())));
                }
            }

            CompareRegs(rows, left, right, "R");
            CompareRegs(rows, left, right, "PR");
            CompareRegs(rows, left, right, "UFRAME");
            CompareRegs(rows, left, right, "UTOOL");
            CompareRegs(rows, left, right, "PAYLOAD");
            return rows;
        }

        private static HashSet<string> CallSet(LsProgram p)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p == null) return set;
            foreach (var c in p.Calls)
                if (!string.IsNullOrEmpty(c.Program)) set.Add(c.Program);
            return set;
        }

        private static void CompareRegs(List<DiffRow> rows, RobotIndex left, RobotIndex right, string kind)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in left.Registers)
                if (string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)) keys.Add(r.BaseKey);
            foreach (var r in right.Registers)
                if (string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase)) keys.Add(r.BaseKey);
            var sorted = new List<string>(keys);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string key in sorted)
            {
                var l = left.FindRegister(key);
                var r = right.FindRegister(key);
                string lv = RegValue(l);
                string rv = RegValue(r);
                if (string.Equals(lv, rv, StringComparison.OrdinalIgnoreCase)) continue;
                string st = (l == null) ? "added" : (r == null ? "removed" : "changed");
                rows.Add(Row(st, kind, key, lv, rv));
            }
        }

        private static string RegValue(RegisterDef d)
        {
            if (d == null) return "";
            if (!string.IsNullOrEmpty(d.Value) && d.Value != "uninit") return d.Value;
            if (!string.IsNullOrEmpty(d.Detail)) return d.Detail;
            if (d.Value == "uninit") return "uninit";
            return "";
        }

        private static DiffRow Row(string status, string kind, string name, string left, string right)
        {
            var d = new DiffRow();
            d.Status = status;
            d.Kind = kind;
            d.Name = name;
            d.Left = left ?? "";
            d.Right = right ?? "";
            return d;
        }
    }
}
