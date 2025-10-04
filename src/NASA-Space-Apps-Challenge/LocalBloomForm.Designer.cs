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
            bOpenMap = new Button();
            timelineControl1 = new TimelineWinForms.TimelineControl();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            SuspendLayout();
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Location = new Point(12, 47);
            webView21.Name = "webView21";
            webView21.Size = new Size(997, 566);
            webView21.TabIndex = 0;
            webView21.ZoomFactor = 1D;
            // 
            // button1
            // 
            button1.Location = new Point(874, 12);
            button1.Name = "button1";
            button1.Size = new Size(94, 29);
            button1.TabIndex = 1;
            button1.Text = "button1";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // bOpenMap
            // 
            bOpenMap.Location = new Point(12, 12);
            bOpenMap.Name = "bOpenMap";
            bOpenMap.Size = new Size(94, 29);
            bOpenMap.TabIndex = 3;
            bOpenMap.Text = "Map";
            bOpenMap.UseVisualStyleBackColor = true;
            bOpenMap.Click += bOpenMap_Click;
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
            // LocalBloomMap
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1030, 702);
            Controls.Add(timelineControl1);
            Controls.Add(bOpenMap);
            Controls.Add(button1);
            Controls.Add(webView21);
            Margin = new Padding(2);
            Name = "LocalBloomMap";
            Text = "Local Bloom Map";
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private Button button1;
        private Button bOpenMap;
        private TimelineWinForms.TimelineControl timelineControl1;
    }
}
