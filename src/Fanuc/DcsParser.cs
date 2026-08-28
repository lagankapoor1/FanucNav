using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public sealed class DcsZone
    {
        public int Number;
        public bool Enabled;
        public string Status = "";
        public string Comment = "";
        public string Method = "";
        public string StopType = "";
        public int Group = 1;
        public double X1, Y1, Z1, X2, Y2, Z2;
        public double CurX, CurY, CurZ;
        public bool HasBox;

        public string Display
        {
            get
            {
                return (Enabled ? "EN  " : "dis ") + "CPC[" + Number + "]  " + Comment +
                       "  " + Status + (HasBox ? "  box" : "");
            }
        }
    }

    public sealed class DcsElement
    {
        public int Model;
        public int Index;
        public string Shape = "";
        public double Size;
        public int Link = 99;
        public bool Enabled;
        public double X1, Y1, Z1, X2, Y2, Z2;
        public string Comment = "";
    }

    public sealed class DcsConfig
    {
        public string Version = "";
        public readonly List<DcsZone> Zones = new List<DcsZone>();
        public readonly List<DcsElement> Elements = new List<DcsElement>();
        public string UserModelName = "";

        public static DcsConfig Load(string folder)
        {
            var cfg = new DcsConfig();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return cfg;
            string dg = Path.Combine(folder, "DCSVRFY.DG");
            if (!File.Exists(dg)) dg = Path.Combine(folder, "DCSCHGD1.DG");
            if (File.Exists(dg))
                ParseVerify(File.ReadAllText(dg, Encoding.Default), cfg);
            return cfg;
        }

        public static void ParseVerify(string text, DcsConfig cfg)
        {
            var ver = Regex.Match(text, @"DCS Version:\s*(\S+)");
            if (ver.Success) cfg.Version = ver.Groups[1].Value;

            foreach (Match m in Regex.Matches(text,
                @"^\s*(\d+)\s+(ENABLE|DISABLE)\s+(\d+)\s+\S+\s+(\S+)\s+\[([^\]]*)\]",
                RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                var z = GetZone(cfg, int.Parse(m.Groups[1].Value));
                z.Enabled = m.Groups[2].Value.StartsWith("EN", StringComparison.OrdinalIgnoreCase);
                z.Group = int.Parse(m.Groups[3].Value);
                z.Status = m.Groups[4].Value.Trim();
                z.Comment = m.Groups[5].Value.Trim();
            }

            var detail = Regex.Matches(text, @"No\.\s+(\d+)\s+Status:(\S+)");
            for (int di = 0; di < detail.Count; di++)
            {
                Match dm = detail[di];
                int n = int.Parse(dm.Groups[1].Value);
                var z = GetZone(cfg, n);
                z.Status = dm.Groups[2].Value.Trim();
                int start = dm.Index;
                int end = di + 1 < detail.Count ? detail[di + 1].Index : Math.Min(text.Length, start + 1400);
                string block = text.Substring(start, end - start);
                var c = Regex.Match(block, @"Comment:\s*\[([^\]]*)\]");
                if (c.Success && !string.IsNullOrWhiteSpace(c.Groups[1].Value))
                    z.Comment = c.Groups[1].Value.Trim();
                var method = Regex.Match(block, @"Method\(Safe side\):\s*(\S.+\S)");
                if (method.Success) z.Method = method.Groups[1].Value.Trim();
                var stop = Regex.Match(block, @"Stop type:\s*(\S.+\S)");
                if (stop.Success) z.StopType = stop.Groups[1].Value.Trim();
                var rowX = Regex.Match(block, @"X\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)");
                var rowY = Regex.Match(block, @"Y\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)");
                var rowZ = Regex.Match(block, @"Z\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)");
                if (rowX.Success && rowY.Success && rowZ.Success)
                {
                    z.CurX = Num(rowX.Groups[1].Value);
                    z.X1 = Num(rowX.Groups[2].Value);
                    z.X2 = Num(rowX.Groups[3].Value);
                    z.CurY = Num(rowY.Groups[1].Value);
                    z.Y1 = Num(rowY.Groups[2].Value);
                    z.Y2 = Num(rowY.Groups[3].Value);
                    z.CurZ = Num(rowZ.Groups[1].Value);
                    z.Z1 = Num(rowZ.Groups[2].Value);
                    z.Z2 = Num(rowZ.Groups[3].Value);
                    z.HasBox = true;
                }
            }

            var um = Regex.Match(text, @"User model[\s\S]{0,200}?No\.\s+1\s+\[([^\]]+)\]", RegexOptions.IgnoreCase);
            if (um.Success) cfg.UserModelName = um.Groups[1].Value.Trim();

            foreach (Match em in Regex.Matches(text,
                @"Element:\s+(\d+)[\s\S]{0,500}?Shape:\s+(\S+)[\s\S]{0,80}?Size \(mm\):\s+([\d.]+)[\s\S]{0,80}?X:\s+([-\d.]+)\s+Y:\s+([-\d.]+)\s+Z:\s+([-\d.]+)[\s\S]{0,80}?X:\s+([-\d.]+)\s+Y:\s+([-\d.]+)\s+Z:\s+([-\d.]+)",
                RegexOptions.IgnoreCase))
            {
                var e = new DcsElement();
                e.Model = 1;
                e.Index = int.Parse(em.Groups[1].Value);
                e.Shape = em.Groups[2].Value;
                e.Size = Num(em.Groups[3].Value);
                e.X1 = Num(em.Groups[4].Value);
                e.Y1 = Num(em.Groups[5].Value);
                e.Z1 = Num(em.Groups[6].Value);
                e.X2 = Num(em.Groups[7].Value);
                e.Y2 = Num(em.Groups[8].Value);
                e.Z2 = Num(em.Groups[9].Value);
                e.Enabled = true;
                e.Comment = cfg.UserModelName;
                cfg.Elements.Add(e);
            }
        }

        private static DcsZone GetZone(DcsConfig cfg, int n)
        {
            foreach (var z in cfg.Zones)
                if (z.Number == n) return z;
            var nz = new DcsZone { Number = n };
            cfg.Zones.Add(nz);
            return nz;
        }

        private static double Num(string s)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, out v)) return v;
            return 0;
        }
    }
}
