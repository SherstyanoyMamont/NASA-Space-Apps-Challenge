namespace NASA_Space_Apps_Challenge
{
    partial class LocalBloomForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            button1 = new Button();
            timelineControl1 = new TimelineWinForms.TimelineControl();
            pbStatus = new ProgressBar();
            lStatus = new Label();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            SuspendLayout();
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            webView21.BackColor = SystemColors.ControlDark;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Location = new Point(12, 51);
            webView21.Name = "webView21";
            webView21.Size = new Size(997, 562);
            webView21.TabIndex = 0;
            webView21.ZoomFactor = 1D;
            // 
            // button1
            // 
            button1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button1.Location = new Point(915, 16);
            button1.Name = "button1";
            button1.Size = new Size(94, 29);
            button1.TabIndex = 1;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // timelineControl1
            // 
            timelineControl1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            timelineControl1.Current = new DateTime(2025, 10, 4, 0, 0, 0, 0);
            timelineControl1.Font = new Font("Segoe UI", 9F);
            timelineControl1.IsPlaying = false;
            timelineControl1.Location = new Point(12, 619);
            timelineControl1.MinimumSize = new Size(420, 60);
            timelineControl1.Name = "timelineControl1";
            timelineControl1.PixelsPerDay = 10D;
            timelineControl1.RangeEnd = new DateTime(2025, 12, 31, 0, 0, 0, 0);
            timelineControl1.RangeStart = new DateTime(2025, 1, 1, 0, 0, 0, 0);
            timelineControl1.Size = new Size(997, 75);
            timelineControl1.StepUnit = TimelineWinForms.TimelineStepUnit.Day;
            timelineControl1.TabIndex = 6;
            // 
            // pbStatus
            // 
            pbStatus.Location = new Point(12, 12);
            pbStatus.Name = "pbStatus";
            pbStatus.Size = new Size(364, 29);
            pbStatus.TabIndex = 7;
            // 
            // lStatus
            // 
            lStatus.AutoSize = true;
            lStatus.Location = new Point(382, 16);
            lStatus.Name = "lStatus";
            lStatus.Size = new Size(69, 20);
            lStatus.TabIndex = 8;
            lStatus.Text = "<Status>";
            // 
            // LocalBloomForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1030, 702);
            Controls.Add(lStatus);
            Controls.Add(pbStatus);
            Controls.Add(timelineControl1);
            Controls.Add(button1);
            Controls.Add(webView21);
            Margin = new Padding(2);
            Name = "LocalBloomForm";
            Text = "Local Bloom Map";
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private Button button1;
        private TimelineWinForms.TimelineControl timelineControl1;
        private ProgressBar pbStatus;
        private Label lStatus;
    }
}
