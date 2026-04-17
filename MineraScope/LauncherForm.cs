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
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに Form1 を残します。
            var form = new Form1();
            form.Show(this);
        }

        private void buttonOpenAnalyzer_Click(object sender, EventArgs e)
        {
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに AnalyzerForm を残します。
            var form = new AnalyzerForm();
            form.Show(this);
        }
    }
}
