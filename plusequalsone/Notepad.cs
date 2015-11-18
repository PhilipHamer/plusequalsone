using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Timer = System.Threading.Timer;

namespace plusequalsone
{
    class Notepad
    {
        private static readonly int _startupWin32Err;
        private static readonly IntPtr _nativeLib;
        private static readonly NativeGoo.HookProc _hookProc;
        private static readonly uint _hookMsg;
        private static readonly uint _setNotepadIdMsg;
        private static readonly uint _pingMsg;
        private static readonly uint _checkWasHookedMsg;

        static Notepad()
        {
            _nativeLib = NativeGoo.LoadLibrary(Environment.Is64BitOperatingSystem ? "pe1inj.dll" : "pe1inj32.dll");
            if (_nativeLib == IntPtr.Zero)
            {
                _startupWin32Err = Marshal.GetLastWin32Error();
                return;
            }
            IntPtr hookProcAddr = NativeGoo.GetProcAddress(_nativeLib, "HookProc");
            if (hookProcAddr == IntPtr.Zero)
            {
                _startupWin32Err = Marshal.GetLastWin32Error();
                NativeGoo.FreeLibrary(_nativeLib);
                return;
            }
            _hookProc = (NativeGoo.HookProc)Marshal.GetDelegateForFunctionPointer(hookProcAddr, typeof(NativeGoo.HookProc));
            _hookMsg = NativeGoo.RegisterWindowMessage("WM_PE1_HOOK");
            _setNotepadIdMsg = NativeGoo.RegisterWindowMessage("WM_PE1_SETNOTEPADID");
            _pingMsg = NativeGoo.RegisterWindowMessage("WM_PE1_PING");
            _checkWasHookedMsg = NativeGoo.RegisterWindowMessage("WM_PE1_CHECKWASHOOKED");
        }

        public static bool CheckStartupSuccess(out string message)
        {
            if (_startupWin32Err == 0)
            {
                message = string.Empty;
                return true;
            }
            message = string.Format("{0} failed ({1})", _nativeLib == IntPtr.Zero ? "LoadLibrary" : "GetProcAddress", _startupWin32Err);
            return false;
        }

        public static Notepad Create(int pid)
        {
            return Create(pid, null, null);
        }

        public static Notepad Create(int pid, Func<int, SlowOpenAction> slowOpenWarning, Func<int, SlowOpenAction> reallySlowOpenWarning)
        {
            return Program.NotepadController.RegisterNewNotepad(new Notepad(pid, slowOpenWarning, reallySlowOpenWarning));
        }

        public enum SlowOpenAction
        {
            ContinueWaiting,
            StopWaiting,
            AbortProcess
        }

        public class SlowOpenException : ApplicationException
        {
        }

        public class NotepadDeadException : ApplicationException
        {
        }

        private Notepad(int pid, Func<int, SlowOpenAction> slowOpenWarning, Func<int, SlowOpenAction> reallySlowOpenWarning)
        {
            _pid = pid;

            _timerIdle = new Timer(IdleCallback, null, Timeout.Infinite, Timeout.Infinite);
            _timerForce = new Timer(ForceCallback, null, Timeout.Infinite, Timeout.Infinite);

            Process proc = Process.GetProcessById(pid);
            if (!proc.WaitForInputIdle(3000))
            {
                if (slowOpenWarning != null)
                {
                    SlowOpenAction soa = slowOpenWarning(_pid);
                    if (soa == SlowOpenAction.AbortProcess)
                    {
                        proc.Kill();
                    }
                    if (soa != SlowOpenAction.ContinueWaiting)
                    {
                        throw new SlowOpenException();
                    }
                }
                if (!proc.WaitForInputIdle(7000))
                {
                    if (reallySlowOpenWarning != null)
                    {
                        SlowOpenAction soa = reallySlowOpenWarning(_pid);
                        if (soa == SlowOpenAction.AbortProcess)
                        {
                            proc.Kill();
                        }
                        if (soa != SlowOpenAction.ContinueWaiting)
                        {
                            throw new SlowOpenException();
                        }
                    }
                    proc.WaitForInputIdle();
                }
            }
            _notepadId = MakeNotepadId(proc);
            proc.Exited += ProcOnExited;
            proc.EnableRaisingEvents = true;
            _mainWnd = proc.MainWindowHandle;
        }

