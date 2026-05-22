using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
// 260424Codex: FormMain 側で共通パスの既定値を組み立てるために IO helper を使います。
using System.IO;
using System.Text;
// 260430Codex: ドロップ後の自動判定を非同期に待つため Task を使います。
using System.Threading.Tasks;
using System.Windows.Forms;
// 260427Codex: スペクトルグラフ用の Profile/PointD 型名を読みやすくします。
using Crystallography;

namespace MineraScope
{
    public partial class FormMain : Form
    {

        // 260508Codex: modeless 子フォームは Load で生成するため、フィールド初期化時点は null 許容を明示的に抑えます。
        public GeneratorForm GeneratorForm = null!;
        public AnalyzerForm AnalyzerForm = null!;

        // 260427Codex: ドロップされたスペクトルの実ファイルパスは表示名とは分けて保持します。
        private string? _spectrumFilePath;

        // 260430Codex: FormMain での自動鉱物判定が重ならないようにします。
        private bool _isPredictionRunning;

        // 260507Codex: モデル一覧の再構築後に前回選択していたモデル名を復元します。
        private string _savedSelectedModelName = string.Empty;

        // 260522Codex: 利用可能モデル一覧の単一の真実源。FormMain と AnalyzerForm が同じインスタンスから描画します。
        private readonly ModelCatalog _modelCatalog = new();
        // 260508Codex: FormMain の設定ファイル名を保存・復元で共有します。
        private const string UserSettingsFileName = "FormMainSettings.json";

        // 260424Codex: モデル保存先は FormMain の共通パス欄から参照します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string ModelPath { get => textBoxlPathSaveModel.Text; set => textBoxlPathSaveModel.Text = value; }

        // 260507Codex: 判定ではモデル群の親フォルダではなく、コンボボックスで選んだ直下のモデルフォルダを使います。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedModelPath
        {
            get
            {
                if (comboBoxModelPath.SelectedItem is not string selectedModelName || string.IsNullOrWhiteSpace(ModelPath))
                    return string.Empty;

                return Path.Combine(ModelPath, selectedModelName);
            }
        }

        // 260424Codex: 生成スペクトル出力先と教師データ参照先は同じフォルダとして親フォームで管理します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EdxOutputPath { get => textBoxPathEDX.Text; set => textBoxPathEDX.Text = value; }

        // 260424Codex: DTSA-II のパスも親フォームが保持し、子フォームはこの値を参照します。
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string DtsaPath { get => textBoxPathDTSA.Text; set => textBoxPathDTSA.Text = value; }

        //public string ParallelNumber { get => SettingForm.ParallelNumber; }


        public FormMain()
        {
            InitializeComponent();
            // 260522Codex: モデル一覧の更新通知でコンボを再描画するよう、設定復元より前に購読します。
            _modelCatalog.Changed += OnModelCatalogChanged;
            // 260507Codex: 共通パス欄は前回終了時の入力値を先に復元します。
            LoadUserSettings();
            // 260424Codex: 共通ファイルパス設定は FormMain 側で初期化して子フォームから参照します。
            InitializeFilePathSettings();
            // 260507Codex: 起動時に既定モデル保存先の直下フォルダをモデル選択欄へ反映します。
            RefreshModelPathList();
            // 260427Codex: フォーム上のどの UI 部品に落としても同じスペクトル入力として扱います。
            EnableSpectrumDropOnChildControls(this);
            // 260507Codex: Designer を触らず、終了時に明示リストの設定だけ保存します。
            FormClosing += FormMain_FormClosing;
        }

        // 260507Codex: ログや spectrum 表示を除き、共通パス欄だけを復元します。
        private void LoadUserSettings()
        {
            var settings = FormUserSettingsStore.Load<FormMainUserSettings>(UserSettingsFileName);
            if (!string.IsNullOrWhiteSpace(settings.ModelPath))
                ModelPath = settings.ModelPath;

            _savedSelectedModelName = settings.SelectedModelName;

            if (!string.IsNullOrWhiteSpace(settings.EdxOutputPath))
                EdxOutputPath = settings.EdxOutputPath;

            if (!string.IsNullOrWhiteSpace(settings.DtsaPath))
                DtsaPath = settings.DtsaPath;
        }

