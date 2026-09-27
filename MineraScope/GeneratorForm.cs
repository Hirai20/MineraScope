// 260727Claude: 手書きの Form 本体なので、誤って付いていた「生成コード」マーカーのコメントを外す
//   (付いたままだとアナライザーがこのファイルを生成コード扱いで素通しし、未使用 using や死んだメンバーを
//    検出できず、nullable 注釈コンテキストも無効になって CS8669 が出る)。
//   ★このコメントにマーカーの文字列そのものを書かないこと。Roslyn は先頭コメント内の文字列だけで
//    生成コードと判定するため、説明文に引用しただけでも同じ扱いに戻ってしまう。
//   併せて、ここで宣言されていた `global using global::System;` (ImplicitUsings と重複) と、
//   OpenTK / Windows.UI / xFunc / Tensorflow などこのファイルが使っていない using を削除した。
using Crystallography;
using MemoryPack;
using System.Collections.Generic;
using System.Diagnostics;
// 260902Codex: Keep measurement-time progress text stable regardless of the Windows display culture.
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace MineraScope
{
    [MemoryPackable]
    public partial struct SimulationProperty
    {
        public string MineralGroupName;
        public string Atoms;
        //public (string Name, double Mol)[]               Atoms1;
        public (string ElementName, double Weight)[][] Atoms1;
        public (string Name, double Mol)[] Atoms2;
        public string DetectorName;
        [MemoryPackIgnore]
        public DetectorProfile DetectorProfile;
        //　カーボン蒸着厚　（nm）
        public double CarbonCoatThickness;
        // 260622Claude: カーボン蒸着厚を spectrum ごとに ±x% 振る幅 (0 で無効)。既知範囲を広げるための生成時ばらつき。
        public double CarbonCoatThicknessJitterPercent;
        // 加速電圧（kV）
        public double BeamEnergy;
        //シミュレーション回数
        public double Count;
        //ステップ
        public double Division;
        //測定時間
        public double LiveTime;
        //照射電流
        public double ProbeCurrent;
        //並列回数
        public int ParallelCount;
        //msaファイル出力先
        public string OutputFolder;
        [MemoryPackIgnore]
        public string[] OutputFiles;
    }



    public partial class GeneratorForm : Form
    {
        public FormMain FormMain;

        private readonly List<EndmemberControl> endmemberControls = [];
        private readonly DeepLearning _deepLearning;
        // 260416Codex: 鉱物 DB の入出力を repository に寄せ、Form の責務を UI 側へ戻していきます。
        private readonly MineralDatabaseRepository _mineralDatabaseRepository;
        // 260416Codex: 学習開始処理を workflow 化し、ボタンイベントから直接 DeepLearning を叩かない形へ寄せます。
        private readonly ModelTrainingWorkflow _modelTrainingWorkflow;
        // 260416Codex: シミュレーション実行計画の組み立てを service 化し、完成 UI へ向けた差し替え点を明確にします。
        private readonly SimulationPlanBuilder _simulationPlanBuilder;
        // 260507Codex: manifest/pool を正本にした不足確認・予約・学習入力作成を担当します。
        private readonly SpectrumPoolWorkflow _spectrumPoolWorkflow;
        // 260416Codex: 計算済み plan の出力と実行を Form から分離し、UI は起動トリガーだけを担当します。
        private readonly SimulationExecutionService _simulationExecutionService;
        // 260511Codex: キャリブレーション画面は閉じても破棄せず、入力値を保持するため 1 インスタンスだけ持ちます。
        private readonly EdxCalibrationForm _edxCalibrationForm;
        // 260626Codex: Detector physics travels with generation requests without adding detector-editing UI to GeneratorForm.
        private DetectorProfile _detectorProfile = DetectorProfile.CreateLegacyTest();
        // 260513Codex: DTSA-II spectrum 生成だけを中止するため、学習処理とは別の CTS を持ちます。
        // 260514Codex: DTSA-II spectrum 生成とモデル作成は別々の CTS を持ち、同じ中止ボタンから現在の処理だけ止めます。
        private CancellationTokenSource _simulationCancellationTokenSource;
        private CancellationTokenSource _modelTrainingCancellationTokenSource;
        // 260528Claude: 鉱物選択や分解能変更で再計算する組成リストを Task.Run に逃がすため、最新の計算だけを残せる CTS を持ちます。
        private CancellationTokenSource _solutionUpdateCts;

        // 260507Codex: 下部ドロワーの標準高さを UI 初期化と開閉処理で共有します。
        private const float BottomDrawerDefaultHeight = 180F;
        // 260508Codex: GeneratorForm の設定ファイル名を保存・復元で共有します。
        private const string UserSettingsFileName = "GeneratorFormSettings.json";

        private string AssemblyPath { get; } = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;
        // 260416Codex: Python スクリプトはユーザーに見せない固定保存先へ集約します。
        // 260727Claude: 既定保存先は DefaultStoragePaths に集約する (パスは従来と同一、headless 実行と共有)。
        private string PythonScriptOutputPath { get; } = DefaultStoragePaths.PythonScriptsFolder;

        // 260430Codex: 親フォーム未接続時の教師データ既定値も Documents 配下の共通パスへそろえます。
        private string DefaultTrainingDataPath => DefaultStoragePaths.TrainingDataFolder;

        // 260424Codex: EDX 出力先は FormMain の共通ファイル設定から参照します。
        private string EdxOutputPath => FormMain?.EdxOutputPath ?? DefaultTrainingDataPath;

        // 260424Codex: DTSA-II パスも FormMain の共通ファイル設定から参照します。
        private string DtsaPath => FormMain?.DtsaPath ?? string.Empty;

        // 260424Codex: モデル保存先は FormMain の共通ファイル設定へ読み書きします。
        private string ModelOutputPath
        {
            get => FormMain?.ModelPath ?? string.Empty;
            set
            {
                if (FormMain is not null)
                    FormMain.ModelPath = value;
            }
        }

        public GeneratorForm()
        {
            InitializeComponent();
            // 260511Codex: EDX キャリブレーション画面を再利用し、フォームを閉じても設定値を失わないようにします。
            _edxCalibrationForm = new EdxCalibrationForm
            {
                Owner = this,
                StartPosition = FormStartPosition.CenterParent,
                Visible = false
            };
            // 260507Codex: 明示リストに入れた生成・EDX・学習設定だけを前回値から復元します。
            LoadUserSettings();
            // 260507Codex: Designer 手作業で追加される新レイアウト部品を実行時に初期化します。
            ConfigureGeneratorLayoutRuntimeState();
            _deepLearning = new(TrainLog);
            // 260416Codex: 既存 helper を Form 初期化時に束ねておき、以降のイベント処理を薄く保ちます。
            _mineralDatabaseRepository = new(AssemblyPath);
            // 260514Codex: tmp フォルダの保存制御も学習ログへ出せるよう workflow にログ出力を渡します。
            _modelTrainingWorkflow = new(_deepLearning, TrainLog);
            _simulationPlanBuilder = new();
            _spectrumPoolWorkflow = new(new SpectrumPoolRepository(new SpectrumConditionKeyBuilder()), _simulationPlanBuilder);
            // 260416Codex: スクリプト生成と外部実行は専用 service へまとめ、Form から I/O 詳細を隠します。
            _simulationExecutionService = new(new SimulationScriptGenerator());
            // 260416Codex: 固定保存先を事前作成し、設定 UI には表示しないようにします。
            Directory.CreateDirectory(PythonScriptOutputPath);
            // 260416Codex: DB 初期化は repository にまとめ、Form からファイル操作の詳細を外します。
            _mineralDatabaseRepository.EnsureInitialized();
            endmemberControls.AddRange([EndmemberControl1, EndmemberControl2]);
            // 260511Codex: repository へ渡すだけの helper を挟まず、初期表示は直接読み込みます。
            BindMineralSolutions(_mineralDatabaseRepository.Load());
        }

        // 260507Codex: ログ、組成候補、選択中鉱物詳細を除外して設定値だけを復元します。
        private void LoadUserSettings()
        {
            // 260727Claude: 壊れた設定は既定値へフォールバックし、理由は FormMain_Load でまとめて知らせる。
            var loaded = FormUserSettingsStore.Load<GeneratorFormUserSettings>(UserSettingsFileName);

            // 260728Claude: 保存済みの値が手に入らなかったとき (初回起動 / 本体も .bak も読めなかった) は Designer 既定値を残す。
            //   既定構築した設定オブジェクトは数値が軒並み 0 なので、そのまま適用すると Designer 既定値
            //   (Epochs 500 / BatchSize 16 / spectrum 1000 など) を 0 で潰し、終了時保存でその値が永続化されてしまう。
            if (!loaded.HasStoredValues)
            {
                _detectorProfile = DetectorProfile.CreateLegacyTest(textBoxDetectorName.Text);
                return;
            }

            var settings = loaded.Settings;
            _detectorProfile = settings.GetDetectorProfile();
            textBoxDetectorName.Text = _detectorProfile.Name;
            textBoxModelName.Text = settings.ModelName;
            numericBoxSpectraPerMineral.Value = settings.TargetSpectrumCount;
            numericBoxParallel.Value = settings.ParallelCount;
            numericBoxResolution.Value = settings.Resolution;
            numericBoxEpochs.Value = settings.Epochs;
            numericBoxBatchSize.Value = settings.BatchSize;
            numericBoxUnknownDistanceScale.Value = settings.UnknownDistanceScale;
            numericBoxEarlyStopping.Value = settings.EarlyStopping;
            numericBoxValidationSplit.Value = settings.ValidationSplit;
            numericBoxProbeCurrent.Value = settings.ProbeCurrent;
            numericBoxLiveTime.Value = settings.LiveTime;
            numericBoxBeamEnergy.Value = settings.BeamEnergy;
            numericBoxCarbonThickness.Value = settings.CarbonThickness;
            numericBoxcarbonrandam.Value = settings.CarbonThicknessJitterPercent;
            // 260901Codex: B 導入前の保存設定には値が無いため、既存ユーザーだけ従来の単一時間を維持します。
            checkBoxMeasurementTimePresetB.Checked = settings.MeasurementTimePresetBEnabled ?? false;
        }

        // 260507Codex: 次回起動で戻す対象を明示し、CheckedListBox のチェック状態は保存しません。
        internal void SaveUserSettings()
        {
            var detectorProfile = ReadDetectorProfileFromUi();
            FormUserSettingsStore.Save(
                UserSettingsFileName,
                new GeneratorFormUserSettings
                {
                    DetectorName = detectorProfile.Name,
                    DetectorProfile = detectorProfile,
                    ModelName = textBoxModelName.Text,
                    TargetSpectrumCount = numericBoxSpectraPerMineral.Value,
                    ParallelCount = numericBoxParallel.Value,
                    Resolution = numericBoxResolution.Value,
                    Epochs = numericBoxEpochs.Value,
                    BatchSize = numericBoxBatchSize.Value,
                    UnknownDistanceScale = numericBoxUnknownDistanceScale.Value,
                    EarlyStopping = numericBoxEarlyStopping.Value,
                    ValidationSplit = numericBoxValidationSplit.Value,
                    ProbeCurrent = numericBoxProbeCurrent.Value,
                    LiveTime = numericBoxLiveTime.Value,
                    // 260901Codex: 非表示中も単一時間用の数値は保持し、B の選択状態だけを別に保存します。
                    MeasurementTimePresetBEnabled = checkBoxMeasurementTimePresetB.Checked,
                    BeamEnergy = numericBoxBeamEnergy.Value,
                    CarbonThickness = numericBoxCarbonThickness.Value,
                    CarbonThicknessJitterPercent = numericBoxcarbonrandam.Value
                });
        }

        // 260513Codex: 現在の Designer 名を直接使い、詳細設定ドロワーを初期化します。
        private void ConfigureGeneratorLayoutRuntimeState()
        {
            ConfigureBottomDrawerInitialState();

            // 260527Codex: DTSA-II spectrum 生成だけを先に実行できるよう、実行ボタンは常時表示します。
            buttonRunSpectrumGeneration.Visible = true;
            buttonModelTrain.Text = "モデルを作成";
            // 260513Codex: 中止ボタンの Click 接続は Designer 側で持ち、ここでは初期状態だけをそろえます。
            buttonCancel.Enabled = false;
            // 260527Codex: Designer 上の statusStrip1 を進捗表示として使うため、初期値だけ code-behind でそろえます。
            InitializeSimulationStatus();

            UpdateAdvancedSettingsButtonText();
            // 260901Codex: 新規起動の Designer 既定値と復元済み設定のどちらにも同じ表示規則を適用します。
            ApplyMeasurementTimePresetUiState();
        }

        // 260513Codex: 詳細設定は最初から下部ドロワー内にあるため、初期状態は非表示と高さ 0 にそろえます。
        private DetectorProfile ReadDetectorProfileFromUi()
        {
            var profile = _detectorProfile.Clone();
            profile.Name = textBoxDetectorName.Text.Trim();
            return DetectorProfile.CreateWithDefaults(profile);
        }

        private void ConfigureBottomDrawerInitialState()
        {
            groupBoxAdvancedSettings.Dock = DockStyle.Fill;
            panelBottomDrawer.Visible = false;
            panelBottomDrawer.AutoScroll = true;
            SetBottomDrawerRowHeight(0F);
        }

        // 260511Codex: キャリブレーション画面は再表示時に既存インスタンスを前面化して値を保持します。
        private void buttonCalibration_Click(object sender, EventArgs e)
        {
            if (_edxCalibrationForm.Visible)
            {
                _edxCalibrationForm.BringToFront();
                return;
            }

            _edxCalibrationForm.Visible = true;
        }

        // 260513Codex: 詳細設定 CheckBox の状態を下部ドロワーの表示状態へ直接同期します。
        private void checkBoxAdvanced_CheckedChanged(object sender, EventArgs e)
        {
            SetAdvancedSettingsVisible(checkBoxAdvanced.Checked);
        }

        // 260901Codex: B は測定時間と本数を固定するため、使用しない単一条件の入力欄を隠します。
        private void checkBoxMeasurementTimePresetB_CheckedChanged(object sender, EventArgs e) =>
            ApplyMeasurementTimePresetUiState();

        private void ApplyMeasurementTimePresetUiState()
        {
            bool usePresetB = checkBoxMeasurementTimePresetB.Checked;
            numericBoxLiveTime.Visible = !usePresetB;
            numericBoxSpectraPerMineral.Visible = !usePresetB;
        }

        // 260513Codex: 下部ドロワーは詳細設定専用なので、中身を差し替えず表示と高さだけを切り替えます。
        private void SetAdvancedSettingsVisible(bool visible)
        {
            panelBottomDrawer.Visible = visible;
            SetBottomDrawerRowHeight(visible ? BottomDrawerDefaultHeight : 0F);
            tableLayoutPanelMain.PerformLayout();
            UpdateAdvancedSettingsButtonText();
        }

        // 260513Codex: 下部ドロワー用の Row だけを高さ変更し、詳細設定の開閉を表現します。
        private void SetBottomDrawerRowHeight(float height)
        {
            if (tableLayoutPanelMain.RowStyles.Count <= 3)
                return;

            tableLayoutPanelMain.RowStyles[3].SizeType = SizeType.Absolute;
            tableLayoutPanelMain.RowStyles[3].Height = height;
        }

        // 260513Codex: 下部ドロワーの表示状態を CheckBox に反映します。
        private void UpdateAdvancedSettingsButtonText()
        {
            checkBoxAdvanced.Text = "詳細設定を表示";
            if (checkBoxAdvanced.Checked != panelBottomDrawer.Visible)
                checkBoxAdvanced.Checked = panelBottomDrawer.Visible;
        }

        private void BindMineralSolutions(SolidSolution[] solutions, bool clearSelection = true)
        {
            checkedListBoxMinerals.DataSource = null;
            checkedListBoxMinerals.DisplayMember = nameof(SolidSolution.Name);
            checkedListBoxMinerals.DataSource = solutions;

            if (clearSelection)
                checkedListBoxMinerals.ClearSelected();
        }

        private Mineral[] CollectEndmemberMinerals() =>
            endmemberControls
                .Where(comp => !string.IsNullOrWhiteSpace(comp.EndmemberName))
                .Select(comp => new Mineral(comp.EndmemberName, comp.EndmemberFormula))
                .ToArray();

        private string[] ParseConstraints() =>
            textBoxConstraints.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(constraint => !string.IsNullOrEmpty(constraint))
                .ToArray();

        // 260424Codex: 画面入力の読み取りは partial に逃がさず、フォーム本体で request に変換します。
        // 260901Codex: B 選択時は生成と学習が同じ共通測定時間配分を使う request にします。
        private ModelCreationRequest CreateModelCreationRequest()
        {
            var request = new ModelCreationRequest(
                // 260424Codex: Python スクリプトの保存先は固定パスへ集約した値を使います。
                new ModelCreationPaths(
                    EdxOutputPath.Trim(),
                    PythonScriptOutputPath,
                    DtsaPath.Trim(),
                    ModelOutputPath.Trim()),
                // 260511Codex: Designer 改名後もモデル保存フォルダ名として画面のモデル名を渡します。
                textBoxModelName.Text.Trim(),
                new SemEdxCondition(
                    ReadDetectorProfileFromUi(),
                    numericBoxCarbonThickness.Value,
                    numericBoxBeamEnergy.Value,
                    numericBoxLiveTime.Value,
                    numericBoxProbeCurrent.Value),
                new SimulationExecutionSettings(
                    (int)numericBoxSpectraPerMineral.Value,
                    (double)numericBoxResolution.Value / 100,
                    (int)numericBoxParallel.Value,
                    // 260622Claude: カーボン蒸着膜厚を spectrum ごとに振るばらつき幅 (%) を生成へ渡す。
                    numericBoxcarbonrandam.Value),
                new ModelTrainingSettings(
                    (int)numericBoxEpochs.Value,
                    (int)numericBoxBatchSize.Value,
                    (int)numericBoxEarlyStopping.Value,
                    (float)numericBoxValidationSplit.Value / 100f,
                    // 260622Claude: 未知判定で既知とみなす距離に掛ける倍率を学習へ渡す。
                    numericBoxUnknownDistanceScale.Value),
                // 260511Codex: 1 回だけの CheckedListBox helper を避け、選択対象はその場で配列化します。
                checkedListBoxMinerals.CheckedItems.Cast<SolidSolution>().ToArray());

            return request with
            {
                SpectrumTimeAllocations = checkBoxMeasurementTimePresetB.Checked
                    ? SpectrumTimeSchedule.MeasurementTimeB.CreateAllocations()
                    : [],
                // 260907Codex: The fixed B checkbox keeps its current UI but now asks the workflow to plan compositions across every live time together.
                SpectrumTimeSchedule = checkBoxMeasurementTimePresetB.Checked
                    ? SpectrumTimeSchedule.MeasurementTimeB
                    : null
            };
        }

        // 260507Codex: manifest/pool フローに必要な最低限の画面入力を実行前に検証します。
        private static string ValidateModelCreationRequest(
            ModelCreationRequest request,
            bool requireDtsaPath,
            bool requireModelName = true)
        {
            if (request.SelectedMineralSolutions.Count == 0)
                return "モデル作成対象の鉱物が選択されていません。";

            // 260901Codex: B と従来単一時間のどちらも、request が返す有効配分を共通検証します。
            try
            {
                request.GetEffectiveSpectrumTimeAllocations();
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }

            if (request.Simulation.ResolutionStep <= 0)
                return "化学組成分解能を 0 より大きい値にしてください。";

            if (string.IsNullOrWhiteSpace(request.Paths.SpectrumOutputFolder))
                return "スペクトル pool の保存先を指定してください。";

            // 260527Codex: spectrum 生成だけの実行ではモデル名を使わないため、学習開始時だけ必須にします。
            if (requireModelName && string.IsNullOrWhiteSpace(request.ModelName))
                return "モデル名を入力してください。";

            if (requireModelName && request.ModelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "モデル名にフォルダ名として使えない文字が含まれています。";

            if (requireDtsaPath && string.IsNullOrWhiteSpace(request.Paths.DtsaFolder))
                return "DTSA-II のフォルダを指定してください。";

            // 260626Codex: Fail early when the selected folder is not the dtsa2.msi install root.
            string dtsaValidationError = requireDtsaPath
                ? DtsaMsiInstallation.GetValidationError(request.Paths.DtsaFolder)
                : null;
            if (dtsaValidationError is not null)
                return dtsaValidationError;

            return string.Empty;
        }

        // 260513Codex: DTSA-II spectrum 生成だけを CancellationTokenSource で中止できるようにします。
        private async void buttonRunSpectrumGeneration_Click(object sender, EventArgs e)
        {
            var request = CreateModelCreationRequest();
            string validationMessage = ValidateModelCreationRequest(request, requireDtsaPath: true, requireModelName: false);
            if (!string.IsNullOrEmpty(validationMessage))
            {
                MessageBox.Show(validationMessage, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 260901Codex: Keep recovery and every DTSA-II batch under one cross-process pool mutation lease.
            if (!SpectrumPoolMutationLease.TryAcquire(out var poolMutationLease, out string leaseFailureReason))
            {
                MessageBox.Show(this, leaseFailureReason, "スペクトル生成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var cancellationTokenSource = new CancellationTokenSource();
            _simulationCancellationTokenSource = cancellationTokenSource;
            buttonRunSpectrumGeneration.Enabled = false;
            // 260527Codex: 生成と学習が同時に同じ pool を触らないよう、生成中はモデル作成を止めます。
            buttonModelTrain.Enabled = false;
            // 260901Codex: 実行中の request と画面表示が食い違わないよう、B の切り替えも完了まで固定します。
            checkBoxMeasurementTimePresetB.Enabled = false;
            buttonCancel.Enabled = true;
            // 260606Claude: 生成にかかった時間をバーへ出すため、開始時に計測を始めます。
            _operationStopwatch.Restart();
            int preparationPoolCount = request.SelectedMineralSolutions.Count
                * request.GetEffectiveSpectrumTimeAllocations().Count;
            TrainLog($"spectrum 生成準備を開始: {preparationPoolCount} pool の manifest と既存ファイルを確認します。");
            SetStatusMarqueeWithElapsed("spectrum 生成準備中: Pending回収");
            int reservedSpectrumCount = 0;

            try
            {
                // 260901Codex: 完成済み Pending の回収と不足予約はファイル数が多いため、UI スレッド外で順に行います。
                var preparationProgress = new Progress<SpectrumPoolPreparationProgress>(ReportSpectrumPoolPreparationProgress);
                var preparation = await Task.Run(() =>
                {
                    var recovery = _spectrumPoolWorkflow.RecoverPendingSpectra(
                        request,
                        preparationProgress,
                        cancellationTokenSource.Token);
                    var plan = _spectrumPoolWorkflow.CreateMissingSimulationPlan(
                        request,
                        out var shortages,
                        preparationProgress,
                        cancellationTokenSource.Token,
                        validateCompletedSpectra: false);
                    return (Recovery: recovery, Plan: plan, Shortages: shortages);
                }, cancellationTokenSource.Token);

                TrainLog(
                    $"Pending回収: 確認 {preparation.Recovery.ExaminedCount} 件 / 復旧 {preparation.Recovery.RecoveredCount} 件 / " +
                    $"不正 {preparation.Recovery.InvalidCount} 件 / ファイルなし {preparation.Recovery.MissingFileCount} 件");
                LogSimulationShortages(preparation.Shortages);
                if (preparation.Plan.Batches.Count == 0)
                {
                    string message = preparation.Shortages.Count == 0
                        ? "不足している spectrum はありません。"
                        : SpectrumPoolWorkflow.FormatShortageMessage(preparation.Shortages);
                    SetStatusWithElapsed(
                        preparation.Shortages.Count == 0 ? "spectrum 生成: 不足なし" : "spectrum 生成: 実行できる予約なし",
                        0,
                        1);
                    MessageBox.Show(message, "スペクトル生成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                int jobCount = preparation.Plan.Batches.Sum(batch => batch.Jobs.Count);
                reservedSpectrumCount = preparation.Plan.Batches
                    .SelectMany(batch => batch.Jobs)
                    .Sum(job => job.Reservations.Count);
                // 260901Codex: B では鉱物 batch 内に複数時間の job が入るため、合計 job と spectrum 数を表示します。
                TrainLog($"spectrum 生成開始: batch {preparation.Plan.Batches.Count} 件、ジョブ {jobCount} 件、spectrum {reservedSpectrumCount} 件");
                SetStatusWithElapsed(
                    $"spectrum 生成開始: batch {preparation.Plan.Batches.Count} 件 / ジョブ {jobCount} 件 / spectrum {reservedSpectrumCount} 件",
                    0,
                    reservedSpectrumCount);

                // 260901Codex: 各鉱物 batch の結果を後続 batch 開始前に原子的保存し、中断済み成果を保持します。
                var progress = new Progress<SimulationExecutionProgress>(ReportSimulationProgress);
                var results = await _simulationExecutionService.RunAsync(
                    preparation.Plan,
                    progress,
                    cancellationTokenSource.Token,
                    (_, batchResults) =>
                    {
                        _spectrumPoolWorkflow.ApplySimulationResults(batchResults);
                        return Task.CompletedTask;
                    });
                foreach (var result in results)
                    LogSimulationExecutionResult(result);

                if (cancellationTokenSource.IsCancellationRequested || results.Any(result => result.IsCanceled))
                {
                    TrainLog("spectrum 生成をキャンセルしました。");
                    SetStatusWithElapsed("spectrum 生成: キャンセル", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
                }

                // 260901Codex: 全時間poolの検証は大量のEMSAを読むため、終了後の集計もUIスレッド外で行います。
                var finalState = await Task.Run(() =>
                    (Counts: _spectrumPoolWorkflow.GetStatusCounts(request, validateCompletedSpectra: false),
                     Shortages: _spectrumPoolWorkflow.GetShortages(request, validateCompletedSpectra: false)));
                TrainLog(
                    $"manifest status: Completed {finalState.Counts.Completed} 件 / Failed {finalState.Counts.Failed} 件 / " +
                    $"Missing {finalState.Counts.Missing} 件 / Pending {finalState.Counts.Pending} 件");
                SetStatusWithElapsed(
                    $"manifest status: Completed {finalState.Counts.Completed} / Failed {finalState.Counts.Failed} / Missing {finalState.Counts.Missing} / Pending {finalState.Counts.Pending}",
                    toolStripProgressBar1.Value,
                    Math.Max(reservedSpectrumCount, 1));

                if (finalState.Shortages.Count > 0)
                {
                    SetStatusWithElapsed(
                        $"spectrum 生成: 不足 {finalState.Shortages.Sum(shortage => shortage.MissingCount)} 件",
                        toolStripProgressBar1.Value,
                        Math.Max(reservedSpectrumCount, 1));
                    MessageBox.Show(
                        SpectrumPoolWorkflow.FormatShortageMessage(finalState.Shortages),
                        "スペクトル生成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    SetStatusWithElapsed(
                        "spectrum 生成完了: 必要な spectrum pool がそろいました",
                        reservedSpectrumCount,
                        Math.Max(reservedSpectrumCount, 1));
                    MessageBox.Show("必要な spectrum pool がそろいました。", "スペクトル生成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                TrainLog("spectrum 生成をキャンセルしました。");
                SetStatusWithElapsed("spectrum 生成: キャンセル", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
            }
            catch (Exception exception)
            {
                // 260901Codex: 予約・checkpoint・検証の失敗を未処理例外にせず、再開可能なmanifestを残して通知します。
                TrainLog($"spectrum 生成に失敗しました: {exception.GetType().Name}: {exception.Message}");
                SetStatusWithElapsed("spectrum 生成: 失敗", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
                MessageBox.Show(
                    this,
                    $"spectrum 生成に失敗しました。\r\n\r\n{exception.Message}",
                    "スペクトル生成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                // 260901Codex: Release after the last batch checkpoint; the OS also releases the file handle on a hard crash.
                poolMutationLease!.Dispose();
                if (ReferenceEquals(_simulationCancellationTokenSource, cancellationTokenSource))
                    _simulationCancellationTokenSource = null;

                buttonRunSpectrumGeneration.Enabled = true;
                buttonModelTrain.Enabled = true;
                checkBoxMeasurementTimePresetB.Enabled = true;
                buttonCancel.Enabled = false;
            }
        }

        // 260527Codex: 正常完了は progress 側で逐次表示し、ここでは失敗・キャンセルの詳細だけを補足します。
        private void LogSimulationExecutionResult(SimulationExecutionResult result)
        {
            if (!result.IsCanceled && result.ExitCode == 0 && result.ExceptionMessage is null)
                return;

            string solutionName = result.Reservations.Count > 0
                ? result.Reservations[0].SolutionName
                : "unknown";
            int spectrumCount = result.Reservations.Count;

            // 260727Claude: 見出し行だけが分岐で、詳細ログは共通なので分岐の外へ出す。
            TrainLog(result.IsCanceled
                ? $"DTSA-II キャンセル: {solutionName} ({spectrumCount} spectra)"
                : $"DTSA-II 終了コード {result.ExitCode}: {solutionName} ({spectrumCount} spectra)");
            LogSimulationFailureDetail(result);
        }
        #region EndmemberControl
        // EndmemberControlに端成分の情報を設定
        // 端成分数に応じてコントロールを追加/削除      
        // 260416Codex: 選択済み固溶体の表示準備を共通化
        private void ResetVisibleEndmemberControls()
        {
            foreach (var control in endmemberControls)
            {
                control.Reset();
                control.Visible = false;
                control.Enabled = true;
            }
        }

        // 260416Codex: 既定で表示する端成分コントロール数を明示
        private void ShowDefaultEndmemberControls()
        {
            for (int i = 0; i < Math.Min(2, endmemberControls.Count); i++)
                endmemberControls[i].Visible = true;
        }

        // 260416Codex: 動的に追加した端成分コントロールの破棄処理を分離
        private void RemoveDynamicEndmemberControls()
        {
            for (int i = endmemberControls.Count - 1; i >= 2; i--)
            {
                var ctrl = endmemberControls[i];
                endmemberControls.Remove(ctrl);
                ctrl.Dispose();
            }
        }

        // 260416Codex: 必要数まで EndmemberControl を増やす
        private void EnsureEndmemberControlCount(int requiredCount)
        {
            while (endmemberControls.Count < requiredCount)
                AddNewEndmemberControl();
        }

        // 260416Codex: 端成分の UI 反映を1か所に集約
        private static void ApplyMember(EndmemberControl control, Mineral member)
        {
            control.EndmemberName = member.Name;
            control.EndmemberFormula = member.FormulaText;
            control.Enabled = true;
            control.Visible = true;
        }

        // 260416Codex: 端成分 UI 更新の分岐を早期 return ベースに整理
        private void UpdateEndmemberUI()
        {
            if (checkedListBoxMinerals.SelectedItem is not SolidSolution selectedSolution)
            {
                // 260416Codex: 未選択時の初期表示を helper で統一
                ResetVisibleEndmemberControls();
                ShowDefaultEndmemberControls();
                return;
            }

            // 260416Codex: 選択済み固溶体に切り替わる前に余分なコントロールを整理
            RemoveDynamicEndmemberControls();
            ResetVisibleEndmemberControls();
            var members = selectedSolution.Members;

            if (members is null)
            {
                // 260416Codex: メンバー未設定時も既定表示に寄せる
                ShowDefaultEndmemberControls();
                return;
            }

            if (members.Length == 1)
            {
                // 260416Codex: 単一端成分時の表示設定を最短経路にする
                ApplyMember(EndmemberControl1, members[0]);
                EndmemberControl2.Enabled = true;
                EndmemberControl2.Visible = true;
                return;
            }

            // 260416Codex: 必要数を確保してから順番に反映する
            EnsureEndmemberControlCount(members.Length);
            for (int i = 0; i < members.Length; i++)
                ApplyMember(endmemberControls[i], members[i]);
        }
        // 新しいEndmemberコントロールを追加
        private void AddNewEndmemberControl()
        {
            // 260511Codex: FlowLayoutPanel が配置するため、追加行は既存行のサイズだけ合わせます。
            var newComp = new EndmemberControl
            {
                Size = endmemberControls[^1].Size
            };

            flowLayoutPanelEndmembers.Controls.Add(newComp);
            endmemberControls.Add(newComp);
        }
        #endregion
        #region 選択されている鉱物の条件式、化学組成リストを更新・表示
        // 260528Claude: 組成リストの先頭表示件数。これ以上は UI で読まないので、Lazy 列挙はこの件数で打ち切ります。
        private const int CompositionListPreviewCount = 200;

        // 260528Claude: 軽い表示 (鉱物名・式・制約) は同期で即時反映し、重い組成リスト計算だけを Task.Run + CTS に逃がします。鉱物選択や分解能変更で前回計算をキャンセルできるよう、最新の計算だけを残します。
        private void UpdateSelectedSolution()
        {
            _solutionUpdateCts?.Cancel();
            _solutionUpdateCts?.Dispose();
            _solutionUpdateCts = null;

            // 260511Codex: 未選択時は guard clause で詳細欄を空に戻します。
            if (checkedListBoxMinerals.SelectedItem is not SolidSolution selectedSolution)
            {
                textBoxConstraints.Text = "";
                textBoxCompositionList.Text = "";
                textBoxMineralName.Text = "";
                textBoxMemo.Text = "";
                return;
            }
            textBoxMineralName.Text = selectedSolution.Name;
            textBoxMemo.Text = selectedSolution.Formula;

            // 条件式を表示
            textBoxConstraints.Text = selectedSolution.Constraints.Length > 0
                ? string.Join($",{Environment.NewLine}", selectedSolution.Constraints)
                : string.Empty;

            double resolution = (double)numericBoxResolution.Value / 100;
            textBoxCompositionList.Text = "計算中…";

            var cts = new CancellationTokenSource();
            _solutionUpdateCts = cts;
            var token = cts.Token;

            Task.Run(() =>
            {
                // 260528Claude: Lazy 列挙を Take(プレビュー件数 + 1) で打ち切ることで、6 端成分 1% のような巨大候補空間でも O(プレビュー件数) で停止します。
                var preview = new List<string>(CompositionListPreviewCount);
                try
                {
                    foreach (var ratio in selectedSolution.EnumerateCandidateFractionsLazy(resolution))
                    {
                        token.ThrowIfCancellationRequested();
                        preview.Add(FormatFractionMap(selectedSolution, ratio));
                        if (preview.Count >= CompositionListPreviewCount)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                    return;

                string text = preview.Count == 0
                    ? "計算できません"
                    : string.Join(Environment.NewLine, preview);

                BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    textBoxCompositionList.Text = text;
                }));
            }, token);
        }
        #endregion
        // 260507Codex: 組成候補表示は端成分名と比率だけにし、ファイル名生成ルールから切り離します。
        private static string FormatFractionMap(SolidSolution solution, double[] fractions) =>
            string.Join(
                ", ",
                solution.Members.Select((member, index) =>
                    $"{member.Name}: {(index < fractions.Length ? fractions[index] : 0):F3}"));

        #region EndmemberControlの追加と削除
        private void buttonEndmemberAdd_Click(object sender, EventArgs e)
        {
            AddNewEndmemberControl();
        }

        private void buttonEndmemberDelete_Click(object sender, EventArgs e)
        {
            if (endmemberControls.Count <= 1)
                return;

            var lastControl = endmemberControls.Last();

            endmemberControls.Remove(lastControl);
            // メモリ解放
            lastControl.Dispose();
        }
        #endregion

        // 260416Codex: 追加・更新で共通の固溶体生成処理をまとめる
        private bool TryCreateSolutionFromInputs(out SolidSolution solution)
        {
            var members = CollectEndmemberMinerals();
            if (members.Length == 0)
            {
                solution = null!;
                return false;
            }

            solution = new SolidSolution(
                textBoxMineralName.Text.Trim(),
                textBoxMemo.Text.Trim(),
                members,
                ParseConstraints());
            return true;
        }

        // 260511Codex: 保存と再バインドはここに寄せ、薄い保存 helper は挟みません。
        private void SaveAndBindSolutions(SolidSolution[] solutions, bool clearSelection = true)
        {
            _mineralDatabaseRepository.Save(solutions);
            BindMineralSolutions(solutions, clearSelection);
        }

        // 260416Codex: ItemCheck 後の再計算をメソッドグループで簡潔にする
        private void checkedListBoxMinerals_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            // 260511Codex: ItemCheck 反映後に、選択中鉱物の候補表示だけを遅延更新します。
            BeginInvoke(new Action(UpdateSelectedSolution));
        }

        private void checkedListBoxMinerals_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateEndmemberUI();
            UpdateSelectedSolution();
        }

        #region リストに追加・更新・削除・初期化メソッド
        #region 追加メソッド
        // 260416Codex: 追加時の入力変換を helper ベースに統一
        private void buttonAddMineral_Click(object sender, EventArgs e)
        {
            // 260416Codex: 入力からの固溶体生成を helper に統一
            if (!TryCreateSolutionFromInputs(out var newSolution))
                return;

            // 260416Codex: 保存と再バインドは共通 helper に寄せ、追加処理の本筋だけを残します。
            SaveAndBindSolutions(
                _mineralDatabaseRepository.Load()
                .Append(newSolution)
                .ToArray(),
                clearSelection: false);
        }
        #endregion
        #region 更新メソッド
        // 260416Codex: 更新時の入力変換も追加処理と揃える
        private void buttonUpdateMineral_Click(object sender, EventArgs e)
        {
            int selectedIndex = checkedListBoxMinerals.SelectedIndex;
            // 260416Codex: 更新時も追加時と同じ入力変換を使う
            if (!TryCreateSolutionFromInputs(out var updatedSolution))
                return;

            // 260511Codex: 更新対象の配列も repository から直接読みます。
            var existingSolutions = _mineralDatabaseRepository.Load();
            existingSolutions[selectedIndex] = updatedSolution;

            // 260416Codex: 更新後の永続化と再表示も共通 helper へ寄せます。
            SaveAndBindSolutions(existingSolutions);
        }
        #endregion
        #region 削除メソッド
        // 260416Codex: 削除後の保存と再バインドも共通 helper を使って読みやすくします。
        private void buttonDeleteMineral_Click(object sender, EventArgs e)
        {
            int selectedIndex = checkedListBoxMinerals.SelectedIndex;
            SaveAndBindSolutions(
                _mineralDatabaseRepository.Load()
                .Where((_, index) => index != selectedIndex)
                .ToArray());
        }
        //全削除メソッド
        private void buttonDeleteAllMinerals_Click(object sender, EventArgs e)
        {
            // 260416Codex: 全削除時も共通 helper を通し、一覧クリアの手順を統一します。
            SaveAndBindSolutions([]);
        }
        #endregion
        #region 初期化メソッド
        private void buttonResetMinerals_Click(object sender, EventArgs e)
        {
            // 260416Codex: DB 初期化のファイル操作は repository に閉じ込めます。
            _mineralDatabaseRepository.Reset();
            // 260416Codex: Reset 後は再保存せず、読み直した内容をそのまま再表示します。
            BindMineralSolutions(_mineralDatabaseRepository.Load());
        }
        #endregion
        #endregion
        private void TrainLog(string message)
            => TextBoxLogHelper.AppendLine(textBoxModelLog, message);

        // 260527Codex: statusStrip1 は Designer の部品をそのまま使い、待機中の初期表示だけを整えます。
        private void InitializeSimulationStatus() =>
            SetStatusStrip("待機中", 0, 1);

        // 260527Codex: status strip 更新を一か所に集め、並列 progress 通知でも UI スレッドへ安全に戻します。
        private void SetStatusStrip(
            string text,
            int value,
            int maximum,
            ProgressBarStyle progressBarStyle = ProgressBarStyle.Blocks)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStatusStrip(text, value, maximum, progressBarStyle)));
                return;
            }

            int safeMaximum = Math.Max(1, maximum);
            toolStripProgressBar1.Style = progressBarStyle;
            toolStripProgressBar1.Minimum = 0;
            toolStripProgressBar1.Maximum = safeMaximum;
            toolStripProgressBar1.Value = Math.Clamp(value, 0, safeMaximum);
            toolStripStatusLabel1.Text = text;
        }

        // 260606Claude: 学習は spectrum 数のような整数単位がないので、全体進捗 (0..1) を 0..ProgressScale に拡大して滑らかなバーにします。
        private const int ProgressScale = 1000;

        // 260606Claude: spectrum 生成・モデル学習の経過時間表示用。各操作の開始時に Restart し、状態表示へ付与します。
        private readonly Stopwatch _operationStopwatch = new();

        // 260606Claude: 状態表示に操作開始からの経過時間を付けます。生成・学習の両方で共通に使います。
        private void SetStatusWithElapsed(string text, int value, int maximum) =>
            SetStatusStrip($"{text} (経過 {_operationStopwatch.Elapsed:hh\\:mm\\:ss})", value, maximum);

        // 260902Codex: Indeterminate animation makes long pool preparation visibly active before the first count arrives.
        private void SetStatusMarqueeWithElapsed(string text) =>
            SetStatusStrip(
                $"{text} (経過 {_operationStopwatch.Elapsed:hh\\:mm\\:ss})",
                0,
                1,
                ProgressBarStyle.Marquee);

        // 260606Claude: 学習進捗をステータスストリップへ反映します。バーは全体進捗、ラベルにモデル名・通し番号・エポックを出します（経過時間は helper が付与）。
        private void ReportTrainingProgress(TrainingProgress progress)
        {
            string epochText = progress.Epoch > 0
                ? $"epoch {progress.Epoch}/{progress.RequestedEpochs}"
                : "準備中";
            SetStatusWithElapsed(
                $"学習中: {progress.ModelName} ({progress.ModelIndex}/{progress.TotalModels}) {epochText}",
                (int)Math.Round(progress.OverallFraction * ProgressScale),
                ProgressScale);
        }

        // 260528Codex: status strip は spectrum 数で毎回更新し、訓練ログは長くなりすぎない間隔に絞ります。
        private void ReportSimulationProgress(SimulationExecutionProgress progress)
        {
            int maximum = progress.TotalSpectrumCount > 0
                ? progress.TotalSpectrumCount
                : progress.TotalJobCount;
            int value = progress.TotalSpectrumCount > 0
                ? progress.CompletedSpectrumCount
                : progress.CompletedJobCount;

            SetStatusWithElapsed(progress.Message, value, maximum);
            if (ShouldLogSimulationProgress(progress))
                TrainLog(FormatSimulationProgressLog(progress));
        }

        // 260902Codex: Preparation progress is reported by the worker and rendered on the UI thread with pool/file counts.
        private void ReportSpectrumPoolPreparationProgress(SpectrumPoolPreparationProgress progress)
        {
            // 260908Codex: Candidate scans have no known total or single live time; show their count and keep the activity indicator moving.
            string liveTimeText = progress.LiveTime > 0
                ? $" ({progress.LiveTime.ToString("G", CultureInfo.InvariantCulture)}秒)"
                : string.Empty;
            string poolText = $"pool {progress.PoolNumber}/{progress.TotalPoolCount}";
            string fileText = progress.FilesToCheck > 0
                ? $", {progress.ItemName} {progress.FilesChecked}/{progress.FilesToCheck}"
                : progress.FilesChecked > 0 ? $", {progress.ItemName} {progress.FilesChecked}件" : string.Empty;
            string statusText = $"準備中: {progress.Phase} {progress.MineralName}{liveTimeText}, {poolText}{fileText}";
            if (!progress.PoolCompleted && progress.FilesToCheck == 0)
                SetStatusMarqueeWithElapsed(statusText);
            else
                SetStatusWithElapsed(
                    statusText,
                    progress.CompletedPoolCount,
                    Math.Max(progress.TotalPoolCount, 1));

            if (!progress.PoolCompleted)
                return;

            if (progress.CompletedPoolCount == 1
                || progress.CompletedPoolCount == progress.TotalPoolCount
                || progress.CompletedPoolCount % 10 == 0)
                TrainLog($"{statusText}");
        }

        // 260528Codex: 1000 spectrum 規模でもログ欄が埋まりすぎないよう、保存進捗は節目だけ残します。
        private static bool ShouldLogSimulationProgress(SimulationExecutionProgress progress) =>
            progress.Kind != SimulationExecutionProgressKind.SpectrumSaved
            || progress.CompletedSpectrumCount <= 5
            || progress.CompletedSpectrumCount == progress.TotalSpectrumCount
            || progress.CompletedSpectrumCount % 10 == 0;

        // 260527Codex: 生成前の manifest 不足状況をログに出し、何を生成する予定かを見えるようにします。
        private void LogSimulationShortages(IReadOnlyList<SpectrumPoolShortage> shortages)
        {
            if (shortages.Count == 0)
            {
                TrainLog("spectrum 生成確認: 不足なし");
                return;
            }

            TrainLog("spectrum 生成確認: 不足あり");
            foreach (var shortage in shortages)
                // 260901Codex: 同じ鉱物の複数条件を区別できるよう、不足ログへ測定時間を含めます。
                TrainLog($"{shortage.MineralName} ({shortage.LiveTime:G}秒): Completed {shortage.CompletedCount}/{shortage.RequiredCount}, 不足 {shortage.MissingCount}");
        }

        // 260527Codex: 進捗ログへ終了コードと経過時間を付け、DTSA-II のどこまで進んだかを追いやすくします。
        private static string FormatSimulationProgressLog(SimulationExecutionProgress progress)
        {
            string message = progress.Message;
            if (progress.ExitCode is int exitCode)
                message += $", exit code {exitCode}";

            if (progress.Elapsed is TimeSpan elapsed)
                message += $", elapsed {elapsed:hh\\:mm\\:ss}";

            return message;
        }

        // 260527Codex: 失敗時の詳細は長くなりすぎないよう、stderr/stdout の先頭だけをログに残します。
        private void LogSimulationFailureDetail(SimulationExecutionResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.ExceptionMessage))
            {
                TrainLog($"DTSA-II 例外: {result.ExceptionMessage}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                TrainLog($"DTSA-II stderr: {TrimSimulationLogDetail(result.StandardError)}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                TrainLog($"DTSA-II stdout: {TrimSimulationLogDetail(result.StandardOutput)}");
        }

        // 260527Codex: 外部プロセス出力を訓練ログ欄へ出すときの上限を決め、画面が埋まりすぎないようにします。
        private static string TrimSimulationLogDetail(string value) =>
            value.Length <= 500 ? value : value[..500];

        //分類モデル
        private async void buttonModelTrain_Click(object sender, EventArgs e)
        {
            // 260416Codex: 学習開始も request -> plan -> workflow の流れに揃え、今後の統合実行へつなげます。
            var request = CreateModelCreationRequest();
            string requestValidationMessage = ValidateModelCreationRequest(request, requireDtsaPath: false);
            if (!string.IsNullOrEmpty(requestValidationMessage))
            {
                MessageBox.Show(requestValidationMessage, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 260901Codex: Do not recover Pending entries while another process may still be producing their EMSA files.
            if (!SpectrumPoolMutationLease.TryAcquire(out var poolMutationLease, out string leaseFailureReason))
            {
                MessageBox.Show(this, leaseFailureReason, "モデル作成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            textBoxModelLog.Clear();
            _operationStopwatch.Restart();
            int trainingPreparationPoolCount = request.SelectedMineralSolutions.Count
                * request.GetEffectiveSpectrumTimeAllocations().Count;
            TrainLog($"学習準備を開始: {trainingPreparationPoolCount} pool の Pending と EMSA を確認します。");
            SetStatusMarqueeWithElapsed("学習準備中: Pending回収");
            buttonModelTrain.Enabled = false;
            buttonRunSpectrumGeneration.Enabled = false;
            checkBoxMeasurementTimePresetB.Enabled = false;

            IReadOnlyList<SpectrumTrainingPool> trainingPools;
            IReadOnlyList<SpectrumPoolShortage> shortages;
            // 260908Codex: Let the existing cancel button stop preparation before training begins.
            using var preparationCancellationTokenSource = new CancellationTokenSource();
            _modelTrainingCancellationTokenSource = preparationCancellationTokenSource;
            buttonCancel.Enabled = true;
            try
            {
                // 260901Codex: 停止済みrunの完成ファイルを回収してから、全時間の厳格な学習pool選択をUIスレッド外で行います。
                var preparationProgress = new Progress<SpectrumPoolPreparationProgress>(ReportSpectrumPoolPreparationProgress);
                // 260908Codex: Share scoped manifest reads across recovery and selection, logging each phase to the UI and file.
                var preparation = await Task.Run(
                    () => _spectrumPoolWorkflow.PrepareTrainingPools(
                        request,
                        preparationProgress,
                        preparationCancellationTokenSource.Token,
                        message =>
                        {
                            TrainLog(message);
                            TensorFlowTrainingDebugLog.Write("training_preparation", TensorFlowTrainingDebugLog.Clean(message));
                        }),
                    preparationCancellationTokenSource.Token);
                preparationCancellationTokenSource.Token.ThrowIfCancellationRequested();
                trainingPools = preparation.Pools;
                shortages = preparation.Shortages;
                TrainLog(
                    $"Pending回収: 確認 {preparation.Recovery.ExaminedCount} 件 / 復旧 {preparation.Recovery.RecoveredCount} 件 / " +
                    $"不正 {preparation.Recovery.InvalidCount} 件 / ファイルなし {preparation.Recovery.MissingFileCount} 件");
            }
            // 260908Codex: User cancellation stops before model creation and still releases the pool lease.
            catch (OperationCanceledException) when (preparationCancellationTokenSource.IsCancellationRequested)
            {
                TrainLog("学習準備をキャンセルしました。");
                SetStatusWithElapsed("モデル作成: 準備キャンセル", 0, ProgressScale);
                return;
            }
            catch (Exception exception)
            {
                // 260901Codex: 物理ヘッダーや2048ch検証の失敗を、学習開始前のpool確認エラーとして通知します。
                TrainLog($"学習 spectrum pool の確認に失敗しました: {exception.GetType().Name}: {exception.Message}");
                SetStatusWithElapsed("モデル作成: pool確認失敗", 0, ProgressScale);
                MessageBox.Show(
                    this,
                    $"学習 spectrum pool の確認に失敗しました。\r\n\r\n{exception.Message}",
                    "モデル作成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
            finally
            {
                // 260901Codex: Training only needs the mutation lease through recovery and the immutable sample snapshot.
                poolMutationLease!.Dispose();
                // 260908Codex: Clear the preparation token before later model training installs its own token.
                if (ReferenceEquals(_modelTrainingCancellationTokenSource, preparationCancellationTokenSource))
                    _modelTrainingCancellationTokenSource = null;
                buttonCancel.Enabled = false;
                buttonModelTrain.Enabled = true;
                buttonRunSpectrumGeneration.Enabled = true;
                checkBoxMeasurementTimePresetB.Enabled = true;
            }

            // 260507Codex: 学習前に pool manifest の Completed 件数を確認し、不足があれば学習へ進みません。
            if (shortages.Count > 0)
            {
                MessageBox.Show(
                    SpectrumPoolWorkflow.FormatShortageMessage(shortages),
                    "スペクトル不足",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var trainingPlan = _modelTrainingWorkflow.CreatePlan(request, trainingPools);
            var validationMessage = _modelTrainingWorkflow.Validate(trainingPlan);
            if (validationMessage is not null)
            {
                MessageBox.Show(validationMessage, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 260619Codex: Confirm before allowing the workflow to replace an existing model folder.
            if (Directory.Exists(trainingPlan.ModelOutputFolder))
            {
                string overwriteMessage =
                    $"同じ名前のモデルが既に存在します。\n\n{trainingPlan.ModelOutputFolder}\n\n上書きしてもよいですか？";
                var overwriteResult = MessageBox.Show(
                    this,
                    overwriteMessage,
                    "モデルの上書き確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (overwriteResult != DialogResult.Yes)
                    return;

                trainingPlan = trainingPlan with { AllowOverwriteExistingModel = true };
            }

            // 260511Codex: FormMain にはモデル群の親フォルダを保持し、個別モデル名フォルダは comboBox 側で選ばせます。
            ModelOutputPath = string.IsNullOrWhiteSpace(request.Paths.ModelOutputFolder)
                ? DefaultStoragePaths.ModelsFolder
                : request.Paths.ModelOutputFolder;

            // 260514Codex: モデル作成中だけ中止ボタンから CancellationToken を要求できるようにします。
            using var cancellationTokenSource = new CancellationTokenSource();
            _modelTrainingCancellationTokenSource = cancellationTokenSource;
            buttonModelTrain.Enabled = false;
            // 260527Codex: 学習中も実行ボタンは見せたまま、同時実行だけを避けます。
            buttonRunSpectrumGeneration.Enabled = false;
            // 260901Codex: 学習poolを確定した後に測定時間構成だけ変わる操作を防ぎます。
            checkBoxMeasurementTimePresetB.Enabled = false;
            buttonCancel.Enabled = true;
            // 260901Codex: pool確認から継続している計測を、学習本体の開始表示へ切り替えます。
            SetStatusWithElapsed("モデル作成開始", 0, ProgressScale);
            var trainingProgress = new Progress<TrainingProgress>(ReportTrainingProgress);

            try
            {
                // 260901Codex: 正式昇格した結果だけをモデル一覧へ反映し、想定内の未昇格は警告として表示します。
                var trainingResult = await _modelTrainingWorkflow.RunAsync(trainingPlan, trainingProgress, cancellationTokenSource.Token);
                if (trainingResult.Status == ModelTrainingStatus.NotPromoted)
                {
                    string failureReason = trainingResult.FailureReason!;
                    bool trainingDataLoadFailed = trainingResult.FailureKind == ModelTrainingFailureKind.TrainingDataLoadFailed;
                    TrainLog(trainingDataLoadFailed
                        ? $"モデル作成を中止しました: {failureReason}"
                        : $"モデルは正式保存されませんでした: {failureReason}");
                    // 260902Codex: 読込失敗は一般的な未昇格と区別し、修復対象がログにあることを通知します。
                    SetStatusWithElapsed(
                        trainingDataLoadFailed ? "モデル作成: 学習データ読込失敗" : "モデル作成: 正式保存なし",
                        toolStripProgressBar1.Value,
                        toolStripProgressBar1.Maximum);

                    string message = trainingDataLoadFailed
                        ? $"{failureReason}\r\n\r\n詳細はモデル作成ログを確認してください。"
                        : $"モデルは正式保存されませんでした。\r\n\r\n{failureReason}";
                    if (trainingResult.PreservedArtifactsFolder is not null)
                        message += $"\r\n\r\n未完了成果物の保存先:\r\n{trainingResult.PreservedArtifactsFolder}";

                    MessageBox.Show(
                        this,
                        message,
                        "モデル作成",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (trainingResult.Status != ModelTrainingStatus.Promoted)
                    throw new InvalidOperationException($"未対応のモデル作成結果です: {trainingResult.Status}");

                FormMain?.RefreshModelPathList(request.ModelName);
                SetStatusWithElapsed("モデル作成完了", ProgressScale, ProgressScale);
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                TrainLog("モデル作成をキャンセルしました");
                SetStatusWithElapsed("モデル作成: キャンセル", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
            }
            catch (Exception exception)
            {
                // 260827Claude: 学習の予期しない失敗はこれまで未処理例外になっていた。Program に ThreadException /
                //   UnhandledException のハンドラが無いため、スペクトル読み込み中の IOException などでアプリごと落ちる。
                //   ここで受け止めてログ・ステータス・通知へ返し、finally のボタン復旧まで必ず通す。
                TrainLog($"モデル作成に失敗しました: {exception.GetType().Name}: {exception.Message}");
                SetStatusWithElapsed("モデル作成: 失敗", toolStripProgressBar1.Value, toolStripProgressBar1.Maximum);
                MessageBox.Show(
                    this,
                    $"モデル作成に失敗しました。\r\n\r\n{exception.Message}",
                    "モデル作成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (ReferenceEquals(_modelTrainingCancellationTokenSource, cancellationTokenSource))
                    _modelTrainingCancellationTokenSource = null;

                buttonModelTrain.Enabled = true;
                buttonRunSpectrumGeneration.Enabled = true;
                checkBoxMeasurementTimePresetB.Enabled = true;
                buttonCancel.Enabled = false;
            }
        }

        private void buttonToggleAllMinerals_Click(object sender, EventArgs e)
        {
            // 260511Codex: 同じ CheckedListBox への繰り返し参照をローカルにまとめます。
            var mineralsList = checkedListBoxMinerals;
            if (mineralsList.Items.Count == 0)
                return;

            // 260511Codex: 1 箇所だけの helper にせず、一括チェック処理をイベント内で完結させます。
            bool newState = mineralsList.CheckedItems.Count != mineralsList.Items.Count;
            mineralsList.BeginUpdate();
            try
            {
                for (int i = 0; i < mineralsList.Items.Count; i++)
                    mineralsList.SetItemChecked(i, newState);
            }
            finally
            {
                mineralsList.EndUpdate();
            }
        }

        private void GeneratorForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 260612Claude: 閉じる操作は破棄せず非表示にするだけなので、後続のクリーンアップが例外を投げても
            // フォームが破棄されず再度開けるよう、先に Cancel と非表示を確定させる。
            e.Cancel = true;
            Visible = false;

            // 260507Codex: 明示リストの設定値だけを保存します。
            SaveUserSettings();
            // 260511Codex: 親を隠すときは子のキャリブレーション画面も破棄せず一緒に隠します。
            _edxCalibrationForm.Visible = false;
            // 260528Claude: 非表示にする際は走行中の組成リスト計算もキャンセルし、再表示時に古い計算結果が UI へ流れ込まないようにします。
            _solutionUpdateCts?.Cancel();
        }

        // 260528Claude: 分解能が変わると候補数が変わるので、組成リストを最新の値で再計算します。重い計算は UpdateSelectedSolution が Task.Run へ逃がし、前回の計算はキャンセルされます。
        private void numericBoxResolution_ValueChanged(object sender, EventArgs e)
        {
            UpdateSelectedSolution();
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            // 260514Codex: 既存の中止ボタンから、現在動いている spectrum 生成またはモデル作成へキャンセル要求を送ります。
            if (_simulationCancellationTokenSource is { IsCancellationRequested: false } simulationCancellationTokenSource)
            {
                TrainLog("spectrum 生成のキャンセルを要求しました。");
                simulationCancellationTokenSource.Cancel();
                buttonCancel.Enabled = false;
                return;
            }

            if (_modelTrainingCancellationTokenSource is not { IsCancellationRequested: false } modelTrainingCancellationTokenSource)
                return;

            TrainLog("モデル作成のキャンセルを要求しました。");
            modelTrainingCancellationTokenSource.Cancel();
            buttonCancel.Enabled = false;
        }
    }
}