        public static int MakeNotepadId(Process proc)
        {
            return MakeNotepadId(proc.Id, proc.StartTime);
        }

        public static int MakeNotepadId(int pid, DateTime timestamp)
        {
            int notepadId = (pid << 16) | (timestamp.Second*1000 + timestamp.Millisecond);
            return notepadId;
        }

        private void ProcOnExited(object sender, EventArgs eventArgs)
        {
            if (_hooked) Program.NotepadController.OnProcessExit(this, ((Process) sender).ExitCode);
        }

        private int _pid;
        private int _notepadId;
        private IntPtr _mainWnd;
        private readonly Timer _timerIdle;
        private readonly Timer _timerForce;
        private bool _resetForce = true;
        private bool _hooked = false;
        private readonly object _filelock = new object();

        private const int TIMER_IDLE_INTERVAL = 3000;
        private const int TIMER_FORCE_INTERVAL = 30000;
        private static readonly IntPtr ACK = new IntPtr(104);

        public object FileLock
        {
            get { return _filelock; }
        }

        public int Pid
        {
            get { return _pid; }
            set { _pid = value; }
        }

        public int NotepadId
        {
            get { return _notepadId; }
            set
            {
                _notepadId = value;
                if (ACK != NativeGoo.SendMessage(_mainWnd, _setNotepadIdMsg, new IntPtr(_notepadId), IntPtr.Zero))
                {
                    Program.NotepadController.Log("failed to set notepad id " + _notepadId, _pid);
                }
            }
        }

        private IntPtr EditWnd
        {
            get
            {
                return NativeGoo.FindWindowEx(_mainWnd, IntPtr.Zero, "Edit", null);
            }
        }

        private bool IsWindowAlive
        {
            get { return NativeGoo.IsWindow(_mainWnd) && NativeGoo.IsWindow(EditWnd); }
        }

        // we use SetWindowsHookEx to inject our dll, as inspired by Robert Kuster: http://www.codeproject.com/Articles/4610/Three-Ways-to-Inject-Your-Code-into-Another-Proces

