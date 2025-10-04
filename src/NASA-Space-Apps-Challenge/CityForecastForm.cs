using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace NASA_Space_Apps_Challenge
{
    public partial class CityForecastForm : Form
    {
        private Form1 mainForm;

        public CityForecastForm(Form1 parent)
        {
            InitializeComponent();
            mainForm = parent;
        }

        private async void TextBoxResult_Click(object sender, EventArgs e)
        {

            string city = CityTextBox.Text.Trim();
            if (string.IsNullOrEmpty(city)) return;

            // Используем экземпляр Form1
            string forecast = await mainForm.GetCityPollenForecastAsync(city);
            ForecastBox.Text = forecast;

        }

        private void ForecastBox_TextChanged(object sender, EventArgs e)
        {

        }
    }

}
