using System;
using System.Windows.Forms;

namespace plusequalsone
{
    /// <summary>
    /// Window used to receive messages from notepad process
    /// </summary>
    public partial class HiddenForm : Form
    {
        public HiddenForm()
        {
            InitializeComponent();
            _textChangeMsg = NativeGoo.RegisterWindowMessage("WM_PE1_TEXTCHANGE");
        }

        private readonly uint _textChangeMsg;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == _textChangeMsg)
            {
                int pid = m.WParam.ToInt32();
                int notepadId = m.LParam.ToInt32();
                Notepad notepad = Program.NotepadController.GetNotepad(notepadId);
                if (notepad != null)
                {
                    notepad.NotifyTextChange();
                }
                else
                {
                    Program.NotepadController.Log("notepad with text change not found " + notepadId, pid);
                }
            }
            else if (m.Msg == NativeGoo.WM_ENDSESSION)
            {
                // all processes (including all notepads) have processed WM_QUERYENDSESSION at this point.
                // we want to ignore process exits and exit codes from now on so that we don't delete saved notepad files.
                Program.NotepadController.OnSessionEnded();
            }
            base.WndProc(ref m);
        }
    }
}
