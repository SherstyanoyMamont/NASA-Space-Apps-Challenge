namespace NASA_Space_Apps_Challenge
{
    partial class Form1
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
        private void InitializeComponent()
        {
            webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            button1 = new Button();
            comboBox1 = new ComboBox();
            trackBar1 = new TrackBar();
            dateTimePicker1 = new DateTimePicker();
            buttonCityForecast = new Button();
            ButtonCityForecast3 = new Button();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBar1).BeginInit();
            SuspendLayout();
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.BackColor = SystemColors.ControlDark;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Dock = DockStyle.Fill;
            webView21.Location = new Point(0, 0);
            webView21.Margin = new Padding(4);
            webView21.Name = "webView21";
            webView21.Size = new Size(1472, 765);
            webView21.TabIndex = 0;
            webView21.ZoomFactor = 1D;
            webView21.Click += webView21_Click;
            // 
            // button1
            // 
            button1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            button1.Location = new Point(1210, 689);
            button1.Margin = new Padding(4);
            button1.Name = "button1";
            button1.Size = new Size(118, 36);
            button1.TabIndex = 1;
            button1.Text = "Start";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // comboBox1
            // 
            comboBox1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(994, 692);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(182, 33);
            comboBox1.TabIndex = 2;
            // 
            // trackBar1
            // 
            trackBar1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            trackBar1.Location = new Point(1129, 12);
            trackBar1.Name = "trackBar1";
            trackBar1.Size = new Size(331, 69);
            trackBar1.TabIndex = 3;
            // 
            // dateTimePicker1
            // 
            dateTimePicker1.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            dateTimePicker1.Location = new Point(651, 694);
            dateTimePicker1.Name = "dateTimePicker1";
            dateTimePicker1.Size = new Size(300, 31);
            dateTimePicker1.TabIndex = 4;
            // 
            // buttonCityForecast
            // 
            buttonCityForecast.Location = new Point(0, 0);
            buttonCityForecast.Name = "buttonCityForecast";
            buttonCityForecast.Size = new Size(75, 23);
            buttonCityForecast.TabIndex = 5;
            // 
            // ButtonCityForecast3
            // 
            ButtonCityForecast3.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            ButtonCityForecast3.Location = new Point(55, 689);
            ButtonCityForecast3.Name = "ButtonCityForecast3";
            ButtonCityForecast3.Size = new Size(163, 34);
            ButtonCityForecast3.TabIndex = 6;
            ButtonCityForecast3.Text = "City Forecast";
            ButtonCityForecast3.UseVisualStyleBackColor = true;
            ButtonCityForecast3.Click += ButtonCityForecast3_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            ClientSize = new Size(1472, 765);
            Controls.Add(ButtonCityForecast3);
            Controls.Add(dateTimePicker1);
            Controls.Add(trackBar1);
            Controls.Add(comboBox1);
            Controls.Add(button1);
            Controls.Add(webView21);
            Controls.Add(buttonCityForecast);
            Margin = new Padding(2);
            Name = "Form1";
            Text = "Form1";
            WindowState = FormWindowState.Maximized;
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBar1).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private Button button1;
        private ComboBox comboBox1;
        private TrackBar trackBar1;
        private DateTimePicker dateTimePicker1;
        private Button buttonCityForecast;
        private Button ButtonCityForecast3;
    }
}
