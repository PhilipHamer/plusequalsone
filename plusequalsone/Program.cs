using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using plusequalsone.Properties;
using Timer = System.Windows.Forms.Timer;

namespace plusequalsone
{
    internal interface INotepadController
    {
        /// <summary>
        /// location where we save our unsaved notepads
        /// </summary>
        string SavedTextDirectory { get; set; }
        /// <summary>
        /// max supported text length to save
        /// </summary>
        int MaxTextSizeMB { get; set; }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="msg"></param>
        void Log(string msg);
        /// <summary>
        /// Only logs if process given by pid is running.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="pid"></param>
        void Log(string msg, int pid);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="ex"></param>
        void Log(string msg, Exception ex);
        /// <summary>
        /// Only logs if process given by pid is running.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="ex"></param>
        /// <param name="pid"></param>
        void Log(string msg, Exception ex, int pid);
        /// <summary>
        /// save notepad text to a file
        /// </summary>
        /// <param name="notepad"></param>
        bool SaveSnapshot(Notepad notepad);
        /// <summary>
        /// notepads should notify us of their death
        /// </summary>
        /// <param name="notepad">the notepad that is no longer with us</param>
        /// <param name="exitCode">notepad process' exit code</param>
        void OnProcessExit(Notepad notepad, int exitCode);
        /// <summary>
        /// Can't talk to this notepad. Not good.
        /// </summary>
        /// <param name="notepad"></param>
        void OnPingFailure(Notepad notepad);
        /// <summary>
        /// a new notepad is ready to be tracked
        /// </summary>
        /// <param name="notepad"></param>
        /// <returns>the notepad object we saved to our data structures</returns>
        Notepad RegisterNewNotepad(Notepad notepad);
        /// <summary>
        /// retrieve notepad by notepad id
        /// </summary>
        /// <param name="notepadId"></param>
        /// <returns></returns>
        Notepad GetNotepad(int notepadId);
        /// <summary>
        /// update our collection.
        /// take the notepad that 'fakeNotepadId' refers to and point 'notepadId' at it instead.
        /// </summary>
        /// <param name="notepadId">the new (correct) notepad id</param>
        /// <param name="fakeNotepadId">the old notepad id</param>
        /// <returns></returns>
        Notepad UpdateRegistration(int notepadId, int fakeNotepadId);
        /// <summary>
        /// enumeration of all currently registered notepad ids
        /// </summary>
        IEnumerable<int> AllNotepadIds { get; }
        /// <summary>
        /// notify of WM_ENDSESSION
        /// </summary>
        void OnSessionEnded();
    }

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            if (!_oneInstance.WaitOne(0)) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new HiddenAppContext();
            _notepadController = context;
            Application.Run(context);
        }

        static readonly Mutex _oneInstance = new Mutex(false, "plus_equals_one_+_=_1");

        private static INotepadController _notepadController;

        public static INotepadController NotepadController
        {
            get { return _notepadController; }
        }

        class HiddenAppContext : ApplicationContext, INotepadController
        {
            private readonly SettingsForm _form;
            private readonly HiddenForm _hiddenForm;
            private readonly NotifyIcon _notifyIcon;
            private readonly MenuItem _menuRestoreAll;
            private readonly Exception _startupException;
            private readonly object _logLock = new object();
            private bool _sessionEnded;
            private ProcessWatcher _processWatcher;
            private readonly ConcurrentDictionary<int, Notepad> _notepads = new ConcurrentDictionary<int, Notepad>();

            public HiddenAppContext()
            {
                Timer timerInit = new Timer {Interval = 100};
                timerInit.Tick += TimerInitOnTick;
                timerInit.Start();

                _form = new SettingsForm {Visible = false};

                _hiddenForm = new HiddenForm {Visible = false};

                _menuRestoreAll = new MenuItem("Restore Unsaved Notepads", new EventHandler(RestoreAllMenuHandler))
                {
                    Enabled = false
                };
                MenuItem menuCloseUnsaved = new MenuItem("Safe Close Unsaved Notepads", new EventHandler(CloseUnsavedMenuHandler));
                MenuItem menuConfig = new MenuItem("Configure", new EventHandler(ConfigureMenuHandler));
                MenuItem menuExit = new MenuItem("Exit", new EventHandler(ExitMenuHandler));
                MenuItem menuCloseAndExit = new MenuItem("Safe Close and Exit", new EventHandler(CloseAndExitMenuHandler));

                _notifyIcon = new NotifyIcon
                {
                    Icon = Properties.Resources.Icon1,
                    ContextMenu =
                        new ContextMenu(new MenuItem[] {_menuRestoreAll, menuCloseUnsaved, menuConfig, menuExit, menuCloseAndExit}),
                    Visible = true
                };
                _notifyIcon.BalloonTipClicked += NotifyIconOnBalloonTipClicked;
                _notifyIcon.Text = "Notepad += 1";

                if (Settings.Default.UpgradeNeeded)
                {
                    try
                    {
                        Settings.Default.Upgrade();
                        Settings.Default.UpgradeNeeded = false;
                        Settings.Default.Save();
                    }
                    catch (ConfigurationException ex)
                    {
                        Trace.WriteLine("Failed to upgrade settings: " + ex.ToString());
                    }
                }

                if (string.IsNullOrEmpty(SavedTextDirectory))
                {
                    SavedTextDirectory = SettingsForm.MY_DOCS_DEFAULT;
                }

                try
                {
                    Directory.CreateDirectory(SavedTextDirectory);
                }
                catch (Exception ex)
                {
                    // TODO handle this better, such as prompting for a good directory before resuming normal functionality
                    _startupException = ex;
                }

                try
                {
                    // this fails if not run as Administrator
                    if (!EventLog.SourceExists(EVENT_LOG_SOURCE))
                    {
                        EventLog.CreateEventSource(EVENT_LOG_SOURCE, "Application");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
                }

                if (MaxTextSizeMB <= 0)
                {
                    MaxTextSizeMB = SettingsForm.MAX_SIZE_MB_DEFAULT;
                }
            }

            private static readonly string EVENT_LOG_SOURCE = "Notepad+=1";

            private void StartupFailure(string message, Exception ex = null)
            {
                // catastrophic failure. only show exit menu item and don't sniff for new notepads.
                _notifyIcon.ContextMenu = new ContextMenu(new MenuItem[] { new MenuItem("Exit", new EventHandler(ExitMenuHandler)) });
                _notifyIcon.Tag = new BalloonTipData() { Reason = BalloonTipReason.CatastrophicFailure };
                _notifyIcon.ShowBalloonTip(10000, "Startup failure!", message, ToolTipIcon.Error);
                _notifyIcon.Icon = Properties.Resources.Icon2;
                _notifyIcon.Text = "Cannot monitor notepads. Please restart.";
                try
                {
                    EventLog.WriteEntry(EVENT_LOG_SOURCE, message + (ex != null ? "\r\n" + ex.ToString() : string.Empty),
                        EventLogEntryType.Error);
                }
                catch (Exception)
                {
                }
            }

            private void TimerInitOnTick(object sender, EventArgs eventArgs)
            {
                ((Timer) sender).Stop();

                // message pump is pumping, so we can start up a few things now

                string startupMessage;
                if (!Notepad.CheckStartupSuccess(out startupMessage))
                {
                    StartupFailure(startupMessage);
                    return;
                }

                if (_startupException != null)
                {
                    StartupFailure(_startupException.Message, _startupException);
                    Trace.WriteLine(_startupException.ToString());
                    return;
                }

                try
                {
                    _processWatcher = new ProcessWatcher(_hiddenForm.Handle, true);
                    _processWatcher.NotepadHookInstalledEvent += ProcessWatcherOnNotepadHookInstalledEvent;
                    _processWatcher.NotepadSlowOpenEvent += ProcessWatcherOnNotepadSlowOpenEvent;
                    _processWatcher.NotepadReallySlowOpenEvent += ProcessWatcherOnNotepadReallySlowOpenEvent;
                }
                catch (Exception ex)
                {
                    StartupFailure("Could not start process watcher: " + ex.Message, ex);
                    Trace.WriteLine(ex.ToString());
                    return;
                }

                // wait for any existing notepads to be re-subclassed before prompting a restore
                try
                {
                    if (!CheckHookExistingComplete())
                    {
                        // start polling
                        Timer timerCheckHookExisting = new Timer {Interval = 500, Tag = Environment.TickCount};
                        timerCheckHookExisting.Tick += TimerCheckHookExistingOnTick;
                        timerCheckHookExisting.Start();
                        // safeguard: ensure Restore menu item is eventually enabled
                        Timer timerForceRestoreEnable = new Timer();
                        timerForceRestoreEnable.Interval = MAX_WAIT_FOR_HOOK_EXISTING;
                        timerForceRestoreEnable.Tick += delegate(object o, EventArgs args)
                        {
                            _menuRestoreAll.Enabled = true;
                            ((Timer) o).Stop();
                        };
                        timerForceRestoreEnable.Start();
                    }
                }
                catch (Exception ex)
                {
                    NotepadController.Log("Exception waiting for hook existing notepads", ex);
                }
            }

            private const int MAX_WAIT_FOR_HOOK_EXISTING = 10000;

            private void TimerCheckHookExistingOnTick(object sender, EventArgs eventArgs)
            {
                Timer timer = (Timer) sender;
                try
                {
                    if (CheckHookExistingComplete())
                    {
                        timer.Stop();
                    }
                    else if (Environment.TickCount - (int)timer.Tag > MAX_WAIT_FOR_HOOK_EXISTING)
                    {
                        Log("Failed to hook existing notepad(s) within 10 seconds");
                        timer.Stop();
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception in timer callback for hook existing", ex);
                    timer.Stop();
                }
            }

            private bool CheckHookExistingComplete()
            {
                if (!_processWatcher.IsHookExistingComplete()) return false;

                _menuRestoreAll.Enabled = true;
                if (Directory.GetFiles(SavedTextDirectory, "*.txt").Length > 0)
                {
                    _notifyIcon.Tag = new BalloonTipData()
                    {
                        Reason = BalloonTipReason.RestoreUnsavedNotepads
                    };
                    _notifyIcon.ShowBalloonTip(10000, "Unsaved data recovered", "Click to restore unsaved notepads", ToolTipIcon.Info);
                }
                return true;
            }

            private enum BalloonTipReason
            {
                Unknown,
                UnixyLineEndings,
                LargeFileSlowOpen,
                ReallyLargeFileReallySlowOpen,
                RestoreUnsavedNotepads,
                LogMessage,
                CatastrophicFailure
            }

            private class BalloonTipData
            {
                public BalloonTipReason Reason;
                public string RealFilePath;
                public int ProcessId;
            }

            private void ProcessWatcherOnNotepadSlowOpenEvent(object sender, ProcessWatcher.NotepadSlowOpenEventArgs notepadSlowOpenEventArgs)
            {
                int pid = notepadSlowOpenEventArgs.ProcessId;
                string realFilePath = Notepad.GetRealFilePath(pid);
                if (!string.IsNullOrEmpty(realFilePath) && Path.GetDirectoryName(realFilePath) != Path.Combine(SavedTextDirectory, "tmp"))
                {
                    _notifyIcon.Tag = new BalloonTipData()
                    {
                        Reason = BalloonTipReason.LargeFileSlowOpen,
                        RealFilePath = realFilePath,
                        ProcessId = pid
                    };
                    _notifyIcon.ShowBalloonTip(10000, "Notepad is opening slowly", "Click to preview truncated text", ToolTipIcon.Warning);
                }
            }

            private void ProcessWatcherOnNotepadReallySlowOpenEvent(object sender, ProcessWatcher.NotepadSlowOpenEventArgs notepadSlowOpenEventArgs)
            {
                int pid = notepadSlowOpenEventArgs.ProcessId;
                string realFilePath = Notepad.GetRealFilePath(pid);
                if (!string.IsNullOrEmpty(realFilePath) && Path.GetDirectoryName(realFilePath) != Path.Combine(SavedTextDirectory, "tmp"))
                {
                    _notifyIcon.Tag = new BalloonTipData()
                    {
                        Reason = BalloonTipReason.ReallyLargeFileReallySlowOpen,
                        RealFilePath = realFilePath,
                        ProcessId = pid
                    };
                    _notifyIcon.ShowBalloonTip(10000, "Notepad continues to open slowly", "Click to open this file in Wordpad", ToolTipIcon.Warning);
                }
            }

            private void ProcessWatcherOnNotepadHookInstalledEvent(object sender, ProcessWatcher.NotepadHookInstalledEventArgs notepadHookInstalledEventArgs)
            {
                Notepad notepad = notepadHookInstalledEventArgs.Notepad;
                if (notepad.LooksUnixy())
                {
                    string realFilePath = notepad.RealFilePath;
                    if (!string.IsNullOrEmpty(realFilePath))
                    {
                        _notifyIcon.Tag = new BalloonTipData()
                        {
                            Reason = BalloonTipReason.UnixyLineEndings,
                            RealFilePath = realFilePath,
                            ProcessId = notepad.Pid
                        };
                        _notifyIcon.ShowBalloonTip(10000, "Unix line endings detected", "Click to open this file in Wordpad", ToolTipIcon.Info);
                    }
                }
            }

            private void NotifyIconOnBalloonTipClicked(object sender, EventArgs eventArgs)
            {
                BalloonTipData data = _notifyIcon.Tag as BalloonTipData;
                if (data != null)
                {
                    try
                    {
                        if ((data.Reason == BalloonTipReason.UnixyLineEndings ||
                             data.Reason == BalloonTipReason.ReallyLargeFileReallySlowOpen) &&
                            !string.IsNullOrEmpty(data.RealFilePath))
                        {
                            // wordpad won't open files in use by another process
                            // test first by trying to open with FileShare.Read
                            string filePath = data.RealFilePath;
                            bool useOrigFile = false;
                            try
                            {
                                using (File.OpenRead(data.RealFilePath))
                                {
                                    // do nothing
                                }
                                useOrigFile = true;
                            }
                            catch (Exception)
                            {
                            }
                            if (!useOrigFile)
                            {
                                try
                                {
                                    // copy to tmp file that wordpad can open
                                    string tmpFilePath = Path.Combine(SavedTextDirectory, "tmp",
                                        Path.GetRandomFileName());
                                    Directory.CreateDirectory(Path.GetDirectoryName(tmpFilePath));
                                    File.Copy(data.RealFilePath, tmpFilePath);
                                    filePath = tmpFilePath;
                                }
                                catch (Exception)
                                {
                                    useOrigFile = true;
                                }
                            }
                            Process proc = StartProcess("wordpad.exe", filePath);
                            if (!useOrigFile)
                            {
                                CleanupTmpFileInBackground(proc, filePath);
                            }
                        }
                        else if (data.Reason == BalloonTipReason.LargeFileSlowOpen &&
                                 !string.IsNullOrEmpty(data.RealFilePath))
                        {
                            // read in first 40K or so
                            // write to file in tmp folder
                            // open in notepad
                            string tmpFilePath = Path.Combine(SavedTextDirectory, "tmp", Path.GetRandomFileName());
                            Directory.CreateDirectory(Path.GetDirectoryName(tmpFilePath));
                            using (FileStream fsIn = new FileStream(data.RealFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                byte[] buf = new byte[40960];
                                int bytesRead = fsIn.Read(buf, 0, buf.Length);
                                if (bytesRead > 0)
                                {
                                    using (FileStream fsOut = File.OpenWrite(tmpFilePath))
                                    {
                                        fsOut.Write(buf, 0, bytesRead);
                                    }
                                }
                            }
                            // we'll end up hooking this process, but we don't expect users to modify its text.
                            // if they do, then we'll save it to our working folder like any other modified notepad.
                            // a bit goofy, but we want to hook all notepads.
                            // users can always File/New and start over within the same process.
                            Process proc = StartProcess("notepad.exe", tmpFilePath);
                            CleanupTmpFileInBackground(proc, tmpFilePath);
                        }
                        else if (data.Reason == BalloonTipReason.RestoreUnsavedNotepads)
                        {
                            RestoreNotepads();
                        }
                        else if (data.Reason == BalloonTipReason.LogMessage)
                        {
                            // TODO 'tail' into tmp file and view that instead of the whole file?
                            StartProcess("notepad.exe", Path.Combine(SavedTextDirectory, "log", "pe1.log"));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("Exception handling balloon tip", ex, data.ProcessId);
                    }
                }
            }

            private static void CleanupTmpFileInBackground(Process proc, string tmpFilePath)
            {
                if (tmpFilePath != null &&
                    tmpFilePath.StartsWith(Path.Combine(Program.NotepadController.SavedTextDirectory, "tmp"),
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    // wait for input idle and then delete the tmp file
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        try
                        {
                            proc.WaitForInputIdle(30000);
                            // TODO i don't think this is sufficient. after input idle, the program could still be using the file for a brief period.
                        }
                        catch (Exception)
                        {
                        }
                        try
                        {
                            File.Delete(tmpFilePath);
                        }
                        catch (Exception ex)
                        {
                            Program.NotepadController.Log("failed to delete tmp file", ex);
                        }
                    });
                }
            }

            private static Process StartProcess(string process, string arguments = null)
            {
                return string.IsNullOrEmpty(arguments)
                    ? Process.Start(process)
                    : Process.Start(process, arguments.TrimStart().StartsWith("\"") ? arguments : string.Format("\"{0}\"", arguments));
            }

            private class RestoreNotepadData
            {
                public int NotepadId;
                public int FakeNotepadId;
                public string FullPath;
                public string NotepadTitle;
                public int StartTicks;
            }

            private void RestoreNotepads()
            {
                try
                {
                    foreach (string fullpath in Directory.GetFiles(SavedTextDirectory))
                    {
                        try
                        {
                            string[] parts = Path.GetFileName(fullpath).Split(new[] {' '}, 2);
                            int notepadId;
                            if (int.TryParse(parts[0], out notepadId) && !_notepads.ContainsKey(notepadId))
                            {
                                if (parts.Length > 1 &&
                                    parts[1].EndsWith(".txt", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    string notepadTitle = parts[1].Substring(0, parts[1].Length - 4).Trim();
                                    // we need to spawn a notepad, wait for our process watcher to see it and hook it, 
                                    // and then continue to shove the text into it.
                                    // we need to communicate the notepad id to the process watcher, but there's a race condition here.
                                    // we will let the process watcher hook the notepad with the wrong notepad id, then we'll update
                                    // the text and the notepad id a bit later
                                    Process proc = StartProcess("notepad.exe");
                                    Timer timer = new Timer
                                    {
                                        Tag = new RestoreNotepadData()
                                        {
                                            NotepadId = notepadId,
                                            FakeNotepadId = Notepad.MakeNotepadId(proc),
                                            FullPath = fullpath,
                                            NotepadTitle = notepadTitle,
                                            StartTicks = Environment.TickCount
                                        },
                                        Interval = 200
                                    };
                                    timer.Tick += TimerCheckContinueRestore;
                                    timer.Start();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("Exception restoring notepad from " + fullpath, ex);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception while restoring notepads", ex);
                }
            }

            void RestoreAllMenuHandler(object sender, EventArgs e)
            {
                RestoreNotepads();
            }

            private const int MAX_WAIT_FOR_RESTORE = 30000;

            private void TimerCheckContinueRestore(object sender, EventArgs eventArgs)
            {
                Timer timer = (Timer) sender;
                try
                {
                    RestoreNotepadData data = (RestoreNotepadData) timer.Tag;
                    // newly-created notepads will have a notepad id based on the new process id and start time.
                    // we can look for this 'fake' notepad id to be registered, remove it, and add it back with the correct key.
                    Notepad notepad;
                    if (_notepads.TryGetValue(data.FakeNotepadId, out notepad) && notepad.IsHooked &&
                        UpdateRegistration(data.NotepadId, data.FakeNotepadId) != null)
                    {
                        timer.Stop();
                        notepad.NotepadId = data.NotepadId; // send msg to notepad to update its notepad id
                        string text;
                        lock (notepad.FileLock)
                        {
                            text = File.ReadAllText(data.FullPath);
                        }
                        notepad.SetWindowText(text);
                        notepad.SetModified();
                        notepad.SetNotepadTitle(data.NotepadTitle);
                        NotepadCountMayHaveChanged();
                    }
                    else if (Environment.TickCount - data.StartTicks > MAX_WAIT_FOR_RESTORE)
                    {
                        Log("Could not restore notepad, because notepad id not seen: " + data.FakeNotepadId);
                        timer.Stop();
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception in timer callback for restore", ex);
                    timer.Stop();
                }
            }

            void CloseAndExitMenuHandler(object sender, EventArgs e)
            {
                CloseUnsavedMenuHandler(sender, e);
                ExitMenuHandler(sender, e);
            }

            void CloseUnsavedMenuHandler(object sender, EventArgs e)
            {
                CloseUnsaved();
            }

            private void CloseUnsaved()
            {
                try
                {
                    // for each notepad, check if modified flag is set
                    // if so, then clear it, save text, and close
                    foreach (var kvp in _notepads)
                    {
                        int pid = 0;
                        try
                        {
                            Notepad notepad = kvp.Value;
                            pid = notepad.Pid;
                            if (notepad.IsModified())
                            {
                                notepad.Detach();
                                if (NotepadController.SaveSnapshot(notepad))
                                {
                                    notepad.SetModified(false);
                                    try
                                    {
                                        Process.GetProcessById(notepad.Pid).CloseMainWindow();
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }
                                Notepad removed;
                                _notepads.TryRemove(kvp.Key, out removed);
                                NotepadCountMayHaveChanged();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log("Exception while saving and closing notepad", ex, pid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception in CloseUnsaved", ex);
                }
            }

            void ConfigureMenuHandler(object sender, EventArgs e)
            {
                if (_form.Visible)
                {
                    _form.Activate();
                }
                else
                {
                    _form.ShowDialog();
                }
            }

            void ExitMenuHandler(object sender, EventArgs e)
            {
                _notifyIcon.Visible = false;
                if (_processWatcher != null) _processWatcher.Dispose();
                ExitThread(); // exit app context = app dies
            }

            public Notepad RegisterNewNotepad(Notepad notepad)
            {
                Notepad gotten = _notepads.GetOrAdd(notepad.NotepadId, notepad);
                if (!object.ReferenceEquals(gotten, notepad))
                {
                    gotten.Pid = notepad.Pid;
                }
                NotepadCountMayHaveChanged();
                return gotten;
            }

            public Notepad GetNotepad(int notepadId)
            {
                Notepad notepad;
                _notepads.TryGetValue(notepadId, out notepad);
                return notepad;
            }

            public Notepad UpdateRegistration(int notepadId, int fakeNotepadId)
            {
                Notepad notepad;
                if (_notepads.TryRemove(fakeNotepadId, out notepad))
                {
                    notepad = _notepads.GetOrAdd(notepadId, notepad);
                    NotepadCountMayHaveChanged();
                    return notepad;
                }
                return null;
            }

            public IEnumerable<int> AllNotepadIds 
            {
                get
                {
                    return _notepads.Keys;
                }
            }

            public bool SaveSnapshot(Notepad notepad)
            {
                try
                {
                    string text = notepad.GetWindowText();
                    string notepadTitle = notepad.GetNotepadTitle();
                    string filename = string.Format("{0} {1}.txt", notepad.NotepadId, notepadTitle);
                    string fullpath = Path.Combine(SavedTextDirectory, filename);
                    DeleteFilesForNotepad(notepad);
                    lock (notepad.FileLock)
                    {
                        File.WriteAllText(fullpath, text);
                    }
                    return true;
                }
                catch (Notepad.NotepadDeadException)
                {
                }
                catch (Exception ex)
                {
                    Log("Exception saving notepad snapshot", ex, notepad.Pid);
                }
                return false;
            }

            public void OnProcessExit(Notepad notepad, int exitCode)
            {
                if (exitCode == 0 && !_sessionEnded)
                {
                    DeleteFilesForNotepad(notepad);
                }
                Notepad removed;
                _notepads.TryRemove(notepad.NotepadId, out removed);
                NotepadCountMayHaveChanged();
            }

            public void OnPingFailure(Notepad notepad)
            {
                Notepad removed;
                _notepads.TryRemove(notepad.NotepadId, out removed);
                NotepadCountMayHaveChanged();
            }

            private void NotepadCountMayHaveChanged()
            {
                _notifyIcon.Text = _notepads.Count == 0 ? "Notepad += 1" : string.Format("Monitoring {0} notepad{1}", _notepads.Count, _notepads.Count == 1 ? string.Empty : "s");
            }

            public void OnSessionEnded()
            {
                _sessionEnded = true;
            }

            private int DeleteFilesForNotepad(Notepad notepad)
            {
                int numDeleted = 0;
                try
                {
                    lock (notepad.FileLock)
                    {
                        foreach (
                            string filename in
                                Directory.GetFiles(SavedTextDirectory, string.Format("{0} *.txt", notepad.NotepadId)))
                        {
                            try
                            {
                                File.Delete(Path.Combine(SavedTextDirectory, filename));
                            }
                            catch (Exception ex)
                            {
                                Log("Exception trying to delete notepad at " + filename, ex);
                            }
                            numDeleted++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Exception when deleting files for notepad " + notepad.NotepadId, ex);
                }
                return numDeleted;
            }

            public string SavedTextDirectory
            {
                get
                {
                    return Settings.Default.SavedNotepadFolder;
                }
                set
                {
                    Settings.Default.SavedNotepadFolder = value;
                    Settings.Default.Save();
                }
            }

            public int MaxTextSizeMB
            {
                get
                {
                    return Settings.Default.MaxSizeMB;
                }
                set
                {
                    Settings.Default.MaxSizeMB = value;
                    Settings.Default.Save();
                }
            }

            public void Log(string msg)
            {
                string path = Path.Combine(SavedTextDirectory, "log", "pe1.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                try
                {
                    lock (_logLock)
                    {
                        File.AppendAllText(path, string.Format("[{0}] {1}{2}", DateTime.Now, msg, Environment.NewLine));
                    }
                }
                catch (Exception ex)
                {
                    // uh oh. swallow and do an OutputDebugString instead.
                    Trace.WriteLine(msg);
                    Trace.WriteLine(ex.ToString());
                }
                const int maxLogBalloon = 120;
                _notifyIcon.Tag = new BalloonTipData() {Reason = BalloonTipReason.LogMessage};
                string balloonMsg = msg.Split('\r', '\n')[0];
                _notifyIcon.ShowBalloonTip(10000, "New Log Message",
                    balloonMsg.Length > maxLogBalloon ? balloonMsg.Substring(0, maxLogBalloon) + "..." : balloonMsg, ToolTipIcon.Warning);
            }

            public void Log(string msg, int pid)
            {
                if (pid > 0)
                {
                    try
                    {
                        Process.GetProcessById(pid);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
                Log(msg);
            }

            public void Log(string msg, Exception ex)
            {
                Log(string.Concat(msg, Environment.NewLine, ex.ToString()));
            }

            public void Log(string msg, Exception ex, int pid)
            {
                if (pid > 0)
                {
                    try
                    {
                        Process.GetProcessById(pid);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }
                Log(msg, ex);
            }
        }
    }
}
