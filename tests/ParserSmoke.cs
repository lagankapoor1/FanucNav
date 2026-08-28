using System;
using System.IO;
using FanucNav.Fanuc;

namespace FanucNav.Tests
{
    internal static class ParserSmoke
    {
        public static int Run(string sampleDir)
        {
            int fails = 0;
            string ping = Path.Combine(sampleDir, "PINGRIP_TEMP.LS");
            if (!File.Exists(ping))
            {
                Console.WriteLine("SKIP: no sample at " + ping);
                return 0;
            }

            var prog = LsParser.ParseFile(ping);
            Expect(ref fails, prog.Name == "PINGRIP_TEMP", "program name", prog.Name);
            Expect(ref fails, prog.IsMacro, "PINGRIP is Macro subtype");
            Expect(ref fails, prog.LblRefs.Count > 5, "labels parsed", prog.LblRefs.Count.ToString());
            Expect(ref fails, prog.IoRefs.Exists(i => i.Key == "DI[804]"), "DI[804]");
            Expect(ref fails, prog.IoRefs.Exists(i => i.Key == "DO[801]"), "DO[801]");
            Expect(ref fails, prog.LblRefs.Exists(l => l.Kind == "JMP" && l.LabelId == "500"), "JMP LBL[500]");
            Expect(ref fails, prog.LblRefs.Exists(l => l.Kind == "TIMEOUT"), "TIMEOUT LBL");

            string line = "  13:  IF DI[804:di804PH02PX2]=OFF OR DI[808:x]=OFF,JMP LBL[500] ;";
            CursorSymbol sym;
            bool ok = LsParser.TryResolveAtPosition(line, line.IndexOf("804") + 1, out sym);
            Expect(ref fails, ok && sym.Kind == "IO" && sym.Symbol == "DI[804]", "cursor DI[804]", ok ? sym.Symbol : "none");

            ok = LsParser.TryResolveAtPosition(line, line.IndexOf("LBL") + 2, out sym);
            Expect(ref fails, ok && sym.Kind == "LBL" && sym.Symbol == "LBL[500]", "cursor LBL[500]", ok ? sym.Symbol : "none");

            string regLine = "  22:  R[402:Continue Last]=0    ;";
            ok = LsParser.TryResolveAtPosition(regLine, regLine.IndexOf("402") + 1, out sym);
            Expect(ref fails, ok && sym.Symbol == "R[402]", "cursor R[402]", ok ? sym.Symbol : "none");

            string alm = "  17:  UALM[3] ;";
            ok = LsParser.TryResolveAtPosition(alm, alm.IndexOf("UALM") + 1, out sym);
            Expect(ref fails, ok && sym.Symbol == "UALM[3]", "cursor UALM[3]", ok ? sym.Symbol : "none");

            string pos = "   7:J P[1] 50% FINE    ;";
            ok = LsParser.TryResolveAtPosition(pos, pos.IndexOf("P[") + 1, out sym);
            Expect(ref fails, ok && sym.Symbol == "P[1]", "cursor P[1]", ok ? sym.Symbol : "none");

            string utNum = "   5:  UTOOL_NUM=1 ;";
            ok = LsParser.TryResolveAtPosition(utNum, utNum.IndexOf("UTOOL") + 2, out sym);
            Expect(ref fails, ok && sym.Symbol == "UTOOL[1]", "UTOOL_NUM=1 → UTOOL[1]", ok ? sym.Symbol : "none");

            string ufNum = "   6:  UFRAME_NUM=0 ;";
            ok = LsParser.TryResolveAtPosition(ufNum, ufNum.IndexOf("UFRAME") + 2, out sym);
            Expect(ref fails, ok && sym.Symbol == "UFRAME[0]", "UFRAME_NUM=0 → UFRAME[0]", ok ? sym.Symbol : "none");

            string payNum = "   7:  PAYLOAD_NUM=2 ;";
            ok = LsParser.TryResolveAtPosition(payNum, payNum.IndexOf("PAYLOAD") + 2, out sym);
            Expect(ref fails, ok && sym.Symbol == "PAYLOAD[2]", "PAYLOAD_NUM=2 → PAYLOAD[2]", ok ? sym.Symbol : "none");

            string msg = "  14:  MESSAGE[MOVING TO REPAIR] ;";
            ok = LsParser.TryResolveAtPosition(msg, msg.IndexOf("MESSAGE") + 2, out sym);
            Expect(ref fails, ok && sym.Symbol == "MESSAGE[MOVING TO REPAIR]", "cursor MESSAGE text", ok ? sym.Symbol : "none");

            string ualm = " 275:  UALM[2] ;";
            ok = LsParser.TryResolveAtPosition(ualm, ualm.IndexOf("UALM") + 2, out sym);
            Expect(ref fails, ok && sym.Symbol == "UALM[2]", "cursor UALM[2]", ok ? sym.Symbol : "none");

            string drop = Path.Combine(sampleDir, "DCD_DROPB3.LS");
            if (File.Exists(drop))
            {
                var d = LsParser.ParseFile(drop);
                Expect(ref fails, d.Calls.Exists(c => c.Program == "DROPOFF1"), "CALL DROPOFF1");
                Expect(ref fails, d.Calls.Exists(c => c.Program == "DROPOFF3"), "CALL DROPOFF3");
            }

            int count;
            string ren = LsParser.RenumberMn(File.ReadAllText(ping), out count);
            Expect(ref fails, count > 20, "renumber step count", count.ToString());
            Expect(ref fails, ren.Contains("   1:"), "renumber starts at 1");

            int changed;
            string labs = LsParser.RenumberLabels(File.ReadAllText(ping), 10, 10, out changed);
            Expect(ref fails, labs.Contains("LBL[10"), "label start 10");

            var folder = RobotIndex.GuessRobotFolder(ping);
            var index = RobotIndex.Build(folder);
            Expect(ref fails, index.Files.Count >= 1, "index files", index.Files.Count.ToString());
            var refs = index.FindRefs("DI[804]", null);
            Expect(ref fails, refs.Count >= 1, "xref DI[804]", refs.Count.ToString());
            Expect(ref fails, index.FindRefs("UTOOL[1]", null).Count >= 1, "xref UTOOL[1] from UTOOL_NUM");
            Expect(ref fails, index.FindRefs("MESSAGE[MOVING TO REPAIR]", null).Count >= 1, "xref MESSAGE text");
            Expect(ref fails, index.FindRefs("UALM[2]", null).Count >= 1, "xref UALM[2]");

            string broken = File.ReadAllText(ping).Replace("  12:", "  99:");
            Expect(ref fails, LsParser.NeedsRenumber(broken), "needs renumber after skip");
            string withBlank = File.ReadAllText(ping);
            int nl = withBlank.IndexOf("\n  13:");
            if (nl > 0)
            {
                string inserted = withBlank.Insert(nl + 1, "\n");
                Expect(ref fails, LsParser.NeedsRenumber(inserted), "needs renumber after enter");
                int n2;
                string fixedUp = LsParser.RenumberMn(inserted, out n2);
                Expect(ref fails, !LsParser.NeedsRenumber(fixedUp), "renumber after enter is sequential");
            }

            string dg = Path.Combine(sampleDir, "MACRO.DG");
            if (File.Exists(dg))
            {
                var macros = MacroTable.ParseDg(File.ReadAllText(dg));
                Expect(ref fails, macros.Count > 5, "parse MACRO.DG", macros.Count.ToString());
                Expect(ref fails, macros.Exists(m => m.KeyProg == "SET_SEGM"), "SET SEGMENT in table");
                var setSeg = macros.Find(m => m.KeyProg == "SET_SEGM");
                Expect(ref fails, setSeg != null && LsParser.BodyUsesMacro("SET SEGMENT(30) ;", setSeg), "detect SET SEGMENT use");
                CursorSymbol macSym;
                bool macOk = LsParser.TryResolveAtPosition("  18:  SET SEGMENT(30) ;", 12, macros, out macSym);
                Expect(ref fails, macOk && macSym.Kind == "MACRO" && macSym.Symbol == "SET_SEGM",
                    "cursor SET SEGMENT → SET_SEGM", macOk ? macSym.Symbol : "none");
            }

            string numreg = Path.Combine(sampleDir, "NUMREG.VA");
            if (File.Exists(numreg))
            {
                var regs = RegTable.LoadNum(numreg, "R");
                Expect(ref fails, regs.Count > 10, "parse NUMREG.VA", regs.Count.ToString());
                Expect(ref fails, regs.Exists(r => r.Key == "R[1]" && r.Comment.IndexOf("Spot", StringComparison.OrdinalIgnoreCase) >= 0),
                    "R[1] comment from NUMREG");
            }
            string posreg = Path.Combine(sampleDir, "POSREG.VA");
            if (File.Exists(posreg))
            {
                var prs = RegTable.LoadPos(posreg);
                Expect(ref fails, prs.Exists(r => r.Key == "PR[1]" && r.Comment == "Home"), "PR[1] Home from POSREG");
                Expect(ref fails, prs.Exists(r => r.Key == "PR[1]" && r.Axes.ContainsKey("J1")), "PR[1] has J1");
                Expect(ref fails, prs.Exists(r => r.Key == "PR[2]" && r.Comment == "Home2" && r.Value == "uninit"), "named uninit PR[2] kept");
                var home = prs.Find(r => r.Key == "PR[1]");
                string pose = home != null ? RegTable.FormatPose(home) : "";
                Expect(ref fails, pose.IndexOf("J1") >= 0 && pose.IndexOf("J6") >= 0, "PR pose lists J1–J6");
            }

            string sysframe = Path.Combine(sampleDir, "SYSFRAME.VA");
            if (File.Exists(sysframe))
            {
                var frames = RegTable.LoadFrames(sysframe);
                Expect(ref fails, frames.Exists(r => r.Key == "UTOOL[1]" && r.Axes.ContainsKey("X")), "UTOOL[1] XYZ from SYSFRAME");
                var ut1 = frames.Find(r => r.Key == "UTOOL[1]");
                Expect(ref fails, ut1 != null && ut1.Axes["X"].IndexOf("363") >= 0, "UTOOL[1] X ≈ -363");
                Expect(ref fails, frames.Exists(r => r.Key == "UFRAME[1]" && r.Axes.ContainsKey("X")), "UFRAME[1] from SYSFRAME");
                Expect(ref fails, frames.Exists(r => r.Key == "UTOOL[1]" && r.Comment != null && r.Comment.IndexOf("selected", StringComparison.OrdinalIgnoreCase) >= 0),
                    "UTOOL[1] marked selected");
            }

            string cb = Path.Combine(sampleDir, "CBPARAM.VA");
            if (File.Exists(cb))
            {
                var pays = RegTable.LoadPayloads(cb);
                Expect(ref fails, pays.Exists(r => r.Key == "PAYLOAD[1]" && r.Axes.ContainsKey("MASS")), "PAYLOAD[1] mass");
                Expect(ref fails, pays.Exists(r => r.Key == "PAYLOAD[2]"), "PAYLOAD[2] present");
                var p1 = pays.Find(r => r.Key == "PAYLOAD[1]");
                string pd = p1 != null ? RegTable.FormatPose(p1) : "";
                Expect(ref fails, pd.IndexOf("Mass", StringComparison.OrdinalIgnoreCase) >= 0 && pd.IndexOf("kg") >= 0, "payload detail lists mass");
            }

            string mov = Path.Combine(sampleDir, "MOV_REPR.LS");
            if (File.Exists(mov))
            {
                var mp = LsParser.ParseFile(mov);
                Expect(ref fails, mp.Positions.Count >= 3, "MOV_REPR /POS count", mp.Positions.Count.ToString());
                Expect(ref fails, mp.Positions.Exists(r => r.Key == "P[1]@MOV_REPR" && r.Axes.ContainsKey("X")), "P[1]@MOV_REPR has XYZ");
                var p1 = mp.Positions.Find(r => r.Number == "1");
                Expect(ref fails, p1 != null && p1.Axes["X"].IndexOf("960") >= 0, "P[1] X ≈ 960");
                Expect(ref fails, p1 != null && p1.Uf == "0" && p1.Ut == "1", "P[1] UF/UT");
                Expect(ref fails, mp.DataRefs.Exists(d => d.Key == "UTOOL[1]"), "UTOOL_NUM=1 collected as UTOOL[1]");
                Expect(ref fails, mp.DataRefs.Exists(d => d.Kind == "MESSAGE" && d.Number.IndexOf("MOVING TO REPAIR", StringComparison.OrdinalIgnoreCase) >= 0),
                    "MESSAGE[MOVING TO REPAIR]");
            }

            string dropb = Path.Combine(sampleDir, "DCD_DROPB3.LS");
            if (File.Exists(dropb))
            {
                var db = LsParser.ParseFile(dropb);
                Expect(ref fails, db.DataRefs.Exists(d => d.Key == "UALM[2]"), "UALM[2] in DCD_DROPB3");
                Expect(ref fails, db.DataRefs.Exists(d => d.Kind == "MESSAGE"), "MESSAGE in DCD_DROPB3");
            }

            string all = Path.Combine(sampleDir, "SYSFRAME.VA");
            if (File.Exists(all) && File.Exists(Path.Combine(sampleDir, "POSREG.VA")))
            {
                var loaded = RegTable.LoadFromFolder(sampleDir);
                Expect(ref fails, loaded.Exists(r => r.Kind == "UFRAME"), "folder load includes UFRAME");
                Expect(ref fails, loaded.Exists(r => r.Kind == "UTOOL"), "folder load includes UTOOL");
                Expect(ref fails, loaded.Exists(r => r.Kind == "PAYLOAD"), "folder load includes PAYLOAD");
            }

            string ver = Path.Combine(sampleDir, "VERSION.DG");
            if (File.Exists(ver))
            {
                var id = RobotIdent.FromFolder(sampleDir);
                Expect(ref fails, id.Model.IndexOf("2000", StringComparison.OrdinalIgnoreCase) >= 0, "VERSION.DG model", id.Model);
                Expect(ref fails, id.Software.IndexOf("Spot", StringComparison.OrdinalIgnoreCase) >= 0, "VERSION.DG software", id.Software);
                Expect(ref fails, id.Header.IndexOf("·") >= 0 || id.Header.Length > 8, "robot header", id.Header);
            }

            Expect(ref fails, index.MissingCallTargets().Count >= 1, "missing CALL targets listed",
                index.MissingCallTargets().Count.ToString());
            Expect(ref fails, index.EntryPrograms().Count + index.UnusedPrograms().Count >= 1, "call-tree roots exist");

            string pns = Path.Combine(sampleDir, "PNS0001.LS");
            if (File.Exists(pns))
            {
                var pn = LsParser.ParseFile(pns);
                Expect(ref fails, ProgramMap.IsSelectorName("PNS0001"), "PNS0001 is a selector");
                Expect(ref fails, ProgramMap.IsSelectorName("RSR0001"), "RSR0001 is a selector");
                Expect(ref fails, ProgramMap.IsSelectorName("STYLE01"), "STYLE01 is a selector");
                var map = ProgramMap.Build(pn, File.ReadAllText(pns), 0, null);
                Expect(ref fails, map.Children.Exists(c => c.Kind == "LBL" && c.Target == "99"), "PNS map has LBL[99]");
                bool sawJmp = false, sawCall = false;
                foreach (var sec in map.Children)
                {
                    foreach (var st in sec.Children)
                    {
                        if (st.Kind == "JMP" && st.Target == "99") sawJmp = true;
                        if (st.Kind == "CALL" && st.Target != null && st.Target.StartsWith("PART_")) sawCall = true;
                    }
                }
                Expect(ref fails, sawJmp, "PNS map JMP LBL[99]");
                Expect(ref fails, sawCall, "PNS map CALL PART_…");
            }

            string siLine = "  10:  IF SI[1]=ON,JMP LBL[1] ;";
            ok = LsParser.TryResolveAtPosition(siLine, siLine.IndexOf("SI") + 1, out sym);
            Expect(ref fails, ok && sym.Symbol == "SI[1]", "cursor SI[1]", ok ? sym.Symbol : "none");
            string soLine = "  11:  SO[2:Safety]=ON ;";
            ok = LsParser.TryResolveAtPosition(soLine, soLine.IndexOf("SO") + 1, out sym);
            Expect(ref fails, ok && sym.Symbol == "SO[2]", "cursor SO[2]", ok ? sym.Symbol : "none");

            Expect(ref fails, ProgramMap.ProgramForSelector("RSR[1]") == "RSR0001", "RSR[1] → RSR0001");
            Expect(ref fails, ProgramMap.ProgramForSelector("PNS[12]") == "PNS0012", "PNS[12] → PNS0012");
            ok = LsParser.TryResolveAtPosition("  4:  RSR[1] ;", 8, out sym);
            Expect(ref fails, ok && sym.Symbol == "RSR[1]", "cursor RSR[1]", ok ? sym.Symbol : "none");

            var dead = new LsProgram();
            dead.Name = "DEAD";
            var deadMap = ProgramMap.Build(dead, "/MN\r\n   1:  LBL[1] ;\r\n   2:  JMP LBL[2] ;\r\n", 0, null);
            Expect(ref fails, deadMap.Children.Exists(c => c.Kind == "LBL" && c.Flag == "UNUSED"), "unused LBL[1]");
            bool missJmp = false;
            foreach (var sec in deadMap.Children)
                foreach (var st in sec.Children)
                    if (st.Kind == "JMP" && st.Flag == "MISSING") missJmp = true;
            Expect(ref fails, missJmp, "JMP LBL[2] missing");

            if (File.Exists(pns))
            {
                var pn = LsParser.ParseFile(pns);
                var table = ProgramMap.ExtractSelectTable(pn, File.ReadAllText(pns));
                Expect(ref fails, table.Count >= 8, "PNS style table rows", table.Count.ToString());
                Expect(ref fails, table.Exists(r => r.Target == "PART_0137"), "GI=1 → PART_0137");
                string txt = ProgramMap.ToText(ProgramMap.Build(pn, File.ReadAllText(pns), 0, null), table);
                Expect(ref fails, txt.IndexOf("LBL[99]") >= 0 && txt.IndexOf("PART_0137") >= 0, "export map text");
            }

            var same = BackupCompare.Compare(index, index);
            Expect(ref fails, !same.Exists(d => d.Kind == "PROG"), "compare same backup has no program add/remove");

            var diRe = FanucNav.PluginInfrastructure.NppEditor.SymbolSearchRegex("DI[804]");
            Expect(ref fails, diRe != null && diRe.IsMatch("  13:  IF DI[804:di804PH02PX2]=OFF,JMP LBL[500] ;"), "highlight regex DI[804]");
            var utRe = FanucNav.PluginInfrastructure.NppEditor.SymbolSearchRegex("UTOOL[1]");
            Expect(ref fails, utRe != null && utRe.IsMatch("   5:  UTOOL_NUM=1 ;"), "highlight regex UTOOL_NUM");
            var msgRe = FanucNav.PluginInfrastructure.NppEditor.SymbolSearchRegex("MESSAGE[MOVING TO REPAIR]");
            Expect(ref fails, msgRe != null && msgRe.IsMatch("  14:  MESSAGE[MOVING TO REPAIR] ;"), "highlight regex MESSAGE");

            Console.WriteLine(fails == 0 ? "ParserSmoke: ALL PASSED" : "ParserSmoke: " + fails + " FAILED");
            return fails;
        }

        private static void Expect(ref int fails, bool cond, string name, string detail = null)
        {
            if (cond) Console.WriteLine("  OK  " + name);
            else
            {
                fails++;
                Console.WriteLine("  FAIL " + name + (detail != null ? " [" + detail + "]" : ""));
            }
        }
    }
}
