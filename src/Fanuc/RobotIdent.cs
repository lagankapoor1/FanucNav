using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public sealed class RobotIdent
    {
        public string Model = "Unknown";
        public string Software = "";
        public string Version = "";
        public string DcsVersion = "";
        public string Controller = "";
        public double ReachMm = 2000;
        public double PayloadKg;
        public DhTable Dh = DhTable.Generic();

        public string Header
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(Model) && Model != "Unknown") parts.Add(Model);
                if (!string.IsNullOrEmpty(Software)) parts.Add(Software);
                if (!string.IsNullOrEmpty(Version)) parts.Add(Version);
                if (parts.Count == 0) return "FANUC robot backup";
                return string.Join("  ·  ", parts.ToArray());
            }
        }

        public override string ToString()
        {
            return Header;
        }

        public static RobotIdent FromFolder(string folder)
        {
            var id = new RobotIdent();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return id;

            string ver = Path.Combine(folder, "VERSION.DG");
            if (File.Exists(ver))
                ParseVersion(File.ReadAllText(ver, Encoding.Default), id);

            string zone = Path.Combine(folder, "DCS_ZONE.LS");
            if (File.Exists(zone) && id.Model == "Unknown")
            {
                var m = Regex.Match(File.ReadAllText(zone, Encoding.Default),
                    @"ROBOT MODEL:\s*(\S+)", RegexOptions.IgnoreCase);
                if (m.Success) id.Model = m.Groups[1].Value.Replace('_', ' ');
            }

            id.Dh = DhTable.ForModel(id.Model);
            id.ReachMm = id.Dh.ReachMm;
            return id;
        }

        private static void ParseVersion(string text, RobotIdent id)
        {
            var sw = Regex.Match(text, @"SOFTWARE:\s*\S+\s*\r?\n\s*([A-Za-z][A-Za-z0-9+ \-]*)", RegexOptions.Multiline);
            if (sw.Success) id.Software = sw.Groups[1].Value.Trim();
            else
            {
                var sw2 = Regex.Match(text, @"\b(SpotTool\+|HandlingTool|PaintTool|DispenseTool|ArcTool|PalletTool)\b",
                    RegexOptions.IgnoreCase);
                if (sw2.Success) id.Software = sw2.Groups[1].Value;
            }
            var ver = Regex.Match(text, @"Software Edition No\.\s*:\s*(\S+)");
            if (ver.Success) id.Version = ver.Groups[1].Value.Trim();
            else
            {
                var v2 = Regex.Match(text, @"\$VERSION:\s*(\S+)");
                if (v2.Success) id.Version = v2.Groups[1].Value.Trim();
            }
            var dcs = Regex.Match(text, @"DCS\s*:\s*(\S+)");
            if (dcs.Success) id.DcsVersion = dcs.Groups[1].Value;
            var cid = Regex.Match(text, @"Controller ID\s*:\s*(\S+)");
            if (cid.Success) id.Controller = cid.Groups[1].Value.Trim();
            var pers = Regex.Match(text, @"Default Personality[^\r\n]*\r?\n\s*(\S[^\r\n]*?)\s+V\d", RegexOptions.IgnoreCase);
            if (pers.Success) id.Model = pers.Groups[1].Value.Trim();
            else
            {
                var m = Regex.Match(text, @"(R-?\d{4}i[A-Z]?/[A-Za-z0-9\-]+|M-?\d+i[A-Z]?/[A-Za-z0-9\-]+|LR Mate[^\r\n]*)",
                    RegexOptions.IgnoreCase);
                if (m.Success) id.Model = m.Groups[1].Value.Trim();
            }
        }
    }

    public sealed class DhTable
    {
        public string Name;
        public double D1, A1, A2, A3, D4, D6;
        public double ReachMm;
        public double[] Jmin = new double[] { -180, -60, -80, -360, -125, -360 };
        public double[] Jmax = new double[] { 180, 76, 245, 360, 125, 360 };

        public static DhTable Generic()
        {
            return new DhTable { Name = "Generic 6-axis", D1 = 525, A1 = 150, A2 = 790, A3 = 250, D4 = 835, D6 = 100, ReachMm = 1800 };
        }

        public static DhTable ForModel(string model)
        {
            string s = (model ?? "").ToUpperInvariant().Replace(" ", "").Replace("_", "");
            if (s.Contains("270F") || s.Contains("270F-IF") || s.Contains("270FIF"))
                return new DhTable { Name = "R-2000iC/270F", D1 = 670, A1 = 312, A2 = 1075, A3 = 225, D4 = 1280, D6 = 240, ReachMm = 2655 };
            if (s.Contains("210F") || s.Contains("210L"))
                return new DhTable { Name = "R-2000iC/210F", D1 = 670, A1 = 312, A2 = 1075, A3 = 225, D4 = 1280, D6 = 215, ReachMm = 2655 };
            if (s.Contains("165F") || s.Contains("165R"))
                return new DhTable { Name = "R-2000iC/165F", D1 = 670, A1 = 312, A2 = 1075, A3 = 225, D4 = 1280, D6 = 215, ReachMm = 2655 };
            if (s.Contains("R2000") || s.Contains("R-2000"))
                return new DhTable { Name = "R-2000iC family", D1 = 670, A1 = 312, A2 = 1075, A3 = 225, D4 = 1280, D6 = 215, ReachMm = 2655 };
            if (s.Contains("M710") || s.Contains("M-710"))
                return new DhTable { Name = "M-710iC", D1 = 650, A1 = 150, A2 = 870, A3 = 170, D4 = 1016, D6 = 175, ReachMm = 2050 };
            if (s.Contains("M20") || s.Contains("M-20"))
                return new DhTable { Name = "M-20iA/iD", D1 = 525, A1 = 150, A2 = 790, A3 = 150, D4 = 860, D6 = 90, ReachMm = 1813 };
            if (s.Contains("LRMATE") || s.Contains("LR-MATE"))
                return new DhTable { Name = "LR Mate", D1 = 330, A1 = 75, A2 = 300, A3 = 75, D4 = 320, D6 = 80, ReachMm = 700 };
            return Generic();
        }
    }

    public static class Kinematics
    {
        public static double[,] Fk(DhTable dh, double[] deg)
        {
            var T = Identity();
            T = Mul(T, RotZ(Rad(deg[0])));
            T = Mul(T, Trans(0, 0, dh.D1));
            T = Mul(T, RotX(-Math.PI / 2));
            T = Mul(T, Trans(dh.A1, 0, 0));
            T = Mul(T, RotZ(Rad(deg[1])));
            T = Mul(T, Trans(dh.A2, 0, 0));
            T = Mul(T, RotZ(Rad(deg[2])));
            T = Mul(T, Trans(dh.A3, 0, 0));
            T = Mul(T, RotX(-Math.PI / 2));
            T = Mul(T, Trans(0, 0, dh.D4));
            T = Mul(T, RotZ(Rad(deg[3])));
            T = Mul(T, RotX(Math.PI / 2));
            T = Mul(T, RotZ(Rad(deg[4])));
            T = Mul(T, RotX(-Math.PI / 2));
            T = Mul(T, RotZ(Rad(deg[5])));
            T = Mul(T, Trans(0, 0, dh.D6));
            return T;
        }

        public static void JointOrigins(DhTable dh, double[] deg, out double[] x, out double[] y, out double[] z)
        {
            x = new double[7];
            y = new double[7];
            z = new double[7];
            var acc = Identity();
            Store(acc, 0, x, y, z);
            acc = Mul(acc, RotZ(Rad(deg[0])));
            acc = Mul(acc, Trans(0, 0, dh.D1));
            Store(acc, 1, x, y, z);
            acc = Mul(acc, RotX(-Math.PI / 2));
            acc = Mul(acc, Trans(dh.A1, 0, 0));
            acc = Mul(acc, RotZ(Rad(deg[1])));
            acc = Mul(acc, Trans(dh.A2, 0, 0));
            Store(acc, 2, x, y, z);
            acc = Mul(acc, RotZ(Rad(deg[2])));
            acc = Mul(acc, Trans(dh.A3, 0, 0));
            acc = Mul(acc, RotX(-Math.PI / 2));
            acc = Mul(acc, Trans(0, 0, dh.D4));
            Store(acc, 3, x, y, z);
            acc = Mul(acc, RotZ(Rad(deg[3])));
            acc = Mul(acc, RotX(Math.PI / 2));
            Store(acc, 4, x, y, z);
            acc = Mul(acc, RotZ(Rad(deg[4])));
            acc = Mul(acc, RotX(-Math.PI / 2));
            Store(acc, 5, x, y, z);
            acc = Mul(acc, RotZ(Rad(deg[5])));
            acc = Mul(acc, Trans(0, 0, dh.D6));
            Store(acc, 6, x, y, z);
        }

        public static bool IkXyz(DhTable dh, double x, double y, double z, double[] seed, out double[] joints)
        {
            joints = new double[6];
            if (seed != null && seed.Length >= 6)
                Array.Copy(seed, joints, 6);

            double j1 = Math.Atan2(y, x);
            double r = Math.Sqrt(x * x + y * y) - dh.A1;
            double zz = z - dh.D1;
            double l2 = dh.A2;
            double l3 = Math.Sqrt(dh.A3 * dh.A3 + dh.D4 * dh.D4);
            double dist = Math.Sqrt(r * r + zz * zz);
            if (dist < 1 || dist > l2 + l3 - 1)
                return false;

            double c3 = (dist * dist - l2 * l2 - l3 * l3) / (2 * l2 * l3);
            if (c3 > 1) c3 = 1;
            if (c3 < -1) c3 = -1;
            double s3 = -Math.Sqrt(1 - c3 * c3);
            double j3 = Math.Atan2(s3, c3);
            double j2 = Math.Atan2(zz, r) - Math.Atan2(l3 * s3, l2 + l3 * c3);

            joints[0] = Deg(j1);
            joints[1] = Deg(j2);
            joints[2] = Deg(j3);
            joints[3] = seed != null ? seed[3] : 0;
            joints[4] = seed != null ? seed[4] : 0;
            joints[5] = seed != null ? seed[5] : 0;
            return true;
        }

        private static void Store(double[,] T, int i, double[] x, double[] y, double[] z)
        {
            x[i] = T[0, 3];
            y[i] = T[1, 3];
            z[i] = T[2, 3];
        }

        private static double Rad(double d) { return d * Math.PI / 180.0; }
        private static double Deg(double r) { return r * 180.0 / Math.PI; }

        private static double[,] Identity()
        {
            return new double[,] { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
        }

        private static double[,] Trans(double x, double y, double z)
        {
            var T = Identity();
            T[0, 3] = x; T[1, 3] = y; T[2, 3] = z;
            return T;
        }

        private static double[,] RotZ(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new double[,] { { c, -s, 0, 0 }, { s, c, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };
        }

        private static double[,] RotX(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new double[,] { { 1, 0, 0, 0 }, { 0, c, -s, 0 }, { 0, s, c, 0 }, { 0, 0, 0, 1 } };
        }

        private static double[,] Mul(double[,] A, double[,] B)
        {
            var R = new double[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    R[i, j] = A[i, 0] * B[0, j] + A[i, 1] * B[1, j] + A[i, 2] * B[2, j] + A[i, 3] * B[3, j];
            return R;
        }

        public static double ParseNum(string s)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, out v)) return v;
            return 0;
        }
    }
}
