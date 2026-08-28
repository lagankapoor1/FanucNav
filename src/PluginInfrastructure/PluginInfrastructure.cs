using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace FanucNav.PluginInfrastructure
{
    public static class Constants
    {
        public const int WM_USER = 0x400;
        public const int NPPMSG = WM_USER + 1000;
        public const int RUNCOMMAND_USER = WM_USER + 3000;
        public const int MAX_PATH = 260;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NppData
    {
        public IntPtr _nppHandle;
        public IntPtr _scintillaMainHandle;
        public IntPtr _scintillaSecondHandle;
    }

    public delegate void NppFuncItemDelegate();

    [StructLayout(LayoutKind.Sequential)]
    public struct ShortcutKey
    {
        public ShortcutKey(bool isCtrl, bool isAlt, bool isShift, Keys key)
        {
            _isCtrl = Convert.ToByte(isCtrl);
            _isAlt = Convert.ToByte(isAlt);
            _isShift = Convert.ToByte(isShift);
            _key = Convert.ToByte(key);
        }

        public byte _isCtrl;
        public byte _isAlt;
        public byte _isShift;
        public byte _key;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FuncItem
    {
        public const int MAX_FUNC_ITEM_NAME_LENGTH = 63;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_FUNC_ITEM_NAME_LENGTH + 1)]
        public string _itemName;
        public NppFuncItemDelegate _pFunc;
        public int _cmdID;
        public bool _init2Check;
        public ShortcutKey _pShKey;
    }

    public sealed class FuncItems : IDisposable
    {
        private readonly List<FuncItem> _funcItems = new List<FuncItem>();
        private readonly int _sizeFuncItem = Marshal.SizeOf(typeof(FuncItem));
        private IntPtr _nativePointer = IntPtr.Zero;
        private bool _disposed;

        [DllImport("kernel32")]
        private static extern void RtlMoveMemory(IntPtr destination, IntPtr source, int length);

        public void Add(FuncItem funcItem)
        {
            int oldSize = _funcItems.Count * _sizeFuncItem;
            _funcItems.Add(funcItem);
            int newSize = _funcItems.Count * _sizeFuncItem;
            IntPtr newPointer = Marshal.AllocHGlobal(newSize);

            if (_nativePointer != IntPtr.Zero)
            {
                RtlMoveMemory(newPointer, _nativePointer, oldSize);
                Marshal.FreeHGlobal(_nativePointer);
            }

            IntPtr pos = (IntPtr)(newPointer.ToInt64() + oldSize);
            byte[] nameBytes = Encoding.Unicode.GetBytes(funcItem._itemName + "\0");
            Marshal.Copy(nameBytes, 0, pos, nameBytes.Length);
            pos = (IntPtr)(pos.ToInt64() + 128);

            IntPtr fn = funcItem._pFunc != null ? Marshal.GetFunctionPointerForDelegate(funcItem._pFunc) : IntPtr.Zero;
            Marshal.WriteIntPtr(pos, fn);
            pos = (IntPtr)(pos.ToInt64() + IntPtr.Size);
            Marshal.WriteInt32(pos, funcItem._cmdID);
            pos = (IntPtr)(pos.ToInt64() + 4);
            Marshal.WriteInt32(pos, Convert.ToInt32(funcItem._init2Check));
            pos = (IntPtr)(pos.ToInt64() + 4);

            if (funcItem._pShKey._key != 0)
            {
                IntPtr keyPtr = Marshal.AllocHGlobal(4);
                Marshal.StructureToPtr(funcItem._pShKey, keyPtr, false);
                Marshal.WriteIntPtr(pos, keyPtr);
            }
            else
            {
                Marshal.WriteIntPtr(pos, IntPtr.Zero);
            }

            _nativePointer = newPointer;
        }

        public void RefreshItems()
        {
            IntPtr pos = _nativePointer;
            for (int i = 0; i < _funcItems.Count; i++)
            {
                FuncItem updated = _funcItems[i];
                pos = (IntPtr)(pos.ToInt64() + 128);
                pos = (IntPtr)(pos.ToInt64() + IntPtr.Size);
                updated._cmdID = Marshal.ReadInt32(pos);
                pos = (IntPtr)(pos.ToInt64() + 4);
                pos = (IntPtr)(pos.ToInt64() + 4);
                pos = (IntPtr)(pos.ToInt64() + IntPtr.Size);
                _funcItems[i] = updated;
            }
        }

        public IntPtr NativePointer { get { return _nativePointer; } }
        public List<FuncItem> Items { get { return _funcItems; } }

        public void Dispose()
        {
            if (_disposed) return;
            if (_nativePointer != IntPtr.Zero) Marshal.FreeHGlobal(_nativePointer);
            _disposed = true;
        }
    }

    [Flags]
    public enum NppTbMsg : uint
    {
        DWS_ICONTAB = 0x00000001,
        DWS_ICONBAR = 0x00000002,
        DWS_ADDINFO = 0x00000004,
        DWS_DF_CONT_LEFT = 0 << 28,
        DWS_DF_CONT_RIGHT = 1 << 28,
        DWS_DF_CONT_TOP = 2 << 28,
        DWS_DF_CONT_BOTTOM = 3 << 28,
        DWS_DF_FLOATING = 0x80000000
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class NppTbData
    {
        public IntPtr hClient;
        public string pszName;
        public int dlgID;
        public NppTbMsg uMask;
        public uint hIconTab;
        public string pszAddInfo;
        public RECT rcFloat;
        public int iPrevCont;
        public string pszModuleName;
    }

    public enum NppMsg : uint
    {
        NPPMSG = Constants.NPPMSG,
        NPPM_GETCURRENTSCINTILLA = Constants.NPPMSG + 4,
        NPPM_MODELESSDIALOG = Constants.NPPMSG + 12,
        NPPM_DMMSHOW = Constants.NPPMSG + 30,
        NPPM_DMMHIDE = Constants.NPPMSG + 31,
        NPPM_DMMUPDATEDISPINFO = Constants.NPPMSG + 32,
        NPPM_DMMREGASDCKDLG = Constants.NPPMSG + 33,
        NPPM_SWITCHTOFILE = Constants.NPPMSG + 37,
        NPPM_GETPLUGINSCONFIGDIR = Constants.NPPMSG + 46,
        NPPM_SETSTATUSBAR = Constants.NPPMSG + 24,
        NPPM_GETMENUHANDLE = Constants.NPPMSG + 25,
        NPPMAINMENU = 1,
        NPPM_MENUCOMMAND = Constants.NPPMSG + 48,
        NPPM_DOOPEN = Constants.NPPMSG + 77,
        NPPM_ADDTOOLBARICON = Constants.NPPMSG + 41,
        NPPM_ADDTOOLBARICON_FORDARKMODE = Constants.NPPMSG + 101,
        NPPM_GETFULLCURRENTPATH = Constants.RUNCOMMAND_USER + 1,
        NPPM_GETCURRENTDIRECTORY = Constants.RUNCOMMAND_USER + 2,
        NPPM_GETFILENAME = Constants.RUNCOMMAND_USER + 3,
        NPPN_FIRST = 1000,
        NPPN_READY = 1001,
        NPPN_TBMODIFICATION = 1002,
        NPPN_SHUTDOWN = 1009,
        NPPN_BUFFERACTIVATED = 1010
    }

    public enum SciMsg : uint
    {
        SCI_GETLENGTH = 2006,
        SCI_GETCURRENTPOS = 2008,
        SCI_GOTOLINE = 2024,
        SCI_SETSEL = 2160,
        SCI_GETLINECOUNT = 2154,
        SCI_GETLINE = 2153,
        SCI_LINEFROMPOSITION = 2166,
        SCI_POSITIONFROMLINE = 2167,
        SCI_LINELENGTH = 2350,
        SCI_GETCOLUMN = 2129,
        SCI_GETTEXT = 2182,
        SCI_SETTEXT = 2181,
        SCI_BEGINUNDOACTION = 2078,
        SCI_ENDUNDOACTION = 2079,
        SCI_ENSUREVISIBLE = 2232,
        SCI_SCROLLCARET = 2169,
        SCI_GETCODEPAGE = 2137,
        SCI_GOTOPOS = 2025,
        SCI_SETVIEWWS = 2020,
        SCI_SETVIEWEOL = 2355,
        SCI_GETVIEWEOL = 2356,
        SCI_INDICSETSTYLE = 2080,
        SCI_INDICSETFORE = 2082,
        SCI_INDICSETUNDER = 2510,
        SCI_INDICSETALPHA = 2523,
        SCI_SETINDICATORCURRENT = 2500,
        SCI_INDICATORFILLRANGE = 2504,
        SCI_INDICATORCLEARRANGE = 2505,
        SCN_DOUBLECLICK = 2006,
        SCN_UPDATEUI = 2007,
        SCN_MODIFIED = 2008
    }

    public static class SciMod
    {
        public const int INSERTTEXT = 0x01;
        public const int DELETETEXT = 0x02;
        public const int UNDO = 0x20;
        public const int REDO = 0x40;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SciNotifyHeader
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public uint Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ScNotification
    {
        public SciNotifyHeader Header;
        public IntPtr position;
        public int ch;
        public int modifiers;
        public int modificationType;
        public IntPtr text;
        public IntPtr length;
        public IntPtr linesAdded;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public IntPtr line;
        public int foldLevelNow;
        public int foldLevelPrev;
        public int margin;
        public int listType;
        public int x;
        public int y;
        public int token;
        public IntPtr annotationLinesAdded;
        public int updated;
        public int listCompletionMethod;
        public int characterSource;
    }

    public static class Win32
    {
        public const int MAX_PATH = 260;

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, int lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, out int lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lParam);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        [DllImport("user32", CharSet = CharSet.Ansi)]
        public static extern IntPtr SendMessageA(IntPtr hWnd, uint msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPStr)] StringBuilder lParam);

        [DllImport("user32", CharSet = CharSet.Ansi)]
        public static extern IntPtr SendMessageA(IntPtr hWnd, uint msg, IntPtr wParam, byte[] lParam);

        [DllImport("user32")]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref ToolbarIconsWithDarkMode lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct ToolbarIconsWithDarkMode
        {
            public IntPtr hToolbarBmp;
            public IntPtr hToolbarIcon;
            public IntPtr hToolbarIconDarkMode;
        }

        [DllImport("user32")]
        public static extern IntPtr GetMenu(IntPtr hWnd);

        [DllImport("user32")]
        public static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32")]
        public static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        [DllImport("user32")]
        public static extern uint GetMenuState(IntPtr hMenu, uint uId, uint uFlags);

        public const uint MF_BYCOMMAND = 0;
        public const uint MF_CHECKED = 0x00000008;
        public const uint MF_DISABLED = 0x00000002;

        public const uint IDM_VIEW_ALL_CHARACTERS = 44019;
        public const uint IDM_VIEW_TAB_SPACE = 44025;
        public const uint IDM_VIEW_EOL = 44026;
        public const uint IDM_VIEW_NPC = 44130;
        public const uint IDM_VIEW_NPC_CCUNIEOL = 44131;

        public const uint SCI_CLEARREPRESENTATION = 2770;
        public const uint SCI_CLEARALLREPRESENTATIONS = 2773;

        public static bool MenuCommandChecked(IntPtr hMenu, uint cmdId)
        {
            if (hMenu == IntPtr.Zero) return false;
            uint state = GetMenuState(hMenu, cmdId, MF_BYCOMMAND);
            if (state != 0xFFFFFFFF && (state & MF_CHECKED) != 0)
                return true;
            int count = GetMenuItemCount(hMenu);
            for (int i = 0; i < count; i++)
            {
                IntPtr sub = GetSubMenu(hMenu, i);
                if (sub != IntPtr.Zero && MenuCommandChecked(sub, cmdId))
                    return true;
            }
            return false;
        }
    }

    internal static class PluginBase
    {
        internal static NppData nppData;
        internal static readonly FuncItems _funcItems = new FuncItems();

        internal static void SetCommand(int index, string commandName, NppFuncItemDelegate functionPointer)
        {
            SetCommand(index, commandName, functionPointer, new ShortcutKey(), false);
        }

        internal static void SetCommand(int index, string commandName, NppFuncItemDelegate functionPointer, ShortcutKey shortcut)
        {
            SetCommand(index, commandName, functionPointer, shortcut, false);
        }

        internal static void SetCommand(int index, string commandName, NppFuncItemDelegate functionPointer, ShortcutKey shortcut, bool checkOnInit)
        {
            var item = new FuncItem();
            item._cmdID = index;
            item._itemName = commandName;
            if (functionPointer != null)
                item._pFunc = functionPointer;
            item._pShKey = shortcut;
            item._init2Check = checkOnInit;
            _funcItems.Add(item);
        }

        internal static IntPtr GetCurrentScintilla()
        {
            int cur;
            Win32.SendMessage(nppData._nppHandle, (uint)NppMsg.NPPM_GETCURRENTSCINTILLA, 0, out cur);
            return cur == 0 ? nppData._scintillaMainHandle : nppData._scintillaSecondHandle;
        }
    }

    internal static class NppEditor
    {
        public static IntPtr Sci { get { return PluginBase.GetCurrentScintilla(); } }
        public static IntPtr Npp { get { return PluginBase.nppData._nppHandle; } }

        public static string GetCurrentPath()
        {
            var sb = new StringBuilder(Win32.MAX_PATH * 4);
            Win32.SendMessage(Npp, (uint)NppMsg.NPPM_GETFULLCURRENTPATH, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }

        public static string GetConfigDirectory()
        {
            int len = Win32.SendMessage(Npp, (uint)NppMsg.NPPM_GETPLUGINSCONFIGDIR, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var sb = new StringBuilder(Math.Max(len + 2, 260));
            Win32.SendMessage(Npp, (uint)NppMsg.NPPM_GETPLUGINSCONFIGDIR, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }

        public static bool OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            IntPtr switched = Win32.SendMessage(Npp, (uint)NppMsg.NPPM_SWITCHTOFILE, IntPtr.Zero, path);
            if (switched != IntPtr.Zero) return true;
            return Win32.SendMessage(Npp, (uint)NppMsg.NPPM_DOOPEN, IntPtr.Zero, path) != IntPtr.Zero;
        }

        public static int GetLength()
        {
            return Win32.SendMessage(Sci, (uint)SciMsg.SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        public static string GetText()
        {
            int len = GetLength();
            var buf = new byte[len + 1];
            Win32.SendMessageA(Sci, (uint)SciMsg.SCI_GETTEXT, (IntPtr)(len + 1), buf);
            return Encoding.Default.GetString(buf, 0, len);
        }

        public static void SetText(string text)
        {
            byte[] bytes = Encoding.Default.GetBytes(text + "\0");
            Win32.SendMessageA(Sci, (uint)SciMsg.SCI_SETTEXT, IntPtr.Zero, bytes);
        }

        public static int GetCurrentPos()
        {
            return Win32.SendMessage(Sci, (uint)SciMsg.SCI_GETCURRENTPOS, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }

        public static int LineFromPosition(int pos)
        {
            return Win32.SendMessage(Sci, (uint)SciMsg.SCI_LINEFROMPOSITION, (IntPtr)pos, IntPtr.Zero).ToInt32();
        }

        public static int PositionFromLine(int line)
        {
            return Win32.SendMessage(Sci, (uint)SciMsg.SCI_POSITIONFROMLINE, (IntPtr)line, IntPtr.Zero).ToInt32();
        }

        public static int GetColumn(int pos)
        {
            return Win32.SendMessage(Sci, (uint)SciMsg.SCI_GETCOLUMN, (IntPtr)pos, IntPtr.Zero).ToInt32();
        }

        public static string GetLine(int line)
        {
            int len = Win32.SendMessage(Sci, (uint)SciMsg.SCI_LINELENGTH, (IntPtr)line, IntPtr.Zero).ToInt32();
            if (len <= 0) return string.Empty;
            var buf = new byte[len + 1];
            Win32.SendMessageA(Sci, (uint)SciMsg.SCI_GETLINE, (IntPtr)line, buf);
            return Encoding.Default.GetString(buf, 0, len).TrimEnd('\r', '\n', '\0');
        }

        public static void GotoLine(int zeroBasedLine)
        {
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_ENSUREVISIBLE, (IntPtr)zeroBasedLine, IntPtr.Zero);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_GOTOLINE, (IntPtr)zeroBasedLine, IntPtr.Zero);
            int pos = PositionFromLine(zeroBasedLine);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETSEL, (IntPtr)pos, (IntPtr)pos);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
        }

        public static void BeginUndo()
        {
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_BEGINUNDOACTION, IntPtr.Zero, IntPtr.Zero);
        }

        public static void EndUndo()
        {
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_ENDUNDOACTION, IntPtr.Zero, IntPtr.Zero);
        }

        public static void ShowDock(IntPtr formHandle)
        {
            Win32.SendMessage(Npp, (uint)NppMsg.NPPM_DMMSHOW, IntPtr.Zero, formHandle);
        }

        public static void HideDock(IntPtr formHandle)
        {
            Win32.SendMessage(Npp, (uint)NppMsg.NPPM_DMMHIDE, IntPtr.Zero, formHandle);
        }

        public static void RegisterModeless(IntPtr formHandle)
        {
            Win32.SendMessage(Npp, (uint)NppMsg.NPPM_MODELESSDIALOG, (IntPtr)0, formHandle);
        }

        public static void HideEolMarkers()
        {
            try
            {
                Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETVIEWEOL, IntPtr.Zero, IntPtr.Zero);
                Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETVIEWWS, IntPtr.Zero, IntPtr.Zero);
                Win32.SendMessage(Sci, Win32.SCI_CLEARALLREPRESENTATIONS, IntPtr.Zero, IntPtr.Zero);

                IntPtr menu = Win32.SendMessage(Npp, (uint)NppMsg.NPPM_GETMENUHANDLE, (IntPtr)NppMsg.NPPMAINMENU, IntPtr.Zero);
                if (menu == IntPtr.Zero) menu = Win32.GetMenu(Npp);
                UncheckView(menu, Win32.IDM_VIEW_EOL);
                UncheckView(menu, Win32.IDM_VIEW_NPC);
                UncheckView(menu, Win32.IDM_VIEW_NPC_CCUNIEOL);
                UncheckView(menu, Win32.IDM_VIEW_ALL_CHARACTERS);
                UncheckView(menu, Win32.IDM_VIEW_TAB_SPACE);

                Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETVIEWEOL, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        private static void UncheckView(IntPtr menu, uint cmd)
        {
            if (menu != IntPtr.Zero && Win32.MenuCommandChecked(menu, cmd))
                Win32.SendMessage(Npp, (uint)NppMsg.NPPM_MENUCOMMAND, IntPtr.Zero, (int)cmd);
        }

        public static void ClearEolStatus()
        {
            try
            {
                Win32.SendMessage(Npp, (uint)NppMsg.NPPM_SETSTATUSBAR, (IntPtr)3, " ");
            }
            catch { }
        }

        public static void SetCaret(int pos)
        {
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_GOTOPOS, (IntPtr)pos, IntPtr.Zero);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETSEL, (IntPtr)pos, (IntPtr)pos);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_SCROLLCARET, IntPtr.Zero, IntPtr.Zero);
        }

        public const int HighlightIndicator = 8;

        public static void HighlightSymbol(string symbol)
        {
            try
            {
                EnsureHighlightStyle();
                ClearHighlights();
                if (string.IsNullOrEmpty(symbol)) return;
                string text = GetText();
                if (string.IsNullOrEmpty(text)) return;
                var re = SymbolSearchRegex(symbol);
                if (re == null) return;
                Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETINDICATORCURRENT, (IntPtr)HighlightIndicator, IntPtr.Zero);
                int n = 0;
                foreach (System.Text.RegularExpressions.Match m in re.Matches(text))
                {
                    if (!m.Success || m.Length <= 0) continue;
                    Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICATORFILLRANGE, (IntPtr)m.Index, (IntPtr)m.Length);
                    n++;
                    if (n > 400) break;
                }
            }
            catch { }
        }

        public static void ClearHighlights()
        {
            try
            {
                int len = GetLength();
                if (len <= 0) return;
                Win32.SendMessage(Sci, (uint)SciMsg.SCI_SETINDICATORCURRENT, (IntPtr)HighlightIndicator, IntPtr.Zero);
                Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICATORCLEARRANGE, IntPtr.Zero, (IntPtr)len);
            }
            catch { }
        }

        private static void EnsureHighlightStyle()
        {
            const int roundBox = 7;
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICSETSTYLE, (IntPtr)HighlightIndicator, roundBox);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICSETFORE, (IntPtr)HighlightIndicator, 0x086CA8);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICSETUNDER, (IntPtr)HighlightIndicator, 1);
            Win32.SendMessage(Sci, (uint)SciMsg.SCI_INDICSETALPHA, (IntPtr)HighlightIndicator, 70);
        }

        public static System.Text.RegularExpressions.Regex SymbolSearchRegex(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            string s = symbol.Trim();
            try
            {
                int br = s.IndexOf('[');
                if (br > 0 && s.EndsWith("]"))
                {
                    string kind = System.Text.RegularExpressions.Regex.Escape(s.Substring(0, br));
                    string inner = s.Substring(br + 1, s.Length - br - 2);
                    string num = System.Text.RegularExpressions.Regex.Escape(inner);
                    string body = kind + @"\s*\[\s*" + num + @"\s*(:[^\]]*)?\]";
                    string upper = kind.ToUpperInvariant();
                    if (upper == "UFRAME")
                        body = "(?:" + body + @"|UFRAME_NUM\s*=\s*" + num + ")";
                    else if (upper == "UTOOL")
                        body = "(?:" + body + @"|UTOOL_NUM\s*=\s*" + num + ")";
                    else if (upper == "PAYLOAD")
                        body = "(?:" + body + @"|PAYLOAD_NUM\s*=\s*" + num + ")";
                    else if (upper == "LBL")
                        body = @"(?:JMP\s+)?LBL\s*\[\s*" + num + @"\s*(:[^\]]*)?\]";
                    return new System.Text.RegularExpressions.Regex(body, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
                string name = System.Text.RegularExpressions.Regex.Escape(s);
                return new System.Text.RegularExpressions.Regex(@"\b(?:CALL|RUN)\s+" + name + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return null;
            }
        }
    }
}
