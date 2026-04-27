using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
// 260424Codex: FormMain 側で共通パスの既定値を組み立てるために IO helper を使います。
using System.IO;
using System.Text;
using System.Windows.Forms;
// 260427Codex: スペクトルグラフ用の Profile/PointD 型名を読みやすくします。
using Crystallography;

namespace MineraScope
{
    // 260416Codex: メイン起動フォーム名を FormMain に統一します。
    public partial class FormMain : Form
    {

        public GeneratorForm GeneratorForm;
        public AnalyzerForm AnalyzerForm;

        // 260427Codex: ドロップされたスペクトルの実ファイルパスは表示名とは分けて保持します。
        private string? _spectrumFilePath;

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


        public FormMain()
        {
            InitializeComponent();
            // 260424Codex: 共通ファイルパス設定は FormMain 側で初期化して子フォームから参照します。
            InitializeFilePathSettings();
            // 260427Codex: フォーム上のどの UI 部品に落としても同じスペクトル入力として扱います。
            EnableSpectrumDropOnChildControls(this);
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
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

        private void FormMain_DragEnter(object? sender, DragEventArgs e)
        {
            // 260427Codex: 単一の msa/emsa ファイルだけをコピー操作として受け付けます。
            e.Effect = TryGetSingleDroppedSpectrumFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void FormMain_DragDrop(object? sender, DragEventArgs e)
        {
            // 260427Codex: DragEnter で拒否済みの入力はここでは処理せず、通常は有効ファイルだけを読み込みます。
            if (!TryGetSingleDroppedSpectrumFile(e, out var filePath))
            {
                return;
            }

            try
            {
                LoadSpectrumFile(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"スペクトルを読み込めませんでした。\r\n{ex.Message}",
                    "スペクトルファイル",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // 260427Codex: 子コントロール上でも FormMain と同じドラッグ＆ドロップ処理を通します。
        private void EnableSpectrumDropOnChildControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.AllowDrop = true;
                control.DragEnter += FormMain_DragEnter;
                control.DragDrop += FormMain_DragDrop;
                EnableSpectrumDropOnChildControls(control);
            }
        }

        // 260427Codex: ドロップ入力は「存在する単一ファイル」かつ「msa/emsa」のみに絞ります。
        private static bool TryGetSingleDroppedSpectrumFile(DragEventArgs e, out string filePath)
        {
            filePath = string.Empty;

            // 260427Codex: ドロップデータが取れないケースを先に除外して Null 警告と実行時例外を避けます。
            IDataObject? dataObject = e.Data;

            if (dataObject is null || !dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                return false;
            }

            if (dataObject.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
            {
                return false;
            }

            if (!File.Exists(files[0]) || !IsSpectrumFile(files[0]))
            {
                return false;
            }

            filePath = files[0];
            return true;
        }

        // 260427Codex: 現時点の FormMain 判定入力は msa/emsa の単一スペクトルに限定します。
        private static bool IsSpectrumFile(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".msa", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".emsa", StringComparison.OrdinalIgnoreCase);
        }

        // 260427Codex: 読み込んだスペクトルをファイル名表示とグラフ表示へ反映します。
        private void LoadSpectrumFile(string filePath)
        {
            var profile = ReadSpectrumProfile(filePath);

            if (profile.Pt.Count == 0)
            {
                throw new InvalidDataException("スペクトルデータ点が見つかりませんでした。");
            }

            _spectrumFilePath = filePath;
            textBoxSpectrumFile.Text = Path.GetFileName(filePath);
            graphControl1.Profile = profile;
            graphControl1.Refresh();
        }

        // 260427Codex: EMSA/MSA の Y データを読み、ヘッダーがあればエネルギー軸へ変換します。
        private static Profile ReadSpectrumProfile(string filePath)
        {
            double xPerChannel = 1.0;
            double offset = 0.0;
            var points = new List<PointD>();

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    UpdateSpectrumAxisHeader(line, ref xPerChannel, ref offset);
                    continue;
                }

                if (!TryReadSpectrumPoint(line, points.Count, xPerChannel, offset, out var point))
                {
                    continue;
                }

                points.Add(point);
            }

            return new Profile { Pt = points };
        }

        // 260427Codex: 1列の Y データと、2列の X/Y データの両方をグラフ点へ変換します。
        private static bool TryReadSpectrumPoint(
            string line,
            int channelIndex,
            double xPerChannel,
            double offset,
            out PointD point)
        {
            point = new PointD();

            string[] values = line.TrimEnd(',').Split(
                [' ', '\t', ','],
                StringSplitOptions.RemoveEmptyEntries);

            if (values.Length == 0)
            {
                return false;
            }

            if (!double.TryParse(values[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double firstValue))
            {
                return false;
            }

            if (values.Length >= 2
                && double.TryParse(values[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double secondValue))
            {
                point = new PointD(firstValue, secondValue);
                return true;
            }

            double energy = offset + channelIndex * xPerChannel;
            point = new PointD(energy, firstValue);
            return true;
        }

        // 260427Codex: EMSA ヘッダーの XPERCHAN/OFFSET だけを拾い、グラフの X 軸に使います。
        private static void UpdateSpectrumAxisHeader(string line, ref double xPerChannel, ref double offset)
        {
            if (TryReadHeaderDouble(line, "#XPERCHAN", out double parsedXPerChannel))
            {
                xPerChannel = parsedXPerChannel;
                return;
            }

            if (TryReadHeaderDouble(line, "#OFFSET", out double parsedOffset))
            {
                offset = parsedOffset;
            }
        }

        // 260427Codex: "#KEY : value" 形式のヘッダー値を InvariantCulture で読み取ります。
        private static bool TryReadHeaderDouble(string line, string key, out double value)
        {
            value = 0.0;

            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int separatorIndex = line.IndexOf(':');

            if (separatorIndex < 0 || separatorIndex + 1 >= line.Length)
            {
                return false;
            }

            string text = line[(separatorIndex + 1)..].Trim().TrimEnd('.');
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
