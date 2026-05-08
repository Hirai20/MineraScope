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
            // 260508Codex: 閉じる操作を非表示化へ変換し、未確定の設定値をフォーム内に残します。
            FormClosing += EdxCalibrationForm_FormClosing;
        }

        // 260508Codex: EDXキャリブレーション画面は再表示時に入力値を保つため Dispose せず隠します。
        private void EdxCalibrationForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Visible = false;
        }
    }
}
