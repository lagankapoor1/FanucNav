using System;

namespace FanucNav.Tests
{
    internal static class SmokeMain
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : "samples";
            return ParserSmoke.Run(dir);
        }
    }
}
