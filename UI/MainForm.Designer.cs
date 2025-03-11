namespace AudioDual
{
    partial class MainForm
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
            this.components = new System.ComponentModel.Container();
            this.lvDevices = new System.Windows.Forms.ListView();
            this.colName = new System.Windows.Forms.ColumnHeader();
            this.colDefault = new System.Windows.Forms.ColumnHeader();
            this.colStatus = new System.Windows.Forms.ColumnHeader();
            this.colVolume = new System.Windows.Forms.ColumnHeader();
            this.btnRefreshDevices = new System.Windows.Forms.Button();
            this.btnToggleDevice = new System.Windows.Forms.Button();
            this.trackVolume = new System.Windows.Forms.TrackBar();
            this.lblVolume = new System.Windows.Forms.Label();
            this.lblVolumePercentage = new System.Windows.Forms.Label();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.audioVisualizer = new System.Windows.Forms.Panel();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.checkStartMinimized = new System.Windows.Forms.CheckBox();
            this.checkRunAtStartup = new System.Windows.Forms.CheckBox();
            this.btnSaveSettings = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).BeginInit();
            this.SuspendLayout();
            
            // lvDevices
            this.lvDevices.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
                this.colName,
                this.colDefault,
                this.colStatus,
                this.colVolume});
            this.lvDevices.FullRowSelect = true;
            this.lvDevices.HideSelection = false;
            this.lvDevices.Location = new System.Drawing.Point(12, 41);
            this.lvDevices.MultiSelect = false;
            this.lvDevices.Name = "lvDevices";
            this.lvDevices.Size = new System.Drawing.Size(500, 200);
            this.lvDevices.TabIndex = 0;
            this.lvDevices.UseCompatibleStateImageBehavior = false;
            this.lvDevices.View = System.Windows.Forms.View.Details;
            this.lvDevices.SelectedIndexChanged += new System.EventHandler(this.lvDevices_SelectedIndexChanged);
            
            // colName
            this.colName.Text = "Device Name";
            this.colName.Width = 250;
            
            // colDefault
            this.colDefault.Text = "Default";
            this.colDefault.Width = 80;
            
            // colStatus
            this.colStatus.Text = "Status";
            this.colStatus.Width = 80;
            
            // colVolume
            this.colVolume.Text = "Volume";
            this.colVolume.Width = 80;
            
            // btnRefreshDevices
            this.btnRefreshDevices.Location = new System.Drawing.Point(12, 12);
            this.btnRefreshDevices.Name = "btnRefreshDevices";
            this.btnRefreshDevices.Size = new System.Drawing.Size(110, 23);
            this.btnRefreshDevices.TabIndex = 1;
            this.btnRefreshDevices.Text = "Refresh Devices";
            this.btnRefreshDevices.UseVisualStyleBackColor = true;
            this.btnRefreshDevices.Click += new System.EventHandler(this.btnRefreshDevices_Click);
            
            // Audio visualizer panel
            this.audioVisualizer.Location = new System.Drawing.Point(12, 250);
            this.audioVisualizer.Name = "audioVisualizer";
            this.audioVisualizer.Size = new System.Drawing.Size(500, 40);
            this.audioVisualizer.TabIndex = 6;
            this.audioVisualizer.Paint += new System.Windows.Forms.PaintEventHandler(this.audioVisualizer_Paint);
            
            // btnToggleDevice
            this.btnToggleDevice.Location = new System.Drawing.Point(12, 320);
            this.btnToggleDevice.Name = "btnToggleDevice";
            this.btnToggleDevice.Size = new System.Drawing.Size(110, 23);
            this.btnToggleDevice.TabIndex = 7;
            this.btnToggleDevice.Text = "Enable Device";
            this.btnToggleDevice.UseVisualStyleBackColor = true;
            this.btnToggleDevice.Click += new System.EventHandler(this.btnToggleDevice_Click);
            
            // trackVolume
            this.trackVolume.Location = new System.Drawing.Point(195, 320);
            this.trackVolume.Maximum = 100;
            this.trackVolume.Name = "trackVolume";
            this.trackVolume.Size = new System.Drawing.Size(249, 45);
            this.trackVolume.TabIndex = 8;
            this.trackVolume.TickFrequency = 10;
            this.trackVolume.Value = 100;
            this.trackVolume.Scroll += new System.EventHandler(this.trackVolume_Scroll);
            
            // lblVolume
            this.lblVolume.AutoSize = true;
            this.lblVolume.Location = new System.Drawing.Point(144, 324);
            this.lblVolume.Name = "lblVolume";
            this.lblVolume.Size = new System.Drawing.Size(45, 15);
            this.lblVolume.TabIndex = 9;
            this.lblVolume.Text = "Volume:";
            
            // lblVolumePercentage
            this.lblVolumePercentage.AutoSize = true;
            this.lblVolumePercentage.Location = new System.Drawing.Point(450, 324);
            this.lblVolumePercentage.Name = "lblVolumePercentage";
            this.lblVolumePercentage.Size = new System.Drawing.Size(35, 15);
            this.lblVolumePercentage.TabIndex = 10;
            this.lblVolumePercentage.Text = "100%";
            
            // groupBox1 - Device Controls (moved lower)
            this.groupBox1.Location = new System.Drawing.Point(12, 295);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(502, 60);
            this.groupBox1.TabIndex = 11;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Device Controls";
            
            // groupBox2
            this.groupBox2.Location = new System.Drawing.Point(12, 359);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(502, 80);
            this.groupBox2.TabIndex = 12;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Settings";
            
            // checkStartMinimized
            this.checkStartMinimized.AutoSize = true;
            this.checkStartMinimized.Location = new System.Drawing.Point(24, 383);
            this.checkStartMinimized.Name = "checkStartMinimized";
            this.checkStartMinimized.Size = new System.Drawing.Size(110, 19);
            this.checkStartMinimized.TabIndex = 13;
            this.checkStartMinimized.Text = "Start Minimized";
            this.checkStartMinimized.UseVisualStyleBackColor = true;
            
            // checkRunAtStartup
            this.checkRunAtStartup.AutoSize = true;
            this.checkRunAtStartup.Location = new System.Drawing.Point(140, 383);
            this.checkRunAtStartup.Name = "checkRunAtStartup";
            this.checkRunAtStartup.Size = new System.Drawing.Size(105, 19);
            this.checkRunAtStartup.TabIndex = 14;
            this.checkRunAtStartup.Text = "Run At Startup";
            this.checkRunAtStartup.UseVisualStyleBackColor = true;
            
            // btnSaveSettings
            this.btnSaveSettings.Location = new System.Drawing.Point(414, 380);
            this.btnSaveSettings.Name = "btnSaveSettings";
            this.btnSaveSettings.Size = new System.Drawing.Size(88, 23);
            this.btnSaveSettings.TabIndex = 15;
            this.btnSaveSettings.Text = "Save Settings";
            this.btnSaveSettings.UseVisualStyleBackColor = true;
            this.btnSaveSettings.Click += new System.EventHandler(this.btnSaveSettings_Click);
            
            // MainForm
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(526, 451);
            this.Controls.Add(this.btnSaveSettings);
            this.Controls.Add(this.checkRunAtStartup);
            this.Controls.Add(this.checkStartMinimized);
            this.Controls.Add(this.lblVolumePercentage);
            this.Controls.Add(this.lblVolume);
            this.Controls.Add(this.trackVolume);
            this.Controls.Add(this.btnToggleDevice);
            this.Controls.Add(this.audioVisualizer);
            this.Controls.Add(this.btnRefreshDevices);
            this.Controls.Add(this.lvDevices);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.groupBox2);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.Text = "Windows Dual Audio Manager";
            ((System.ComponentModel.ISupportInitialize)(this.trackVolume)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ListView lvDevices;
        private System.Windows.Forms.ColumnHeader colName;
        private System.Windows.Forms.ColumnHeader colDefault;
        private System.Windows.Forms.ColumnHeader colStatus;
        private System.Windows.Forms.ColumnHeader colVolume;
        private System.Windows.Forms.Button btnRefreshDevices;
        private System.Windows.Forms.Button btnToggleDevice;
        private System.Windows.Forms.TrackBar trackVolume;
        private System.Windows.Forms.Label lblVolume;
        private System.Windows.Forms.Label lblVolumePercentage;
        private System.Windows.Forms.Panel audioVisualizer;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.CheckBox checkStartMinimized;
        private System.Windows.Forms.CheckBox checkRunAtStartup;
        private System.Windows.Forms.Button btnSaveSettings;
    }
}
