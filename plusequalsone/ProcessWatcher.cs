using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace plusequalsone
{
    /// <summary>
    /// Watches for notepad processes to be created, then injects our dll in order to subclass the main window.
    /// </summary>
    class ProcessWatcher : IDisposable
    {
        private ManagementEventWatcher _watcher;
        private readonly IntPtr _thisWnd;
        private readonly Thread _pingThread;
        private readonly Task[] _hookedAtStartup;

        public ProcessWatcher(IntPtr thisWnd, bool includeAlreadyRunning)
        {
            _thisWnd = thisWnd;

            _pingThread = new Thread(new ThreadStart(PingThreadProc)) {Name = "PE1 Ping Thread", IsBackground = true};
            _pingThread.Start();

            if (includeAlreadyRunning)
            {
                // note that callers won't be subscribed to our events at this point, but that's ok right now.
                // we can go ahead and hook existing notepads, but not fire any events.
                try
                {
                    List<Task> tasks = new List<Task>();
                    foreach (Process proc in Process.GetProcessesByName("notepad"))
                    {
                        try
                        {
                            tasks.Add(Task.Factory.StartNew(state => HandleNewNotepadProcess((int) state, false, true), proc.Id));
                        }
                        catch (Exception ex)
                        {
                            Program.NotepadController.Log("Exception handling already running notepad", ex);
                        }
                    }
                    if (tasks.Count > 0) _hookedAtStartup = tasks.ToArray();
                }
                catch (Exception ex)
                {
                    Program.NotepadController.Log("Exception enumerating already running processes", ex);
                }
            }

            WqlEventQuery query = new WqlEventQuery("__InstanceCreationEvent", new TimeSpan(0, 0, 1), 
                "TargetInstance isa \"Win32_Process\" and TargetInstance.Name=\"notepad.exe\"");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += WatcherOnEventArrived;
            _watcher.Start();
        }

        public bool IsHookExistingComplete()
        {
            return WaitForHookExisting(0);
        }

        public bool WaitForHookExisting(int timeoutMs = Timeout.Infinite)
        {
            if (_hookedAtStartup == null) return true;
            try
            {
                return Task.WaitAll(_hookedAtStartup, timeoutMs);
            }
            catch (Exception ex)
            {
                Program.NotepadController.Log("Exception waiting for tasks in WaitForHookExisting", ex);
                return false;
            }
        }

        private void WatcherOnEventArrived(object sender, EventArrivedEventArgs eventArrivedEventArgs)
        {
            int pid = (int)(uint)((ManagementBaseObject)eventArrivedEventArgs.NewEvent["TargetInstance"])["ProcessId"];
            HandleNewNotepadProcess(pid, true, false);
        }

        private void HandleNewNotepadProcess(int pid, bool monitorSlowOpen, bool saveTextImmediately)
        {
            Notepad notepad = null;
            try
            {
                if (monitorSlowOpen)
                {
                    try
                    {
                        notepad = Notepad.Create(pid, SlowOpenWarning, ReallySlowOpenWarning);
                    }
                    catch (Notepad.SlowOpenException)
                    {
                        return;
                    }
                }
                else
                {
                    notepad = Notepad.Create(pid);
                }
                notepad.InstallHook(_thisWnd);

                var handler = this.NotepadHookInstalledEvent;
                if (handler != null)
                {
                    handler(this, new NotepadHookInstalledEventArgs(notepad));
                }

                if (saveTextImmediately && notepad.IsModified())
                {
                    Program.NotepadController.SaveSnapshot(notepad);
                }
            }
            catch (Exception ex)
            {
                if (ex is Win32Exception && ((Win32Exception) ex).ErrorCode == unchecked((int)0x80004005))
                {
                    Program.NotepadController.Log("Access Denied for notepad " + pid + ". Running this app as Administrator may fix this problem.", ex);
                }
                else
                {
                    Program.NotepadController.Log("Exception in HandleNewNotepadProcess " + pid, ex, pid);
                }
            }
        }

        private Notepad.SlowOpenAction SlowOpenWarning(int pid)
        {
            // notepad won't respond after 3 seconds.
            // probably a large file. does the user want to preview it?
            var handler = this.NotepadSlowOpenEvent;
            if (handler != null)
            {
                handler(this, new NotepadSlowOpenEventArgs(pid));
            }
            return Notepad.SlowOpenAction.ContinueWaiting;
        }

        private Notepad.SlowOpenAction ReallySlowOpenWarning(int pid)
        {
            // notepad won't respond after 10 seconds.
            // spawn another thread to keep waiting so we don't block other notepads from being hooked.
            ThreadPool.QueueUserWorkItem(state => HandleNewNotepadProcess((int) state, false, false), pid);
            var handler = this.NotepadReallySlowOpenEvent;
            if (handler != null)
            {
                handler(this, new NotepadSlowOpenEventArgs(pid));
            }
            return Notepad.SlowOpenAction.StopWaiting;
        }

        public class NotepadHookInstalledEventArgs : EventArgs
        {
            public NotepadHookInstalledEventArgs(Notepad notepad)
            {
                this.Notepad = notepad;
            }

            public Notepad Notepad { get; private set; }
        }

        public class NotepadSlowOpenEventArgs : EventArgs
        {
            public NotepadSlowOpenEventArgs(int pid)
            {
                this.ProcessId = pid;
            }

            public int ProcessId { get; private set; }
        }

        public event EventHandler<NotepadHookInstalledEventArgs> NotepadHookInstalledEvent;
        public event EventHandler<NotepadSlowOpenEventArgs> NotepadSlowOpenEvent;
        public event EventHandler<NotepadSlowOpenEventArgs> NotepadReallySlowOpenEvent;

        void PingThreadProc()
        {
            while (true)
            {
                try
                {
                    Thread.Sleep(5000);
                    foreach (int notepadId in Program.NotepadController.AllNotepadIds)
                    {
                        int pid = 0;
                        try
                        {
                            Notepad notepad = Program.NotepadController.GetNotepad(notepadId);
                            if (notepad != null)
                            {
                                pid = notepad.Pid;
                                if (!notepad.Ping())
                                {
                                    Program.NotepadController.OnPingFailure(notepad);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.NotepadController.Log("Exception while Pinging notepad " + notepadId, ex, pid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.NotepadController.Log("Exception in PingThreadProc", ex);
                }
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Stop();
                _watcher.EventArrived -= WatcherOnEventArrived;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }
}