        // 260507Codex: 次回起動時に共通パス欄だけを戻せるよう、終了時に保存します。
        private void FormMain_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // 260508Codex: GeneratorForm が非表示のままでも、アプリ終了時に現在値を保存します。
            GeneratorForm?.SaveUserSettings();

            FormUserSettingsStore.Save(
                UserSettingsFileName,
                new FormMainUserSettings
                {
                    ModelPath = ModelPath,
                    SelectedModelName = comboBoxModelPath.SelectedItem as string ?? string.Empty,
                    EdxOutputPath = EdxOutputPath,
                    DtsaPath = DtsaPath
                });
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            GeneratorForm = new GeneratorForm
            {
                Visible = false,
                FormMain = this
            };

            // 260522Codex: 解析フォームは共有カタログから描画するので、親参照ではなくカタログを渡します。
            AnalyzerForm = new AnalyzerForm
            {
                Visible = false,
                ModelCatalog = _modelCatalog
            };
        }

        // 260513Codex: 共通ファイルパス欄の既定値だけをまとめ、イベント接続は Designer 側へ寄せます。
        private void InitializeFilePathSettings()
        {
            // 260430Codex: 空欄のモデル保存先はユーザーごとの Documents 配下へ初期化します。
            if (string.IsNullOrWhiteSpace(ModelPath))
                ModelPath = DefaultStoragePaths.ModelsFolder;

            // 260430Codex: 空欄の EDX/教師データ保存先はユーザーごとの Documents 配下へ初期化します。
            if (string.IsNullOrWhiteSpace(EdxOutputPath))
                EdxOutputPath = DefaultStoragePaths.TrainingDataFolder;
        }

        private void textBoxlPathSaveModel_TextChanged(object sender, EventArgs e)
        {
            RefreshModelPathList();
        }

        // 260424Codex: FormMain 上の 3 つのパス欄からフォルダ選択を行います。
        private void buttonFilePathBrowse_Click(object? sender, EventArgs e)
        {
            TextBox? targetTextBox = sender switch
            {
                Button button when button == buttonPathSaveMode => textBoxlPathSaveModel,
                // 260507Codex: ボタン名と保存先 TextBox の対応を名前通りに揃えます。
                Button button when button == buttonPathDTSA => textBoxPathDTSA,
                Button button when button == buttonPathEDX => textBoxPathEDX,
                _ => null
            };

            if (targetTextBox is null)
                return;

            if (!FolderSelectionHelper.TrySelectFolder(targetTextBox))
                return;

            if (targetTextBox == textBoxlPathSaveModel)
            {
                // 260508Codex: モデル保存先を参照変更した直後に、直下フォルダのモデル候補を更新します。
                RefreshModelPathList();
            }
        }

        // 260507Codex: モデル保存先フォルダの直下にある各フォルダを、使用モデルとして選べるようにします。
        // 260508Codex: GeneratorForm の学習完了後にも、作成されたモデル名を選択状態で一覧更新できるようにします。
        public void RefreshModelPathList(string preferredModelName = "")
            => _modelCatalog.Update(ModelPath, preferredModelName);

        // 260522Codex: カタログ更新時に自フォームのモデル選択コンボを再描画します（前回選択 → 保存済み名でフォールバック）。
        private void OnModelCatalogChanged(object? sender, ModelCatalogChangedEventArgs e)
        {
            string previousSelection = comboBoxModelPath.SelectedItem as string ?? string.Empty;
            string target = !string.IsNullOrWhiteSpace(e.PreferredModelName) ? e.PreferredModelName
                : !string.IsNullOrWhiteSpace(previousSelection) ? previousSelection
                : _savedSelectedModelName;

            ModelComboBinder.Populate(comboBoxModelPath, _modelCatalog.ModelNames, target);
        }

