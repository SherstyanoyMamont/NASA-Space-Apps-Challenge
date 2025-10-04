namespace NASA_Space_Apps_Challenge
{
    partial class CityForecastForm
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
            label1 = new Label();
            labelCity = new Label();
            CityTextBox = new TextBox();
            buttonGetForecast = new Button();
            label3 = new Label();
            TextBoxResult = new Button();
            dateTimePicker1 = new DateTimePicker();
            ForecastBox = new RichTextBox();
            SuspendLayout();
            // 
            // label1
            // 
            label1.Location = new Point(0, 0);
            label1.Name = "label1";
            label1.Size = new Size(100, 23);
            label1.TabIndex = 10;
            // 
            // labelCity
            // 
            labelCity.AutoSize = true;
            labelCity.Font = new Font("Segoe UI", 11F);
            labelCity.Location = new Point(45, 76);
            labelCity.Name = "labelCity";
            labelCity.Size = new Size(313, 30);
            labelCity.TabIndex = 1;
            labelCity.Text = "Enter city name or coordinates:";
            // 
            // CityTextBox
            // 
            CityTextBox.Location = new Point(397, 77);
            CityTextBox.Name = "CityTextBox";
            CityTextBox.Size = new Size(182, 31);
            CityTextBox.TabIndex = 2;
            CityTextBox.Text = "Dallas";
            // 
            // buttonGetForecast
            // 
            buttonGetForecast.Location = new Point(0, 0);
            buttonGetForecast.Name = "buttonGetForecast";
            buttonGetForecast.Size = new Size(75, 23);
            buttonGetForecast.TabIndex = 7;
            // 
            // label3
            // 
            label3.Location = new Point(0, 0);
            label3.Name = "label3";
            label3.Size = new Size(100, 23);
            label3.TabIndex = 9;
            // 
            // TextBoxResult
            // 
            TextBoxResult.Location = new Point(467, 420);
            TextBoxResult.Name = "TextBoxResult";
            TextBoxResult.Size = new Size(112, 34);
            TextBoxResult.TabIndex = 8;
            TextBoxResult.Text = "Get Result";
            TextBoxResult.UseVisualStyleBackColor = true;
            TextBoxResult.Click += TextBoxResult_Click;
            // 
            // dateTimePicker1
            // 
            dateTimePicker1.Location = new Point(33, 423);
            dateTimePicker1.MinDate = new DateTime(2025, 10, 4, 0, 0, 0, 0);
            dateTimePicker1.Name = "dateTimePicker1";
            dateTimePicker1.Size = new Size(300, 31);
            dateTimePicker1.TabIndex = 11;
            dateTimePicker1.Value = new DateTime(2025, 10, 5, 0, 0, 0, 0);
            // 
            // ForecastBox
            // 
            ForecastBox.Location = new Point(45, 186);
            ForecastBox.Name = "ForecastBox";
            ForecastBox.Size = new Size(534, 129);
            ForecastBox.TabIndex = 12;
            ForecastBox.Text = "";
            ForecastBox.TextChanged += ForecastBox_TextChanged;
            // 
            // CityForecastForm
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(623, 491);
            Controls.Add(ForecastBox);
            Controls.Add(dateTimePicker1);
            Controls.Add(TextBoxResult);
            Controls.Add(label3);
            Controls.Add(buttonGetForecast);
            Controls.Add(CityTextBox);
            Controls.Add(labelCity);
            Controls.Add(label1);
            Name = "CityForecastForm";
            Text = "CityForecastForm";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private Label labelCity;
        private TextBox CityTextBox;
        private Button buttonGetForecast;
        private Label label3;
        private Button TextBoxResult;
        private DateTimePicker dateTimePicker1;
        private RichTextBox ForecastBox;
    }
}