using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
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

        // 260430Codex: FormMain での自動鉱物判定が重ならないようにします。
        private bool _isPredictionRunning;

        // 260620Codex: Keep the dropped input set so changing the selected model can rerun the same batch.
        private string[] _droppedSpectrumFiles = [];
        private bool _isModelComboRefreshing;

        // 260621Claude: デフォルトのエクスポート名を「ドロップしたフォルダ名/ファイル名」から作るため、生のドロップパスを保持する。
        private string[] _droppedRawPaths = [];

        // 260620Claude: ドロップ済みスペクトルの分類結果（真実源）。combo 選択表示・エクスポートはここから作る。
        private SpectrumPredictionBatch? _currentBatch;

        // 260620Claude: グラフ用 Profile は選択時に lazy load してキャッシュする。
        private readonly Dictionary<string, Profile> _profileCache = new(StringComparer.OrdinalIgnoreCase);

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
            ControlDropHelper.EnableRecursive(this, FormMain_DragEnter, FormMain_DragDrop);
            // 260507Codex: Designer を触らず、終了時に明示リストの設定だけ保存します。
            FormClosing += FormMain_FormClosing;
        }

        // 260507Codex: ログや spectrum 表示を除き、共通パス欄だけを復元します。
        private void LoadUserSettings()
        {
            var settings = FormUserSettingsStore.Load<FormMainUserSettings>(UserSettingsFileName);
            // 260621Codex: Restore the selected model name before ModelPath triggers catalog refresh through TextChanged.
            _savedSelectedModelName = settings.SelectedModelName;

            if (!string.IsNullOrWhiteSpace(settings.ModelPath))
                ModelPath = settings.ModelPath;

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
            // 260716Claude: AnalyzerForm も同様に、非表示のままのアプリ終了時にマッピングモデル選択を保存する。
            AnalyzerForm?.SaveUserSettings();

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

            // 260620Claude: 起動直後はまだ分類結果が無いので、エクスポートはドロップ＆分類成功まで無効にする。
            exportToolStripMenuItem.Enabled = false;
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

            // 260626Codex: Show dtsa2.msi's per-user install root only when no saved/user path exists.
            if (string.IsNullOrWhiteSpace(DtsaPath))
                DtsaPath = DtsaMsiInstallation.DefaultFolder;

            // 260716Codex: msi の既定インストール先を示す補足は、変更後のパスにも残るため表示しない。
            labelPathDTSA.Text = "DTSA-IIパス";
            textBoxPathDTSA.PlaceholderText = "dtsa2.msi の既定インストール先。別の場所なら変更可";
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

            // 260620Codex: Catalog refresh can change selection internally; only user model changes should rerun analysis.
            _isModelComboRefreshing = true;
            try
            {
                ModelComboBinder.Populate(comboBoxModelPath, _modelCatalog.ModelNames, target);
            }
            finally
            {
                _isModelComboRefreshing = false;
            }
        }

        private void buttonOpenGenerator_Click(object sender, EventArgs e)
            => ShowChildForm(GeneratorForm);

        private void buttonOpenAnalyzer_Click(object sender, EventArgs e)
        {
            // 260522Codex: 開く直前にカタログを再走査し、ディスク上で増えたモデルフォルダを両コンボへ反映します。
            _modelCatalog.Update(ModelPath);
            // 260416Codex: modeless 表示では using を外し、呼び出し元をブロックせずに AnalyzerForm を残します。
            ShowChildForm(AnalyzerForm);
        }

        // 260612Claude: 一度閉じた(非表示にした)後でも確実に再表示できるよう、表示・最小化解除・最前面化をまとめて行う。
        private static void ShowChildForm(Form form)
        {
            form.Visible = true;
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        // 260621Codex: Keep the spectrum dropdown cap in sync with the current form width.
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            AdjustSpectrumFileDropDownWidth();
        }

        // 260620Claude: 判定中の追加ドロップは拒否し、msa/emsa/eds（フォルダ再帰含む）を複数受け付ける。
        private void FormMain_DragEnter(object? sender, DragEventArgs e)
        {
            e.Effect = TryGetDroppedSpectrumFiles(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        // 260620Claude: ドロップされた全スペクトルを選択モデルで一括分類し、combo で閲覧・エクスポートできるようにする。
        private async void FormMain_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetDroppedSpectrumFiles(e, out var files))
                return;

            // 260620Codex: Remember exactly what was dropped so model changes can rerun without another drop.
            _droppedSpectrumFiles = files;
            // 260621Claude: デフォルトのエクスポート名生成用に、ユーザーが実際にドロップした項目（フォルダ/ファイル）を覚えておく。
            _droppedRawPaths = (e.Data?.GetData(DataFormats.FileDrop) as string[]) ?? [];
            await RunBatchPredictionAsync(_droppedSpectrumFiles);
        }

        // 260620Claude: 判定中の再入力を拒否し、ドロップ（ファイル/フォルダ）から対象スペクトルを再帰収集する。
        private bool TryGetDroppedSpectrumFiles(DragEventArgs e, out string[] files)
        {
            files = [];

            if (_isPredictionRunning)
                return false;

            if (e.Data is not IDataObject data || !data.GetDataPresent(DataFormats.FileDrop))
                return false;

            if (data.GetData(DataFormats.FileDrop) is not string[] paths)
                return false;

            files = MineralPredictionWorkflow.CollectSpectrumFiles(paths);
            return files.Length > 0;
        }

        // 260430Codex: FormMain の判定ログを結果欄へ追記します。
        private void AnalysisLog(string message)
            => TextBoxLogHelper.AppendLine(textBoxAnalysisResult, message);

        // 260620Claude: ドロップされた複数スペクトルを一括分類し、結果を combo へ bind して先頭を表示する。
        private async Task RunBatchPredictionAsync(IReadOnlyList<string> files, string preferredFilePath = "")
        {
            if (_isPredictionRunning || files.Count == 0)
                return;

            string selectedModelPath = SelectedModelPath;
            string modelName = comboBoxModelPath.SelectedItem as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedModelPath))
            {
                AnalysisLog("使用するモデルフォルダが選択されていません。");
                return;
            }

            _isPredictionRunning = true;
            textBoxAnalysisResult.Clear();
            _profileCache.Clear();
            _currentBatch = null;
            comboBoxSpectrumFile.Items.Clear();
            // 260620Claude: 分類中はエクスポート不可。完了後に成功があれば有効化する。
            exportToolStripMenuItem.Enabled = false;

            try
            {
                // 260620Claude: 先頭ファイルは分類完了を待たずに即グラフ表示する。
                ShowSpectrumProfile(files[0]);

                var batch = await new SpectrumBatchPredictionWorkflow(AppContext.BaseDirectory, AnalysisLog)
                    .RunAsync(selectedModelPath, modelName, files);

                _currentBatch = batch;
                BindBatchToCombo(batch, preferredFilePath);
                exportToolStripMenuItem.Enabled = batch.Items.Any(item => item.IsSuccess);
            }
            catch (Exception ex)
            {
                AnalysisLog($"\nエラー: {ex.Message}");
            }
            finally
            {
                _isPredictionRunning = false;
            }
        }

        // 260621Codex: combo へファイル名だけを bind し、内部参照は SelectedIndex→batch.Items[index] のまま保つ。
        private void BindBatchToCombo(SpectrumPredictionBatch batch, string preferredFilePath = "")
        {
            comboBoxSpectrumFile.BeginUpdate();
            comboBoxSpectrumFile.Items.Clear();
            foreach (var item in batch.Items)
                comboBoxSpectrumFile.Items.Add(item.FileName);
            comboBoxSpectrumFile.EndUpdate();
            AdjustSpectrumFileDropDownWidth();

            if (comboBoxSpectrumFile.Items.Count == 0)
                return;

            int preferredIndex = FindPredictionItemIndex(batch.Items, preferredFilePath);
            comboBoxSpectrumFile.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
            ShowSelectedItem();
        }

        // 260620Codex: After model reanalysis, keep the same spectrum selected when it is still in the batch.
        private static int FindPredictionItemIndex(IReadOnlyList<SpectrumPredictionItem> items, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return -1;

            for (int i = 0; i < items.Count; i++)
                if (string.Equals(items[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    return i;

            return -1;
        }

        // 260621Codex: Let the dropdown fit long labels, but cap it at the current form width.
        private void AdjustSpectrumFileDropDownWidth()
        {
            if (comboBoxSpectrumFile is null)
                return;

            int requiredWidth = comboBoxSpectrumFile.Width;
            foreach (object item in comboBoxSpectrumFile.Items)
            {
                int itemWidth = TextRenderer.MeasureText(
                    item.ToString() ?? string.Empty,
                    comboBoxSpectrumFile.Font).Width;
                requiredWidth = Math.Max(requiredWidth, itemWidth);
            }

            int paddedWidth = requiredWidth + SystemInformation.VerticalScrollBarWidth + 12;
            int maxWidth = Math.Max(comboBoxSpectrumFile.Width, ClientSize.Width);
            comboBoxSpectrumFile.DropDownWidth = Math.Min(paddedWidth, maxWidth);
        }

        // 260620Claude: combo 選択でスペクトルと結果を切り替える（Designer で SelectedIndexChanged を接続）。
        private void comboBoxSpectrumFile_SelectedIndexChanged(object sender, EventArgs e)
            => ShowSelectedItem();

        // 260620Codex: Reanalyze the dropped spectra when the user switches models.
        private async void comboBoxModelPath_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isModelComboRefreshing || _droppedSpectrumFiles.Length == 0 || _isPredictionRunning)
                return;

            string selectedFilePath = GetSelectedPredictionFilePath();
            await RunBatchPredictionAsync(_droppedSpectrumFiles, selectedFilePath);
        }

        // 260620Codex: Read the current batch selection before a rerun clears the combo.
        private string GetSelectedPredictionFilePath()
        {
            if (_currentBatch is null)
                return string.Empty;

            int index = comboBoxSpectrumFile.SelectedIndex;
            return index >= 0 && index < _currentBatch.Items.Count
                ? _currentBatch.Items[index].FilePath
                : string.Empty;
        }

        // 260620Claude: combo 選択中スペクトルのグラフと結果ブロックを表示する。
        private void ShowSelectedItem()
        {
            if (_currentBatch is null)
                return;

            int index = comboBoxSpectrumFile.SelectedIndex;
            if (index < 0 || index >= _currentBatch.Items.Count)
                return;

            var item = _currentBatch.Items[index];
            ShowSpectrumProfile(item.FilePath);
            textBoxAnalysisResult.Text = SpectrumPredictionBlockFormatter.FormatBody(item);
        }

        // 260620Claude: 指定ファイルの Profile を lazy load + cache してグラフへ表示する。読めない場合は何もしない。
        private void ShowSpectrumProfile(string filePath)
        {
            if (!TryGetRawProfile(filePath, out var profile))
                return;

            // 260630Claude: キャッシュは生プロファイルのまま保持し、表示時に選択モデルのマスクを掛けたコピーを描く。
            //   モデルを切り替えても再キャッシュ無しで反映でき、エクスポート等の生データ参照とも分離できる。
            graphControl1.Profile = ApplyDisplayMask(profile);
            graphControl1.Refresh();
        }

        // 260716Claude: 生 Profile の lazy load + cache を表示と CSV エクスポートで共用する。読めない/空なら false。
        private bool TryGetRawProfile(string filePath, out Profile profile)
        {
            if (_profileCache.TryGetValue(filePath, out profile!))
                return true;

            try
            {
                profile = ReadSpectrumProfile(filePath);
            }
            catch
            {
                return false;
            }

            if (profile.Pt.Count == 0)
                return false;

            _profileCache[filePath] = profile;
            return true;
        }

        // 260630Claude: マスクあり(NoCarbon)モデル選択時は、表示スペクトルの低エネルギー範囲(先頭チャンネル)も 0 にして、
        //   C 領域が実際にゼロ化されているのを目視確認できるようにする。マスク無し/モデル未選択なら生プロファイルをそのまま返す。
        //   マスクはチャンネル番号基準(=点の並び順)なので、モデル入力 (ZeroLeadingChannels) と同じ範囲が 0 になる。
        private Profile ApplyDisplayMask(Profile rawProfile)
        {
            string modelPath = SelectedModelPath;
            var preprocessing = string.IsNullOrWhiteSpace(modelPath)
                ? SpectrumPreprocessing.None
                : SpectrumPreprocessing.LoadFromModelFolder(Path.Combine(modelPath, "AllMinerals_Classification"));
            if (!preprocessing.HasLowEnergyMask)
                return rawProfile;

            int maskCount = Math.Min(preprocessing.MaskChannelCount, rawProfile.Pt.Count);
            var points = new List<PointD>(rawProfile.Pt.Count);
            for (int i = 0; i < rawProfile.Pt.Count; i++)
                points.Add(i < maskCount ? new PointD(rawProfile.Pt[i].X, 0) : rawProfile.Pt[i]);

            return new Profile { Pt = points };
        }

        // 260427Codex: EMSA/MSA の Y データを読み、ヘッダーがあればエネルギー軸へ変換します。
        // 260613Claude: バイナリ .eds はテキストとして読めないため専用パーサで Profile を組み立てる。
        private static Profile ReadSpectrumProfile(string filePath)
        {
            if (EdsSpectrumReader.IsEdsFile(filePath))
                return ReadEdsSpectrumProfile(filePath);

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

        // 260613Claude: .eds の 2048ch カウント列を 10 eV/ch の energy 軸でグラフ点へ変換する (.msa の eV 表示と同じスケール)。
        private static Profile ReadEdsSpectrumProfile(string filePath)
        {
            int[]? counts = EdsSpectrumReader.TryReadCounts(filePath);
            if (counts is null)
                throw new InvalidDataException(".eds スペクトルを読み込めませんでした。");

            var points = new List<PointD>(counts.Length);
            for (int channel = 0; channel < counts.Length; channel++)
                points.Add(new PointD(channel * EdsSpectrumReader.EnergyPerChannelEv, counts[channel]));

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

            if (!double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double firstValue))
                return false;

            if (values.Length >= 2
                && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double secondValue))
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
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // 260620Claude: 読み込んだ全スペクトルの予測結果を、分析用 CSV と閲覧用 TXT の2ファイルへ書き出す。
        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_currentBatch is null || !_currentBatch.Items.Any(item => item.IsSuccess))
            {
                MessageBox.Show(
                    "エクスポートできる分類結果がありません。先にスペクトルをドロップしてください。",
                    "エクスポート", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                // 260620Claude: 「ファイルの種類」で CSV か TXT を選ぶ。選択は FilterIndex で判定する。
                Filter = "CSV (*.csv)|*.csv|TXT (*.txt)|*.txt",
                FilterIndex = 1,
                FileName = SpectrumPredictionExporter.BuildDefaultFileName(_currentBatch.ModelName, _droppedRawPaths)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            // 260620Claude: 選んだ拡張子に依らず base 名から .csv/.txt を組み立て、FilterIndex で出力対象を決める。
            string basePath = Path.Combine(
                Path.GetDirectoryName(dialog.FileName) ?? string.Empty,
                Path.GetFileNameWithoutExtension(dialog.FileName));
            // 260621Codex: The dialog writes one selected format, so keep one output path instead of a list.
            bool writeCsv = dialog.FilterIndex == 1;
            string outputPath = basePath + (writeCsv ? ".csv" : ".txt");

            // 260705Codex: CSV exports use the normal SaveFileDialog overwrite flow again.
            try
            {
                if (writeCsv)
                    SpectrumPredictionExporter.WriteCsv(outputPath, _currentBatch);
                else
                    SpectrumPredictionExporter.WriteReport(outputPath, _currentBatch);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"エクスポートに失敗しました。\r\n{ex.Message}",
                    "エクスポート", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int total = _currentBatch.Items.Count;
            int success = _currentBatch.Items.Count(item => item.IsSuccess);
            string summary = success == total
                ? $"{success} 件を出力しました。"
                : $"{success}/{total} 件を出力しました（{total - success} 件は失敗のため除外）。";
            MessageBox.Show(
                $"{summary}\r\n\r\n{outputPath}",
                "エクスポート", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // 260716Claude: 選択中（グラフ表示中）のスペクトル1本を、マスク適用前の生カウントで CSV 出力する（Designer で Click を接続）。
        private void exportSpectrumCsvToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string filePath = GetSelectedPredictionFilePath();
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show(
                    "エクスポートできるスペクトルがありません。先にスペクトルをドロップしてください。",
                    "スペクトルCSV出力", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryGetRawProfile(filePath, out var profile))
            {
                MessageBox.Show(
                    $"スペクトルを読み込めませんでした。\r\n{filePath}",
                    "スペクトルCSV出力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = Path.GetFileNameWithoutExtension(filePath)
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                SpectrumCsvExporter.Write(dialog.FileName, "Energy (eV)", profile.Pt.Select(p => (p.X, p.Y)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"エクスポートに失敗しました。\r\n{ex.Message}",
                    "スペクトルCSV出力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

    }
}