        private void buttonOpenGenerator_Click(object sender, EventArgs e)
        {
            if (GeneratorForm.Visible)
                GeneratorForm.BringToFront();
            else
            {
                GeneratorForm.Visible = true;
            }
        }

        private void buttonOpenAnalyzer_Click(object sender, EventArgs e)
        {
            // 260522Codex: 開く直前にカタログを再走査し、ディスク上で増えたモデルフォルダを両コンボへ反映します。
            _modelCatalog.Update(ModelPath);

            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに AnalyzerForm を残します。
            if (AnalyzerForm.Visible)
                AnalyzerForm.BringToFront();
            else
                AnalyzerForm.Visible = true;
        }

        // 260430Codex: 判定中は追加ドロップを受け付けず、単一スペクトルだけをコピー可能にします。
        private void FormMain_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = TryGetAcceptedSpectrumFile(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        // 260430Codex: スペクトル表示が成功したら FormMain 上で鉱物判定を自動実行します。
        private async void FormMain_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetAcceptedSpectrumFile(e, out var filePath))
                return;

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
                return;
            }

            await RunMineralPredictionAsync(filePath);
        }

        // 260430Codex: 判定中の再入力と単一スペクトル以外のドロップを同じ条件で拒否します。
        private bool TryGetAcceptedSpectrumFile(DragEventArgs e, out string filePath)
        {
            if (_isPredictionRunning)
            {
                filePath = string.Empty;
                return false;
            }

            return TryGetSingleDroppedSpectrumFile(e, out filePath);
        }

        // 260430Codex: FormMain の判定ログを結果欄へ追記します。
        private void AnalysisLog(string message)
            => TextBoxLogHelper.AppendLine(textBoxAnalysisResult, message);

        // 260430Codex: ドロップされた単一スペクトルを FormMain の結果欄へ自動判定します。
        private async Task RunMineralPredictionAsync(string filePath)
        {
            if (_isPredictionRunning)
                return;

            _isPredictionRunning = true;
            textBoxAnalysisResult.Clear();
            AnalysisLog("判定中...");

            try
            {
                // 260507Codex: 親フォルダだけでなく、comboBoxModelPath で選択されたモデルフォルダまで確認します。
                string selectedModelPath = SelectedModelPath;
                if (string.IsNullOrWhiteSpace(selectedModelPath))
                {
                    AnalysisLog("使用するモデルフォルダが選択されていません。");
                    return;
                }

                await new MineralPredictionWorkflow(AppContext.BaseDirectory, AnalysisLog)
                    // 260507Codex: 選択中のモデルフォルダを予測ワークフローへ渡します。
                    .RunAsync(selectedModelPath, new[] { filePath });
            }
            catch (Exception ex)
            {
                AnalysisLog($"\nエラー: {ex.Message}");
                AnalysisLog($"スタックトレース: {ex.StackTrace}");
            }
            finally
            {
                _isPredictionRunning = false;
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
                return false;

            if (dataObject.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
                return false;

            if (!File.Exists(files[0]) || !IsSpectrumFile(files[0]))
                return false;

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
                throw new InvalidDataException("スペクトルデータ点が見つかりませんでした。");

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
                    continue;

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    UpdateSpectrumAxisHeader(line, ref xPerChannel, ref offset);
                    continue;
                }

                if (!TryReadSpectrumPoint(line, points.Count, xPerChannel, offset, out var point))
                    continue;

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
                return false;

            if (!double.TryParse(values[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double firstValue))
                return false;

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
                offset = parsedOffset;
        }

        // 260427Codex: "#KEY : value" 形式のヘッダー値を InvariantCulture で読み取ります。
        private static bool TryReadHeaderDouble(string line, string key, out double value)
        {
            value = 0.0;

            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                return false;

            int separatorIndex = line.IndexOf(':');

            if (separatorIndex < 0 || separatorIndex + 1 >= line.Length)
                return false;

            string text = line[(separatorIndex + 1)..].Trim().TrimEnd('.');
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
