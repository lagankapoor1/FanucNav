using System;
using System.Runtime.InteropServices;

namespace RGiesecke.DllExport
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class DllExportAttribute : Attribute
    {
        public CallingConvention CallingConvention { get; set; }
        public string ExportName { get; set; }

        public DllExportAttribute() { }

        public DllExportAttribute(CallingConvention callingConvention)
        {
            CallingConvention = callingConvention;
        }
    }
}
