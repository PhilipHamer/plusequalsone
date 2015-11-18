namespace plusequalsone
{
    partial class SettingsForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.textBoxSavedNotepadFolder = new System.Windows.Forms.TextBox();
            this.labelSavedNotepadFolder = new System.Windows.Forms.Label();
            this.checkBoxMyDocuments = new System.Windows.Forms.CheckBox();
            this.folderBrowserSavedNotepad = new System.Windows.Forms.FolderBrowserDialog();
            this.buttonBrowse = new System.Windows.Forms.Button();
            this.buttonOK = new System.Windows.Forms.Button();
            this.checkBoxStartup = new System.Windows.Forms.CheckBox();
            this.labelMaxSize = new System.Windows.Forms.Label();
            this.textBoxMaxSize = new System.Windows.Forms.TextBox();
            this.labelMB = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // textBoxSavedNotepadFolder
            // 
            this.textBoxSavedNotepadFolder.Location = new System.Drawing.Point(12, 34);
            this.textBoxSavedNotepadFolder.Name = "textBoxSavedNotepadFolder";
            this.textBoxSavedNotepadFolder.Size = new System.Drawing.Size(488, 20);
            this.textBoxSavedNotepadFolder.TabIndex = 0;
            this.textBoxSavedNotepadFolder.TextChanged += new System.EventHandler(this.textBoxSavedNotepadFolder_TextChanged);
            // 
            // labelSavedNotepadFolder
            // 
            this.labelSavedNotepadFolder.AutoSize = true;
            this.labelSavedNotepadFolder.Location = new System.Drawing.Point(12, 9);
            this.labelSavedNotepadFolder.Name = "labelSavedNotepadFolder";
            this.labelSavedNotepadFolder.Size = new System.Drawing.Size(114, 13);
            this.labelSavedNotepadFolder.TabIndex = 1;
            this.labelSavedNotepadFolder.Text = "Saved Notepad Folder";
            // 
            // checkBoxMyDocuments
            // 
            this.checkBoxMyDocuments.AutoSize = true;
            this.checkBoxMyDocuments.Location = new System.Drawing.Point(168, 9);
            this.checkBoxMyDocuments.Name = "checkBoxMyDocuments";
            this.checkBoxMyDocuments.Size = new System.Drawing.Size(151, 17);
            this.checkBoxMyDocuments.TabIndex = 2;
            this.checkBoxMyDocuments.Text = "Use My Documents Folder";
            this.checkBoxMyDocuments.UseVisualStyleBackColor = true;
            this.checkBoxMyDocuments.CheckedChanged += new System.EventHandler(this.checkBoxMyDocuments_CheckedChanged);
            // 
            // folderBrowserSavedNotepad
            // 
            this.folderBrowserSavedNotepad.RootFolder = System.Environment.SpecialFolder.MyComputer;
            // 
            // buttonBrowse
            // 
            this.buttonBrowse.Location = new System.Drawing.Point(506, 34);
            this.buttonBrowse.Name = "buttonBrowse";
            this.buttonBrowse.Size = new System.Drawing.Size(33, 23);
            this.buttonBrowse.TabIndex = 3;
            this.buttonBrowse.Text = "...";
            this.buttonBrowse.UseVisualStyleBackColor = true;
            this.buttonBrowse.Click += new System.EventHandler(this.buttonBrowse_Click);
            // 
            // buttonOK
            // 
            this.buttonOK.Location = new System.Drawing.Point(464, 150);
            this.buttonOK.Name = "buttonOK";
            this.buttonOK.Size = new System.Drawing.Size(75, 23);
            this.buttonOK.TabIndex = 4;
            this.buttonOK.Text = "Save";
            this.buttonOK.UseVisualStyleBackColor = true;
            this.buttonOK.Click += new System.EventHandler(this.buttonOK_Click);
            // 
            // checkBoxStartup
            // 
            this.checkBoxStartup.AutoSize = true;
            this.checkBoxStartup.Location = new System.Drawing.Point(12, 81);
            this.checkBoxStartup.Name = "checkBoxStartup";
            this.checkBoxStartup.Size = new System.Drawing.Size(97, 17);
            this.checkBoxStartup.TabIndex = 5;
            this.checkBoxStartup.Text = "Run at StartUp";
            this.checkBoxStartup.UseVisualStyleBackColor = true;
            // 
            // labelMaxSize
            // 
            this.labelMaxSize.AutoSize = true;
            this.labelMaxSize.Location = new System.Drawing.Point(12, 127);
            this.labelMaxSize.Name = "labelMaxSize";
            this.labelMaxSize.Size = new System.Drawing.Size(140, 13);
            this.labelMaxSize.TabIndex = 6;
            this.labelMaxSize.Text = "Max supported notepad size";
            // 
            // textBoxMaxSize
            // 
            this.textBoxMaxSize.Location = new System.Drawing.Point(158, 124);
            this.textBoxMaxSize.Name = "textBoxMaxSize";
            this.textBoxMaxSize.Size = new System.Drawing.Size(100, 20);
            this.textBoxMaxSize.TabIndex = 7;
            // 
            // labelMB
            // 
            this.labelMB.AutoSize = true;
            this.labelMB.Location = new System.Drawing.Point(264, 127);
            this.labelMB.Name = "labelMB";
            this.labelMB.Size = new System.Drawing.Size(23, 13);
            this.labelMB.TabIndex = 8;
            this.labelMB.Text = "MB";
            // 
            // SettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(551, 185);
            this.Controls.Add(this.labelMB);
            this.Controls.Add(this.textBoxMaxSize);
            this.Controls.Add(this.labelMaxSize);
            this.Controls.Add(this.checkBoxStartup);
            this.Controls.Add(this.buttonOK);
            this.Controls.Add(this.buttonBrowse);
            this.Controls.Add(this.checkBoxMyDocuments);
            this.Controls.Add(this.labelSavedNotepadFolder);
            this.Controls.Add(this.textBoxSavedNotepadFolder);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.ShowInTaskbar = false;
            this.Text = "Settings";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SettingsForm_FormClosing);
            this.Load += new System.EventHandler(this.SettingsForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBoxSavedNotepadFolder;
        private System.Windows.Forms.Label labelSavedNotepadFolder;
        private System.Windows.Forms.CheckBox checkBoxMyDocuments;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserSavedNotepad;
        private System.Windows.Forms.Button buttonBrowse;
        private System.Windows.Forms.Button buttonOK;
        private System.Windows.Forms.CheckBox checkBoxStartup;
        private System.Windows.Forms.Label labelMaxSize;
        private System.Windows.Forms.TextBox textBoxMaxSize;
        private System.Windows.Forms.Label labelMB;
    }
}

