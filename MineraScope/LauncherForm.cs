using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MineraScope
{
    public partial class LauncherForm : Form
    {
        public LauncherForm()
        {
            InitializeComponent();
        }

        private void buttonOpenGenerator_Click(object sender, EventArgs e)
        {
            // 260416Codex: Generator 導線では既存の Form1 を開く
            using var form = new Form1();
            form.ShowDialog(this);
        }

        private void buttonOpenAnalyzer_Click(object sender, EventArgs e)
        {
            // 260416Codex: Analyzer 導線では AnalyzerForm を開く
            using var form = new AnalyzerForm();
            form.ShowDialog(this);
        }
    }
}
