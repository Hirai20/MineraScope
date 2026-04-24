using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MineraScope
{
    // 260416Codex: メイン起動フォーム名を FormMain に統一します。
    public partial class FormMain : Form
    {

        public GeneratorForm GeneratorForm;
        public AnalyzerForm AnalyzerForm;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ModelPath { get => textBoxSaveModelPath.Text; set => textBoxSaveModelPath.Text = value; }

        //public string ParallelNumber { get => SettingForm.ParallelNumber; }


        // 260416Codex: クラス名変更に合わせてコンストラクタ名も更新します。
        public FormMain()
        {
            InitializeComponent();
        }

        // 260416Codex: Load ハンドラー名も FormMain に合わせます。
        private void FormMain_Load(object sender, EventArgs e)
        {
            // 260416Codex: 子フォームから親を参照する名前も FormMain にそろえます。
            GeneratorForm = new GeneratorForm
            {
                Visible = false,
                FormMain = this
            };

            // 260416Codex: 解析フォーム側の親参照も同じ名前へ統一します。
            AnalyzerForm = new AnalyzerForm
            {
                Visible = false,
                FormMain = this
            };
        }

        private void buttonOpenGenerator_Click(object sender, EventArgs e)
        {
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに GeneratorForm を残します。
            //var form = new GeneratorForm();
            //form.Show(this);
            if (GeneratorForm.Visible)
                GeneratorForm.BringToFront();
            else
                GeneratorForm.Visible = true;

        }

        private void buttonOpenAnalyzer_Click(object sender, EventArgs e)
        {
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに AnalyzerForm を残します。
            if (AnalyzerForm.Visible)
                AnalyzerForm.BringToFront();
            else
                AnalyzerForm.Visible = true;
        }

  
    }
}
