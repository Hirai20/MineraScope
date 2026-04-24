using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
// 260424Codex: FormMain 側で共通パスの既定値を組み立てるために IO helper を使います。
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MineraScope
{
    // 260416Codex: メイン起動フォーム名を FormMain に統一します。
    public partial class FormMain : Form
    {

        public GeneratorForm GeneratorForm;
        public AnalyzerForm AnalyzerForm;

        // 260424Codex: モデル保存先は FormMain の共通パス欄から参照します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ModelPath { get => textBoxlPathSaveMode.Text; set => textBoxlPathSaveMode.Text = value; }

        // 260424Codex: 生成スペクトル出力先と教師データ参照先は同じフォルダとして親フォームで管理します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EdxOutputPath { get => textBoxPathEDX.Text; set => textBoxPathEDX.Text = value; }

        // 260424Codex: GeneratorForm から教師データ欄を消すため、教師データパスは EDX 出力先と共有します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string TeacherDataPath => EdxOutputPath;

        // 260424Codex: DTSA-II のパスも親フォームが保持し、子フォームはこの値を参照します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string DtsaPath { get => textBoxPathDTSA.Text; set => textBoxPathDTSA.Text = value; }

        //public string ParallelNumber { get => SettingForm.ParallelNumber; }


        // 260416Codex: クラス名変更に合わせてコンストラクタ名も更新します。
        public FormMain()
        {
            InitializeComponent();
            // 260424Codex: 共通ファイルパス設定は FormMain 側で初期化して子フォームから参照します。
            InitializeFilePathSettings();
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
            // 260424Codex: 親フォーム設定を割り当てたあと、教師データ一覧を最新パスで初期化します。
            GeneratorForm.RefreshTrainingMineralListFromMain();

            // 260416Codex: 解析フォーム側の親参照も同じ名前へ統一します。
            AnalyzerForm = new AnalyzerForm
            {
                Visible = false,
                FormMain = this
            };
        }

        // 260424Codex: 共通ファイルパス欄の既定値とフォルダ選択イベントをまとめます。
        private void InitializeFilePathSettings()
        {
            if (string.IsNullOrWhiteSpace(EdxOutputPath))
            {
                EdxOutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TrainingData");
            }

            buttonPathSaveMode.Click += buttonFilePathBrowse_Click;
            buttonPathDTSA.Click += buttonFilePathBrowse_Click;
            buttonPathEDX.Click += buttonFilePathBrowse_Click;
        }

        // 260424Codex: FormMain 上の 3 つのパス欄からフォルダ選択を行います。
        private void buttonFilePathBrowse_Click(object? sender, EventArgs e)
        {
            TextBox? targetTextBox = sender switch
            {
                Button button when button == buttonPathSaveMode => textBoxlPathSaveMode,
                Button button when button == buttonPathDTSA => textBoxPathEDX,
                Button button when button == buttonPathEDX => textBoxPathDTSA,
                _ => null
            };

            if (targetTextBox is null)
            {
                return;
            }

            if (FolderSelectionHelper.TrySelectFolder(targetTextBox))
            {
                GeneratorForm?.RefreshTrainingMineralListFromMain();
            }
        }

        private void buttonOpenGenerator_Click(object sender, EventArgs e)
        {
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに GeneratorForm を残します。
            //var form = new GeneratorForm();
            //form.Show(this);
            // 260424Codex: 手入力された共通パスも開く直前に GeneratorForm の教師データ一覧へ反映します。
            GeneratorForm.RefreshTrainingMineralListFromMain();
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
