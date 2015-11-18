using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using plusequalsone.Properties;

namespace plusequalsone
{
    /// <summary>
    /// Form for configuration settings
    /// </summary>
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
        }

        public static readonly string MY_DOCS_DEFAULT = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Unsaved Notepads");
        private static readonly string REG_STARTUP_NAME = "Notepad += 1";
        private static readonly string REG_STARTUP_FOLDER = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static readonly int MAX_SIZE_MB_DEFAULT = 100;
        private string _folderSavedNotepad;
        private string _lastNonMyDocs;

        private void SettingsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            if (Settings.Default.SavedNotepadFolder == MY_DOCS_DEFAULT)
            {
                checkBoxMyDocuments.Checked = true;
            }
            else
            {
                checkBoxMyDocuments.Checked = false;
                textBoxSavedNotepadFolder.Text = Settings.Default.SavedNotepadFolder;
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_FOLDER))
                {
                    checkBoxStartup.Checked = key.GetValue(REG_STARTUP_NAME) != null;
                }
            }
            catch (Exception ex)
            {
                Program.NotepadController.Log("Error reading registry", ex);
            }

            textBoxMaxSize.Text = Settings.Default.MaxSizeMB.ToString();
        }

        private void checkBoxMyDocuments_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxMyDocuments.Checked)
            {
                textBoxSavedNotepadFolder.ReadOnly = true;
                _lastNonMyDocs = textBoxSavedNotepadFolder.Text;
                textBoxSavedNotepadFolder.Text = MY_DOCS_DEFAULT;
            }
            else
            {
                textBoxSavedNotepadFolder.Text = _lastNonMyDocs ?? string.Empty;
                textBoxSavedNotepadFolder.ReadOnly = false;
            }
        }

        private void buttonBrowse_Click(object sender, EventArgs e)
        {
            if (folderBrowserSavedNotepad.ShowDialog() == DialogResult.OK)
            {
                if (folderBrowserSavedNotepad.SelectedPath == MY_DOCS_DEFAULT)
                {
                    checkBoxMyDocuments.Checked = true;
                }
                else
                {
                    checkBoxMyDocuments.Checked = false;
                    textBoxSavedNotepadFolder.Text = folderBrowserSavedNotepad.SelectedPath;
                }
            }
        }

        private void textBoxSavedNotepadFolder_TextChanged(object sender, EventArgs e)
        {
            _folderSavedNotepad = textBoxSavedNotepadFolder.Text.Trim();
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            int maxSizeMB = MAX_SIZE_MB_DEFAULT;

            // check valid folder
            bool invalid = string.IsNullOrEmpty(_folderSavedNotepad);
            if (!invalid)
            {
                if (!Directory.Exists(_folderSavedNotepad))
                {
                    if (File.Exists(_folderSavedNotepad))
                    {
                        // not a directory!
                        invalid = true;
                        MessageBox.Show(this, "Please enter a folder location, not a file.", "Invalid Path",
                            MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                    }
                    else
                    {
                        invalid = true;
                        if (
                            MessageBox.Show(this, "Create folder?", "Path does not exist", MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question) == DialogResult.Yes)
                        {
                            try
                            {
                                Directory.CreateDirectory(_folderSavedNotepad);
                                invalid = false;
                            }
                            catch (Exception ex)
                            {
                                if (MessageBox.Show(this, "Failed to create directory. More information?", "Error", MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Error) == DialogResult.Yes)
                                {
                                    MessageBox.Show(this, ex.ToString(), "Exception", MessageBoxButtons.OK,
                                        MessageBoxIcon.Error);
                                }
                            }
                        }
                    }
                }
            }
            if (!invalid)
            {
                // max size for notepad text
                if (!int.TryParse(textBoxMaxSize.Text, out maxSizeMB) || maxSizeMB <= 0)
                {
                    invalid = true;
                    MessageBox.Show(this, "Maximum supported size should be a positive integer", "Max size",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            if (!invalid)
            {
                Settings.Default.SavedNotepadFolder = _folderSavedNotepad;
                Settings.Default.MaxSizeMB = maxSizeMB;
                Settings.Default.Save();

                // run at startup?
                // only request write access to reg key if needed
                try
                {
                    bool currentlySetToStartup = false;
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_FOLDER))
                    {
                        currentlySetToStartup = key.GetValue(REG_STARTUP_NAME) != null;
                    }
                    if (currentlySetToStartup && !checkBoxStartup.Checked)
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_FOLDER, true))
                        {
                            key.DeleteValue(REG_STARTUP_NAME, false);
                        }
                    }
                    else if (!currentlySetToStartup && checkBoxStartup.Checked)
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_STARTUP_FOLDER, true))
                        {
                            key.SetValue(REG_STARTUP_NAME,
                                string.Concat("\"", Process.GetCurrentProcess().MainModule.FileName, "\""));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.NotepadController.Log("Error accessing registry", ex);
                }

                Close();
            }
        }
    }
}
