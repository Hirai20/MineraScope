using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MineraScope
{
    public partial class EdxCalibrationForm : Form
    {
        public EdxCalibrationForm()
        {
            InitializeComponent();
            // 260514Codex: FormClosing の接続は Designer 側に寄せ、ここでは初期化だけを行います。
        }

        // 260508Codex: EDXキャリブレーション画面は再表示時に入力値を保つため Dispose せず隠します。
        private void EdxCalibrationForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Visible = false;
        }
    }
}
