using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FanucNav.Fanuc
{
    public sealed class RobotIndex
    {
        public string Root { get; private set; }
        public RobotIdent Ident = new RobotIdent();
        public readonly Dictionary<string, LsProgram> Programs = new Dictionary<string, LsProgram>(StringComparer.OrdinalIgnoreCase);
        public readonly List<LsProgram> Files = new List<LsProgram>();
        public readonly Dictionary<string, List<CrossRefHit>> CrossRefs = new Dictionary<string, List<CrossRefHit>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<MacroEntry> Macros = new List<MacroEntry>();
        public readonly List<MacroUse> MacroUses = new List<MacroUse>();
        public readonly List<RegisterDef> Registers = new List<RegisterDef>();
        public readonly Dictionary<string, RegisterDef> RegisterMap = new Dictionary<string, RegisterDef>(StringComparer.OrdinalIgnoreCase);

        public static RobotIndex Build(string robotFolder)
        {
            var index = new RobotIndex();
            index.Root = robotFolder;
            if (string.IsNullOrEmpty(robotFolder)) return index;

            if (File.Exists(robotFolder) && robotFolder.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                index.IndexZip(robotFolder);
            }
            else if (Directory.Exists(robotFolder))
            {
                foreach (string path in EnumerateLsFiles(robotFolder))
                {
                    try { index.AddProgram(LsParser.ParseFile(path)); }
                    catch { }
                }
                index.Macros.AddRange(MacroTable.LoadFromFolder(robotFolder));
                index.ScanMacroUses();
                index.LoadRegisters(robotFolder);
                try { index.Ident = RobotIdent.FromFolder(robotFolder); }
                catch { index.Ident = new RobotIdent(); }
            }

            return index;
        }

        public static string GuessRobotFolder(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "";
            try
            {
                string dir = File.Exists(filePath) ? Path.GetDirectoryName(filePath) : filePath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return dir ?? "";

                string current = Path.GetFullPath(dir);
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(current); i++)
                {
                    int ls = 0;
                    try
                    {
                        ls = Directory.GetFiles(current, "*.LS", SearchOption.TopDirectoryOnly).Length
                           + Directory.GetFiles(current, "*.ls", SearchOption.TopDirectoryOnly).Length;
                    }
                    catch { }

                    if (ls >= 5) return current;

                    string parent = Directory.GetParent(current) != null ? Directory.GetParent(current).FullName : null;
                    if (string.IsNullOrEmpty(parent) || parent == current) break;
                    current = parent;
                }
                return Path.GetDirectoryName(filePath) ?? "";
            }
            catch
            {
                return Path.GetDirectoryName(filePath) ?? "";
            }
        }

        public LsProgram Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            LsProgram prog;
            if (Programs.TryGetValue(name, out prog)) return prog;
            if (Programs.TryGetValue(Path.GetFileNameWithoutExtension(name), out prog)) return prog;
            return null;
        }

        public string ResolvePath(string name)
        {
            var p = Resolve(name);
            return p != null ? p.Path : null;
        }

        public List<CrossRefHit> FindRefs(string symbol, string limitToFilePath)
        {
            var result = new List<CrossRefHit>();
            if (string.IsNullOrEmpty(symbol)) return result;

            string key = NormalizeSymbolKey(symbol);
            List<CrossRefHit> hits;
            if (CrossRefs.TryGetValue(key, out hits))
            {
                foreach (var h in hits)
                {
                    if (!string.IsNullOrEmpty(limitToFilePath) &&
                        !string.Equals(h.FilePath, limitToFilePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.Add(h);
                }
            }
            return result;
        }

        public List<ProgramCall> CallersOf(string programName)
        {
            var list = new List<ProgramCall>();
            if (string.IsNullOrEmpty(programName)) return list;
            string want = programName.ToUpperInvariant();
            foreach (var prog in Files)
            {
                foreach (var call in prog.Calls)
                {
                    if (string.Equals(call.Program, want, StringComparison.OrdinalIgnoreCase))
                    {
                        var copy = new ProgramCall();
                        copy.LineNo = call.LineNo;
                        copy.TpLine = call.TpLine;
                        copy.Kind = call.Kind;
                        copy.Program = prog.Name;
                        copy.Raw = call.Raw;
                        list.Add(copy);
                    }
                }
            }
            return list;
        }

        public bool IsCalled(string programName)
        {
            return CallersOf(programName).Count > 0;
        }

        public List<LsProgram> EntryPrograms()
        {
            var list = new List<LsProgram>();
            foreach (var p in Files)
            {
                if (!IsCalled(p.Name) && p.Calls.Count > 0)
                    list.Add(p);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public List<LsProgram> UnusedPrograms()
        {
            var list = new List<LsProgram>();
            foreach (var p in Files)
            {
                if (!IsCalled(p.Name) && p.Calls.Count == 0)
                    list.Add(p);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public List<LsProgram> SelectorPrograms()
        {
            var list = new List<LsProgram>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Files)
            {
                if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                if (ProgramMap.IsSelectorName(p.Name) && seen.Add(p.Name))
                    list.Add(p);
            }
            foreach (var mac in Macros)
            {
                if (!ProgramMap.IsSelectorAssign(mac.AssignType)) continue;
                var p = Resolve(mac.ProgName);
                if (p != null && seen.Add(p.Name))
                    list.Add(p);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public List<string> MissingCallTargets()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Files)
            {
                foreach (var c in p.Calls)
                {
                    if (string.IsNullOrEmpty(c.Program)) continue;
                    if (Resolve(c.Program) == null)
                        set.Add(c.Program.ToUpperInvariant());
                }
            }
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public List<MacroUse> UsesOf(MacroEntry mac)
        {
            var list = new List<MacroUse>();
            if (mac == null) return list;
            foreach (var u in MacroUses)
            {
                if (u.Macro == mac) list.Add(u);
            }
            return list;
        }

        private void LoadRegisters(string folder)
        {
            Registers.Clear();
            RegisterMap.Clear();
            foreach (var r in RegTable.LoadFromFolder(folder))
                AddRegister(r);
            foreach (var prog in Files)
            {
                foreach (var r in prog.Positions)
                {
                    if (string.IsNullOrEmpty(r.Source))
                        r.Source = prog.Name;
                    if (string.IsNullOrEmpty(r.Comment) && !string.IsNullOrEmpty(prog.Comment))
                        r.Comment = prog.Comment;
                    AddRegister(r);
                }
                ApplyComments(prog.IoRefs);
                ApplyComments(prog.DataRefs);
            }
        }

        private void AddRegister(RegisterDef r)
        {
            if (r == null || string.IsNullOrEmpty(r.Kind)) return;
            Registers.Add(r);
            if (!RegisterMap.ContainsKey(r.Key))
                RegisterMap[r.Key] = r;
            if (r.Key != r.BaseKey && !RegisterMap.ContainsKey(r.BaseKey))
                RegisterMap[r.BaseKey] = r;
        }

        private void ApplyComments(List<IoReference> refs)
        {
            if (refs == null) return;
            foreach (var io in refs)
            {
                RegisterDef def;
                if (!RegisterMap.TryGetValue(io.Key, out def)) continue;
                if (string.IsNullOrEmpty(io.Comment) && !string.IsNullOrEmpty(def.Comment))
                    io.Comment = def.Comment;
            }
        }

        public RegisterDef FindRegister(string key)
        {
            return FindRegister(key, null);
        }

        public RegisterDef FindRegister(string key, string programName)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string norm = NormalizeSymbolKey(key);
            RegisterDef d;
            if (!string.IsNullOrEmpty(programName))
            {
                string spec = norm;
                if (spec.IndexOf('@') < 0)
                    spec = spec + "@" + programName;
                if (RegisterMap.TryGetValue(spec, out d)) return d;
            }
            if (RegisterMap.TryGetValue(norm, out d)) return d;
            if (norm.IndexOf('@') > 0)
            {
                string baseKey = norm.Substring(0, norm.IndexOf('@'));
                if (RegisterMap.TryGetValue(baseKey, out d)) return d;
            }
            return null;
        }

        public List<IoReference> AllDataInProgram(LsProgram prog)
        {
            var list = new List<IoReference>();
            if (prog == null) return list;
            list.AddRange(prog.IoRefs);
            list.AddRange(prog.DataRefs);
            return list;
        }

        public MacroEntry FindMacroBySymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            string key = MacroEntry.NormalizeWs(symbol);
            foreach (var m in Macros)
            {
                if (m.KeyName == key || m.KeyProg == key) return m;
            }
            return null;
        }

        private void ScanMacroUses()
        {
            MacroUses.Clear();
            if (Macros.Count == 0) return;
            foreach (var prog in Files)
            {
                if (string.IsNullOrEmpty(prog.Path) || !File.Exists(prog.Path)) continue;
                string text;
                try { text = File.ReadAllText(prog.Path); }
                catch { continue; }
                foreach (var line in LsParser.EnumerateMnLines(text))
                {
                    foreach (var mac in Macros)
                    {
                        if (!LsParser.BodyUsesMacro(line.Body, mac)) continue;
                        var use = new MacroUse();
                        use.Macro = mac;
                        use.ProgramName = prog.Name;
                        use.FilePath = prog.Path;
                        use.LineNo = line.FileLine;
                        use.TpLine = line.TpLine;
                        use.How = LsParser.HowUsesMacro(line.Body, mac);
                        use.Raw = line.Raw;
                        MacroUses.Add(use);
                        var hit = new CrossRefHit
                        {
                            ProgramName = prog.Name,
                            FilePath = prog.Path,
                            LineNo = line.FileLine,
                            TpLine = line.TpLine,
                            Kind = "MACRO",
                            Symbol = mac.Name,
                            Raw = line.Raw
                        };
                        if (!string.IsNullOrEmpty(mac.KeyName)) AddHit(mac.KeyName, hit);
                        if (!string.IsNullOrEmpty(mac.KeyProg)) AddHit(mac.KeyProg, hit);
                    }
                }
            }
        }

        public string NormalizeSymbolKey(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return "";
            string s = symbol.Trim().ToUpperInvariant();
            s = s.Replace(" ", "");
            if (s.StartsWith("JMP"))
            {
                int i = s.IndexOf("LBL[");
                if (i >= 0) s = s.Substring(i);
            }
            int colon = s.IndexOf(':');
            if (colon > 0 && s.EndsWith("]"))
                s = s.Substring(0, colon) + "]";
            return s;
        }

        private void AddProgram(LsProgram prog)
        {
            if (prog == null || string.IsNullOrEmpty(prog.Name)) return;
            Files.Add(prog);
            Programs[prog.Name] = prog;
            AddCrossRefs(prog);
        }

        private void AddCrossRefs(LsProgram prog)
        {
            foreach (var call in prog.Calls)
            {
                AddHit(call.Program, new CrossRefHit
                {
                    ProgramName = prog.Name,
                    FilePath = prog.Path,
                    LineNo = call.LineNo,
                    TpLine = call.TpLine,
                    Kind = call.Kind,
                    Symbol = call.Program,
                    Raw = call.Raw
                });
            }

            foreach (var io in prog.IoRefs)
                AddDataHit(prog, io);
            foreach (var io in prog.DataRefs)
                AddDataHit(prog, io);

            foreach (var lbl in prog.LblRefs)
            {
                AddHit(lbl.Key, new CrossRefHit
                {
                    ProgramName = prog.Name,
                    FilePath = prog.Path,
                    LineNo = lbl.LineNo,
                    TpLine = lbl.TpLine,
                    Kind = lbl.Kind,
                    Symbol = lbl.Key,
                    Raw = lbl.Raw
                });
            }
        }

        private void AddDataHit(LsProgram prog, IoReference io)
        {
            var hit = new CrossRefHit
            {
                ProgramName = prog.Name,
                FilePath = prog.Path,
                LineNo = io.LineNo,
                TpLine = io.TpLine,
                Kind = io.Kind,
                Symbol = io.Key,
                Raw = io.Raw
            };
            AddHit(io.Key, hit);
            string sel = ProgramMap.ProgramForSelector(io.Key);
            if (!string.IsNullOrEmpty(sel))
                AddHit(sel, hit);
        }

        private void AddHit(string key, CrossRefHit hit)
        {
            key = NormalizeSymbolKey(key);
            if (string.IsNullOrEmpty(key)) return;
            List<CrossRefHit> list;
            if (!CrossRefs.TryGetValue(key, out list))
            {
                list = new List<CrossRefHit>();
                CrossRefs[key] = list;
            }
            list.Add(hit);
        }

        private void IndexZip(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.EndsWith(".LS", StringComparison.OrdinalIgnoreCase) &&
                        !entry.FullName.EndsWith(".ls", StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream, System.Text.Encoding.Default))
                        {
                            string text = reader.ReadToEnd();
                            string virtualPath = zipPath + "::" + entry.FullName.Replace('/', '\\');
                            AddProgram(LsParser.Parse(virtualPath, text));
                        }
                    }
                    catch { }
                }
            }
        }

        private static IEnumerable<string> EnumerateLsFiles(string root)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(root);
            int dirs = 0;
            while (stack.Count > 0 && dirs < 80)
            {
                string dir = stack.Pop();
                dirs++;
                string[] files = new string[0];
                try { files = Directory.GetFiles(dir, "*.LS"); }
                catch { continue; }

                foreach (string f in files)
                    if (found.Add(f)) yield return f;

                try
                {
                    foreach (string child in Directory.GetDirectories(dir))
                    {
                        string name = Path.GetFileName(child);
                        if (name.StartsWith(".")) continue;
                        if (name.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("MD", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("FR", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("UD1", StringComparison.OrdinalIgnoreCase) ||
                            dirs < 8)
                            stack.Push(child);
                    }
                }
                catch { }
            }
        }
    }
}