        public void InstallHook(IntPtr thisWnd)
        {
            if (Environment.Is64BitOperatingSystem && DangerousPebHack.Is64BitChecker.IsWow64Process(_pid))
            {
                Process proc = Process.Start(new ProcessStartInfo()
                {
                    FileName = "pe1wow.exe",
                    Arguments = string.Format("{0} {1}", (int) _mainWnd.ToInt64(), (int) thisWnd.ToInt64()),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (!proc.WaitForExit(30000))
                {
                    proc.Kill();
                    throw new ApplicationException("failed to hook wow64 notepad (pe1wow hung)" + _pid);
                }
                else if (proc.ExitCode != 0)
                {
                    throw new ApplicationException(
                        string.Format("failed to hook wow64 notepad (pe1wow exit code {0}) {1}", proc.ExitCode, _pid));
                }
            }
            else
            {
                uint pidSame;
                uint tid = NativeGoo.GetWindowThreadProcessId(_mainWnd, out pidSame);
                if (tid == 0)
                {
                    throw new ApplicationException(string.Format("InstallHook: GetWindowThreadProcessId failed for {0} (error={1})", _pid, Marshal.GetLastWin32Error()));
                }
                IntPtr hook = NativeGoo.SetWindowsHookEx(NativeGoo.HookType.WH_CALLWNDPROC, _hookProc, _nativeLib, tid);
                if (hook == IntPtr.Zero)
                {
                    throw new ApplicationException(
                        string.Format("InstallHook: SetWindowsHookEx failed for {0} (error={1})", _pid,
                            Marshal.GetLastWin32Error()));
                }
                if (ACK != NativeGoo.SendMessage(_mainWnd, _hookMsg, hook, thisWnd))
                {
                    throw new ApplicationException("InstallHook: failed sending hook message for " + _pid);
                }
            }
            // check if we hooked a notepad that already had a notepad id. if so use that notepad id.
            IntPtr wasHookedResult = NativeGoo.SendMessage(_mainWnd, _checkWasHookedMsg, IntPtr.Zero, thisWnd);
            if (wasHookedResult != ACK)
            {
                int realNotepadId = wasHookedResult.ToInt32();
                if (Program.NotepadController.UpdateRegistration(realNotepadId, _notepadId) != this)
                {
                    throw new ApplicationException(
                        string.Format("failed to UpdateRegistration for already-subclassed notepad {0} ==> {1}",
                            _notepadId, realNotepadId));
                }
                _notepadId = realNotepadId;
            }
            this.NotepadId = NotepadId; // force send msg to update notepad id
            _hooked = true;
        }

        public void Detach()
        {
            _hooked = false;
            // unhook
            uint pidSame;
            uint tid = NativeGoo.GetWindowThreadProcessId(_mainWnd, out pidSame);
            if (tid == 0)
            {
                Program.NotepadController.Log(
                    string.Format("Detach: GetWindowThreadProcessId failed for {0} (error={1})", _pid,
                        Marshal.GetLastWin32Error()), _pid);
                return;
            }
            IntPtr hook = NativeGoo.SetWindowsHookEx(NativeGoo.HookType.WH_CALLWNDPROC, _hookProc, _nativeLib, tid);
            if (hook == IntPtr.Zero)
            {
                Program.NotepadController.Log(string.Format("Detach: SetWindowsHookEx failed for {0} (error={1})", _pid,
                    Marshal.GetLastWin32Error()), _pid);
                return;
            }
            NativeGoo.SendMessage(_mainWnd, _hookMsg, hook, IntPtr.Zero);
        }

        public bool IsHooked
        {
            get { return _hooked; }
        }

        public bool Ping()
        {
            return ACK == NativeGoo.SendMessage(_mainWnd, _pingMsg, IntPtr.Zero, IntPtr.Zero);
        }

        public void SetWindowText(string text)
        {
            NativeGoo.SendMessage(EditWnd, NativeGoo.WM_SETTEXT, IntPtr.Zero, text);
        }

        public string GetWindowText(int maxLen = 0, bool logTruncation = true)
        {
            if (maxLen <= 0)
            {
                maxLen = Program.NotepadController.MaxTextSizeMB * 1024 * 1024;
            }
            int textLength = NativeGoo.SendMessage(EditWnd, NativeGoo.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (textLength > maxLen)
            {
                if (logTruncation) Program.NotepadController.Log(string.Format("Notepad text too long. Truncating. {0}", _notepadId));
                textLength = maxLen;
            }
            StringBuilder sbText = new StringBuilder(textLength + 1);
            NativeGoo.SendMessage(EditWnd, NativeGoo.WM_GETTEXT, new IntPtr(sbText.Capacity), sbText);
            if (!IsWindowAlive)
            {
                // don't return empty text for a dead notepad!
                throw new NotepadDeadException();
            }
            return sbText.ToString();
        }

        public void SetModified(bool value = true)
        {
            NativeGoo.SendMessage(EditWnd, NativeGoo.EM_SETMODIFY, value ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero); 
        }

        public bool IsModified()
        {
            return IntPtr.Zero != NativeGoo.SendMessage(EditWnd, NativeGoo.EM_GETMODIFY, IntPtr.Zero, IntPtr.Zero);
        }

        public string GetNotepadTitle()
        {
            const string titleTrailer = " - Notepad";
            int len = NativeGoo.GetWindowTextLength(_mainWnd) + 1;
            StringBuilder notepadTitle = new StringBuilder(len);
            NativeGoo.GetWindowText(_mainWnd, notepadTitle, notepadTitle.Capacity);
            string title = notepadTitle.ToString();
            if (title.EndsWith(titleTrailer))
            {
                title = title.Remove(title.LastIndexOf(titleTrailer));
            }
            if (!IsWindowAlive)
            {
                // don't return empty title for a dead notepad!
                throw new NotepadDeadException();
            }
            return title;
        }

        public void SetNotepadTitle(string title)
        {
            const string titleTrailer = " - Notepad";
            NativeGoo.SendMessage(_mainWnd, NativeGoo.WM_SETTEXT, IntPtr.Zero, title + titleTrailer);
        }

        // save text if idle (no text change) for 3 seconds.
        // force save text after 30 seconds of continuous text changes.

        public void NotifyTextChange()
        {
            _timerIdle.Change(TIMER_IDLE_INTERVAL, TIMER_IDLE_INTERVAL);
            if (_resetForce)
            {
                _timerForce.Change(TIMER_FORCE_INTERVAL, TIMER_FORCE_INTERVAL);
                _resetForce = false;
            }
        }

        private void IdleCallback(object state)
        {
            _timerIdle.Change(Timeout.Infinite, Timeout.Infinite);
            _timerForce.Change(Timeout.Infinite, Timeout.Infinite);
            _resetForce = true;
            Scrape();
        }

        private void ForceCallback(object state)
        {
            Scrape();
        }

        private void Scrape()
        {
            Program.NotepadController.SaveSnapshot(this);
        }

        public bool LooksUnixy()
        {
            try
            {
                // i'm sure there are much better ways of doing this, but let's see how this goes
                string text = GetWindowText(500, false);
                if (text.Length > 1)
                {
                    int cr = text.IndexOf('\r', 1);
                    int lf = text.IndexOf('\n', 2);
                    if (lf >= 1 && cr != lf - 1)
                    {
                        return true;
                    }
                }
            }
            catch (NotepadDeadException)
            {
            }
            return false;
        }

        private static readonly Regex CommandLineRegex = new Regex(".*notepad(\\.exe)?\"?\\s+(/(a|w)\\s)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string GetPossiblyRelativeFilePath(int pid)
        {
            try
            {
                using (
                    var searcher =
                        new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                {
                    foreach (var obj in searcher.Get())
                    {
                        string cl = obj["CommandLine"].ToString();
                        string clReplaced = CommandLineRegex.Replace(cl, string.Empty, 1);
                        if (clReplaced != cl)
                        {
                            return clReplaced;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.NotepadController.Log("Exception in GetPossiblyRelativeFilePath for " + pid, ex, pid);
            }
            return null;
        }

        public static string GetRealFilePath(int pid, bool fallback = true, bool ensureExists = true)
        {
            string realFilePath = null;
            try
            {
                string cmdLine = DangerousPebHack.GetCommandLine(pid);
                string currentDir = DangerousPebHack.GetCurrentDirectory(pid);

                string cmdLinePathGiven = CommandLineRegex.Replace(cmdLine, string.Empty, 1);
                if (cmdLinePathGiven != cmdLine)
                {
                    realFilePath = Path.Combine(currentDir.Trim('"'), cmdLinePathGiven.Trim('"'));

                    StringBuilder sbRealPath = new StringBuilder(NativeGoo.MAX_PATH);
                    if (NativeGoo.PathCanonicalize(sbRealPath,
                        realFilePath.Length < NativeGoo.MAX_PATH
                            ? realFilePath
                            : realFilePath.Substring(0, NativeGoo.MAX_PATH - 1)))
                    {
                        realFilePath = sbRealPath.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Program.NotepadController.Log("Exception in GetRealFilePath for " + pid, ex, pid);
            }
            try
            {
                if (realFilePath != null && ensureExists)
                {
                    if (!File.Exists(realFilePath))
                    {
                        realFilePath = null;
                    }
                }
                if (realFilePath == null && fallback)
                {
                    realFilePath = GetPossiblyRelativeFilePath(pid);
                    if (realFilePath != null && ensureExists)
                    {
                        if (!File.Exists(realFilePath))
                        {
                            realFilePath = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.NotepadController.Log("Exception at end of GetRealFilePath for " + pid, ex, pid);
            }
            return realFilePath;
        }

        public string RealFilePath
        {
            get { return GetRealFilePath(_pid); }
        }

        private string PossiblyRelativeFilePath
        {
            get { return GetPossiblyRelativeFilePath(_pid); }
        }
    }
}
