using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FanucNav.Fanuc
{
    public sealed class CartPose
    {
        public string Name;
        public double X, Y, Z, W, P, R;
        public double[] Joints;
        public bool HasCart;
        public bool HasJoints;

        public override string ToString()
        {
            if (HasJoints) return Name + "  J";
            if (HasCart) return Name + "  XYZ " + X.ToString("0") + "," + Y.ToString("0") + "," + Z.ToString("0");
            return Name;
        }
    }

    public static class PosParser
    {
        private static readonly Regex Block = new Regex(
            @"P\[(\d+)\]\s*\{([\s\S]*?)\}",
            RegexOptions.Compiled);
        private static readonly Regex Xyz = new Regex(
            @"X\s*=\s*([-\d.*]+)\s*mm,\s*Y\s*=\s*([-\d.*]+)\s*mm,\s*Z\s*=\s*([-\d.*]+)\s*mm",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Wpr = new Regex(
            @"W\s*=\s*([-\d.*]+)\s*deg,\s*P\s*=\s*([-\d.*]+)\s*deg,\s*R\s*=\s*([-\d.*]+)\s*deg",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Jn = new Regex(
            @"J(\d)\s*=\s*([-\d.]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MotionRe = new Regex(
            @"\b([JL])\s+(P|PR)\s*\[\s*(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<CartPose> ExtractProgramPath(string lsText, IEnumerable<CartPose> known)
        {
            var map = new Dictionary<string, CartPose>(StringComparer.OrdinalIgnoreCase);
            if (known != null)
            {
                foreach (var k in known)
                    if (k != null && !string.IsNullOrEmpty(k.Name)) map[k.Name] = k;
            }
            foreach (var p in ParseLs(lsText))
                map[p.Name] = p;

            var ordered = new List<CartPose>();
            if (string.IsNullOrEmpty(lsText)) return ordered;
            foreach (Match m in MotionRe.Matches(lsText))
            {
                string name = m.Groups[2].Value.ToUpperInvariant() + "[" + m.Groups[3].Value + "]";
                CartPose p;
                if (!map.TryGetValue(name, out p)) continue;
                var copy = new CartPose();
                copy.Name = name + " " + m.Groups[1].Value.ToUpperInvariant();
                copy.X = p.X; copy.Y = p.Y; copy.Z = p.Z; copy.W = p.W; copy.P = p.P; copy.R = p.R;
                copy.HasCart = p.HasCart;
                copy.HasJoints = p.HasJoints;
                if (p.Joints != null)
                {
                    copy.Joints = new double[p.Joints.Length];
                    Array.Copy(p.Joints, copy.Joints, p.Joints.Length);
                }
                ordered.Add(copy);
            }
            if (ordered.Count == 0)
            {
                foreach (var kv in map) ordered.Add(kv.Value);
            }
            return ordered;
        }

        public static List<CartPose> ParseLs(string text)
        {
            var list = new List<CartPose>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in Block.Matches(text))
            {
                var p = new CartPose();
                p.Name = "P[" + m.Groups[1].Value + "]";
                string body = m.Groups[2].Value;
                var xyz = Xyz.Match(body);
                if (xyz.Success && !xyz.Groups[1].Value.Contains("*"))
                {
                    p.X = N(xyz.Groups[1].Value);
                    p.Y = N(xyz.Groups[2].Value);
                    p.Z = N(xyz.Groups[3].Value);
                    p.HasCart = true;
                    var w = Wpr.Match(body);
                    if (w.Success && !w.Groups[1].Value.Contains("*"))
                    {
                        p.W = N(w.Groups[1].Value);
                        p.P = N(w.Groups[2].Value);
                        p.R = N(w.Groups[3].Value);
                    }
                }
                var joints = new double[6];
                bool anyJ = false;
                foreach (Match j in Jn.Matches(body))
                {
                    int i = int.Parse(j.Groups[1].Value) - 1;
                    if (i >= 0 && i < 6)
                    {
                        joints[i] = N(j.Groups[2].Value);
                        anyJ = true;
                    }
                }
                if (anyJ) { p.Joints = joints; p.HasJoints = true; }
                if (p.HasCart || p.HasJoints) list.Add(p);
            }
            return list;
        }

        public static CartPose FromRegister(RegisterDef r)
        {
            if (r == null) return null;
            var p = new CartPose();
            p.Name = r.Key;
            if (!string.IsNullOrEmpty(r.Detail) && r.Detail.IndexOf("J1=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Joints = new double[6];
                foreach (Match j in Regex.Matches(r.Detail, @"J(\d)=([-\d.]+)"))
                {
                    int i = int.Parse(j.Groups[1].Value) - 1;
                    if (i >= 0 && i < 6) p.Joints[i] = N(j.Groups[2].Value);
                }
                p.HasJoints = true;
            }
            return p.HasJoints ? p : null;
        }

        private static double N(string s)
        {
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, out v)) return v;
            return 0;
        }
    }
}
