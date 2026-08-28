using System;
using System.Collections.Generic;

namespace FanucNav.Fanuc
{
    public sealed class LsProgram
    {
        public string Path;
        public string Name;
        public string Comment;
        public bool IsMacro;
        public readonly List<ProgramCall> Calls = new List<ProgramCall>();
        public readonly List<IoReference> IoRefs = new List<IoReference>();
        public readonly List<IoReference> DataRefs = new List<IoReference>();
        public readonly List<LblReference> LblRefs = new List<LblReference>();
        public readonly List<RegisterDef> Positions = new List<RegisterDef>();

        public override string ToString()
        {
            string tag = IsMacro ? "  [Macro]" : "";
            if (string.IsNullOrEmpty(Comment)) return Name + tag;
            return Name + "  —  " + Comment + tag;
        }
    }

    public sealed class ProgramCall
    {
        public int LineNo;
        public int TpLine;
        public string Kind;
        public string Program;
        public string Raw;

        public string Display
        {
            get { return "L" + LineNo + "  " + Kind + " " + Program; }
        }
    }

    public sealed class IoReference
    {
        public int LineNo;
        public int TpLine;
        public string Kind;
        public string Number;
        public string Comment;
        public string Raw;

        public string Key
        {
            get { return (Kind ?? "").ToUpperInvariant() + "[" + Number + "]"; }
        }

        public string Display
        {
            get
            {
                string extra = "";
                if (!string.IsNullOrEmpty(Comment) &&
                    !string.Equals(Comment, Number, StringComparison.OrdinalIgnoreCase))
                    extra = ":" + Comment;
                return "L" + LineNo + "  " + Kind + "[" + Number + extra + "]";
            }
        }
    }

    public sealed class LblReference
    {
        public int LineNo;
        public int TpLine;
        public string Kind;
        public string LabelId;
        public string Comment;
        public string Raw;

        public string Key
        {
            get { return "LBL[" + LabelId + "]"; }
        }

        public string Display
        {
            get
            {
                string extra = string.IsNullOrEmpty(Comment) ? "" : ":" + Comment;
                return "L" + LineNo + "  " + Kind + " LBL[" + LabelId + extra + "]";
            }
        }
    }

    public sealed class CrossRefHit
    {
        public string ProgramName;
        public string FilePath;
        public int LineNo;
        public int TpLine;
        public string Kind;
        public string Symbol;
        public string Raw;

        public string ListLabel
        {
            get
            {
                string tp = TpLine > 0 ? TpLine.ToString() : "-";
                string snippet = (Raw ?? "").Trim();
                if (snippet.Length > 90) snippet = snippet.Substring(0, 87) + "...";
                return ProgramName + "  L" + LineNo + " (TP " + tp + ")  " + Kind + "  " + snippet;
            }
        }
    }

    public sealed class CursorSymbol
    {
        public string Kind;
        public string Symbol;
        public string Display;
    }

    public sealed class MacroEntry
    {
        public int Slot;
        public string Name;
        public string ProgName;
        public string AssignType;
        public int AssignId;
        public bool SystemMacro;

        public string KeyName
        {
            get { return NormalizeWs(Name); }
        }

        public string KeyProg
        {
            get { return (ProgName ?? "").Trim().ToUpperInvariant(); }
        }

        public string Display
        {
            get
            {
                string kind = SystemMacro ? "SYS" : (string.IsNullOrEmpty(AssignType) || AssignType == "--" ? "USR" : AssignType);
                string id = AssignId > 0 ? " " + kind + "[" + AssignId + "]" : "";
                return "[" + Slot.ToString().PadLeft(3) + "] " + Name + "  →  " + ProgName + id;
            }
        }

        public static string NormalizeWs(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool sp = false;
            foreach (char c in s.Trim().ToUpperInvariant())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!sp) { sb.Append(' '); sp = true; }
                }
                else { sb.Append(c); sp = false; }
            }
            return sb.ToString();
        }
    }

    public sealed class MacroUse
    {
        public MacroEntry Macro;
        public string ProgramName;
        public string FilePath;
        public int LineNo;
        public int TpLine;
        public string How;
        public string Raw;

        public string ListLabel
        {
            get
            {
                return ProgramName + "  L" + LineNo + "  " + How + "  " + (Raw ?? "").Trim();
            }
        }
    }
}
