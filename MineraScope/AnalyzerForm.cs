using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
// 260517Codex: graphControl1 に渡すスペクトル点列を Profile/PointD として組み立てます。
using Crystallography;
using Crystallography.Controls;

namespace MineraScope
{
    public partial class AnalyzerForm : Form
    {
        // 260522Codex: 共有モデルカタログ。代入時に購読し、以後はカタログの更新通知だけで一覧を同期します。
        private ModelCatalog? _modelCatalog;

        // 260716Claude: AnalyzerForm の設定ファイル名を保存・復元で共有する。
        private const string UserSettingsFileName = "AnalyzerFormSettings.json";

        // 260716Claude: モデル一覧の構築後に前回選択していたマッピングモデル名を復元する。
        private readonly string _savedSelectedModelName;


        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        internal ModelCatalog ModelCatalog
        {
            set
            {
                _modelCatalog = value;
                _modelCatalog.Changed += OnModelCatalogChanged;
                PopulateMappingModelFolders(string.Empty);
            }
        }

        // 260517Codex: 現在表示中の PTS ファイルをクリック後のグラフタイトルへ反映します。
        private string? _currentPtsFilePath;

        // 260522Codex: Cache clicked spectra by pixel and binning size so experiments do not reuse another bin.
        private readonly Dictionary<(Point Pixel, int BinSize, int? LeadingSweepCount), PtsPixelSpectrum> _pixelSpectrumCache = [];

        // 260716Claude: 最後にグラフ表示したスペクトルを CSV エクスポート用に保持する（SEM クリック・マップクリック共通）。
        // 260825Codex: 表示時の分析条件と分類結果も保持し、単一の自己記述 CSV へ渡す。
        private DisplayedSpectrumExportContext? _lastDisplayedSpectrum;

        // 260522Codex: Keep the first binning candidates small enough for experiments while covering low-count spectra.
        // 260710Codex: Include small bins so mineral maps can be generated with finer spatial resolutions.
        private static readonly int[] BinningSizes = [1, 2, 3, 4, 5, 7, 8, 10, 20];

        // 260522Codex: Default to 10x10 as the first low-count classification baseline.
        private const int DefaultBinningSize = 10;

        // 260612Codex: The loaded PTS sweep count drives the research-only leading-sweep selector.
        private int _loadedSweepCount;

        // 260523Codex: Treat tiny mouse movement as a click so ScalablePictureBox drag panning stays separate.
        private const int ScalableSemClickMoveTolerance = 4;

        // 260523Codex: Keep ownership of the image assigned to scalablePictureBoxSEM so replaced SEM images can be disposed.
        private PseudoBitmap? _semPseudoBitmap;

        // 260527Codex: Prevent duplicate or rapid .pts drops from opening the same file concurrently.
        private bool _isPtsDropLoading;

        // 260527Codex: Ignore rapid image clicks while the previous PTS read/classification is still running.
        private bool _isSpectrumClickBusy;

        // 260523Codex: Remember the ScalablePictureBox left-button start point until MouseUp2 decides click vs pan.
        // 260526Claude: SEM/マップ両ボックスで共有するため名前を一般化。
        private Point? _scalableMouseDownPoint;

        // 260519Codex: 後から完了した古いクリック読み取りが最新表示を上書きしないようにします。
        private int _spectrumReadVersion;

        // 260522Codex: One service instance keeps the loaded classification model warm across clicks.
        // 260529Claude: マップ実行後もモデルを解放しない。clear_session() の繰り返しが TF の eager context/スレッドプールをリークさせ、stale なリソース変数で次の predict が abort するため、起動中は常駐させる。
        private readonly MineralClassificationPredictionService _classificationService = new();

        // 260828Codex: 読み込めない未学習検知器の警告は、同じモデルについてフォーム起動中に一度だけ表示する。
        private readonly HashSet<string> _warnedUnknownDetectionModelPaths = new(StringComparer.OrdinalIgnoreCase);

        // 260526Claude: 鉱物マッピングの状態。result は Top-1 のみ軽量保持し、クリック再分類は作成時条件で再読みする。
        private PtsClassificationMapResult? _classificationMap;
        private PseudoBitmap? _mapPseudoBitmap;
        private bool _isMappingBusy;
        private int _mapBuildVersion;
        private CancellationTokenSource? _mapClassificationCancellation;

        // 260716Claude: SEM⇔マップ viewport の相互同期用。ZoomAndCenter 設定は相手側の DrawingAreaChanged を
        //   無条件に発火させるため、このフラグで逆流 (無限再帰) を止める。
        private bool _isSyncingViewports;

        // 260528Claude: 凡例ハイライト用に colorizer 出力を保持。palette だけ差し替えて bitmap を作り直すための源データ。
        private MineralMapImage? _mapImage;

        // 260717Codex: Keep one global mineral-name palette alive across PTS drops and mapping-condition changes.
        private readonly MineralColorPalette _mineralColorPalette;
        private string? _mineralColorPaletteLoadWarning;

        // 260528Codex: Keep legend highlighting independent from ListBox.SelectedIndex timing.
        private int _highlightLegendIndex = -1;

        // 260527Codex: Name the shared prediction-service gate used by click and full-map classification.
        private bool IsInteractiveClassificationBusy => _isMappingBusy || _isSpectrumClickBusy;

        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            // 260717Codex: Load persisted colors before any map can register its TOP20 minerals.
            _mineralColorPalette = MineralColorPalette.Load(out _mineralColorPaletteLoadWarning);
            // 260716Claude: 前回選択したマッピングモデル名を復元する (ModelCatalog 代入時の一覧構築で使う)。
            // 260727Claude: 壊れた設定は既定値へフォールバックし、理由は FormMain_Load でまとめて知らせる。
            // 260807Codex: 設定ファイルは一度だけ読み、モデル選択とマップ未学習検知の両方を同じスナップショットから復元する。
            var settings = FormUserSettingsStore.Load<AnalyzerFormUserSettings>(UserSettingsFileName).Settings;
            _savedSelectedModelName = settings.SelectedMappingModelName;
            // 260807Codex: Checked の代入は Designer 既定値の上書きではなく、InitializeComponent 後の永続値復元として行う。
            checkBoxunknown.Checked = settings.MapUnknownDetectionEnabled;
            InitializeBinningOptions();
            InitializeSweepOptions(0);
            // 260526Claude: 待機状態（マッピング有効・中止無効）を初期化する。
            // 260807Codex: マッピング関連のボタンと設定コントロールをまとめて初期化する。
            UpdateMappingControls();
            // 260523Claude: .pts ドロップを復活。フォーム本体は Designer で DragEnter/DragDrop 済みなので AllowDrop だけ立て、
            // 子孫側は再帰で有効化する（ScalablePictureBox は内部 pictureBox がドロップ先になり、Designer では再帰配線できないため）。
            AllowDrop = true;
            // 260527Codex: scalablePictureBoxSEM itself is already wired by the Designer; only its descendants need runtime wiring.
            ControlDropHelper.EnableRecursive(this, AnalyzerForm_DragEnter, AnalyzerForm_DragDrop, scalablePictureBoxSEM);
        }

        // 260522Codex: Store binning choices as typed combo items instead of parsing display text later.
        private sealed record BinningOption(int Size)
        {
            public override string ToString() => $"{Size}×{Size}";
        }

        // 260612Codex: Keep combo box items typed so the displayed text and sweep value cannot diverge.
        private sealed record SweepOption(int Count)
        {
            public override string ToString() => Count.ToString(CultureInfo.InvariantCulture);
        }

        // 260612Codex: Pair the SEM image with the PTS acquisition sweep count discovered during load.
        private sealed record LoadedPtsData(PseudoBitmap SemImage, int TotalFrames);

        // 260522Codex: Populate the binning selector once, defaulting to the low-count experimental baseline.
        private void InitializeBinningOptions()
        {
            comboBoxBinning.BeginUpdate();
            try
            {
                comboBoxBinning.Items.Clear();
                foreach (int binningSize in BinningSizes)
                {
                    int itemIndex = comboBoxBinning.Items.Add(new BinningOption(binningSize));
                    if (binningSize == DefaultBinningSize)
                        comboBoxBinning.SelectedIndex = itemIndex;
                }

                if (comboBoxBinning.SelectedIndex < 0 && comboBoxBinning.Items.Count > 0)
                    comboBoxBinning.SelectedIndex = 0;
            }
            finally
            {
                comboBoxBinning.EndUpdate();
            }
        }

        // 260522Codex: Fall back to the planned default if the designer state is ever empty.
        private int GetSelectedBinningSize() =>
            comboBoxBinning.SelectedItem is BinningOption option
                ? option.Size
                : DefaultBinningSize;

        // 260612Codex: Populate one candidate per available PTS sweep and default to the full acquisition.
        private void InitializeSweepOptions(int totalFrames)
        {
            _loadedSweepCount = Math.Max(0, totalFrames);
            comboBox1.BeginUpdate();
            try
            {
                comboBox1.Items.Clear();
                for (int sweep = 1; sweep <= _loadedSweepCount; sweep++)
                    comboBox1.Items.Add(new SweepOption(sweep));

                comboBox1.Enabled = _loadedSweepCount > 0;
                comboBox1.SelectedIndex = comboBox1.Items.Count > 0 ? comboBox1.Items.Count - 1 : -1;
            }
            finally
            {
                comboBox1.EndUpdate();
            }
        }

        // 260612Codex: Return the selected leading sweep count exactly; PTSFile normalizes the max value to full-read.
        private int? GetSelectedLeadingSweepCount()
        {
            if (_loadedSweepCount <= 0 || comboBox1.SelectedItem is not SweepOption option)
                return null;

            return option.Count;
        }

        // 260522Codex: カタログ更新時にマッピングモデル選択コンボを再描画します。
        private void OnModelCatalogChanged(object? sender, ModelCatalogChangedEventArgs e)
            => PopulateMappingModelFolders(e.PreferredModelName);

        private void PopulateMappingModelFolders(string preferredModelName)
        {
            if (_modelCatalog is null)
                return;

            // 260716Claude: コンボが未選択の初回だけ前回保存のモデル名へ復元する。以降は preferred / 現在選択が優先される
            //   (Populate 内で previousSelection が使われる) ため、ユーザーの選び直しを上書きしない。
            if (string.IsNullOrWhiteSpace(preferredModelName) && comboBoxMappingModellFolder.SelectedItem is null)
                preferredModelName = _savedSelectedModelName;

            ModelComboBinder.Populate(comboBoxMappingModellFolder, _modelCatalog.ModelNames, preferredModelName);
        }

        private void AnalyzerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Visible = false;

            // 260716Claude: 閉じる (非表示) 操作でもマッピングモデルの選択を保存し、次回起動時に復元できるようにする。
            SaveUserSettings();
        }

        // 260716Claude: 次回起動時に戻すのはマッピングモデルの選択だけ。アプリ終了時は FormMain からも呼ばれる。
        // 260807Codex: 現在はマッピングモデルの選択と未学習検知設定を保存し、PTS 表示やマップ結果は保存しない。
        // 260717Codex: Save both UI selection and dirty mineral colors on hide or application shutdown.
        internal void SaveUserSettings()
        {
            // 260728Claude: 上書き可否の判定は FormUserSettingsStore.Save が持つので、ここは値の組み立てだけ。
            FormUserSettingsStore.Save(
                UserSettingsFileName,
                new AnalyzerFormUserSettings
                {
                    SelectedMappingModelName = comboBoxMappingModellFolder.SelectedItem as string ?? string.Empty,
                    // 260807Codex: 初回既定 OFF のチェック状態を次回起動時に復元できるよう保存する。
                    MapUnknownDetectionEnabled = checkBoxunknown.Checked
                });

            SaveMineralColorPalette();
        }

        // 260717Codex: Report persistence failures because silently losing fixed publication colors would be misleading.
        private void SaveMineralColorPalette()
        {
            if (_mineralColorPalette.TrySave(out string? error))
                return;

            MessageBox.Show(
                $"鉱物色設定を保存できませんでした。\r\n{error}",
                "鉱物色設定",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // 260717Codex: Delay a corrupt-palette warning until the user actually starts mineral mapping.
        private void ShowMineralColorPaletteLoadWarning()
        {
            if (string.IsNullOrWhiteSpace(_mineralColorPaletteLoadWarning))
                return;

            MessageBox.Show(
                _mineralColorPaletteLoadWarning,
                "鉱物色設定",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _mineralColorPaletteLoadWarning = null;
        }

        // 260519Codex: .pts ドロップ時は SEM画像だけを読み込み、EDXスペクトルはクリック時に1ピクセルだけ読みます。
        private async void AnalyzerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (_isPtsDropLoading)
                return;

            if (!TryGetSingleDroppedPtsFile(e, out var filePath))
                return;

            _isPtsDropLoading = true;
            // 260818Claude: 状態を変える処理は try の内側へ置く。ここで投げると finally へ到達せず、
            //   _isPtsDropLoading が立ったまま残って以後の .pts ドロップが無視される。
            try
            {
                ClearLoadedPtsData();
                UseWaitCursor = true;
                // 260612Codex: Load the SEM image and sweep count together so the selector matches the active PTS file.
                LoadedPtsData? loadedPts = await Task.Run(() => LoadPtsData(filePath));

                if (loadedPts is null)
                {
                    MessageBox.Show(
                        "このPTSファイルからSEM画像を読み取れませんでした。",
                        "PTS SEM画像",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                _currentPtsFilePath = filePath;
                InitializeSweepOptions(loadedPts.TotalFrames);
                SetSemPseudoBitmap(loadedPts.SemImage);
                graphControl1.GraphTitle = Path.GetFileName(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"PTSファイルからSEM画像を読み取れませんでした。\r\n{ex.Message}",
                    "PTS SEM画像",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                // 260818Codex: Release the drop guard first so a cursor-property failure cannot permanently block later drops.
                _isPtsDropLoading = false;
                UseWaitCursor = false;
            }
        }

        // 260519Codex: PTS の SEM画像だけをバックグラウンド側で読み込みます。
        // 260523Codex: Read the PTS SEM byte image and flatten it into ScalablePictureBox's row-major source data.
        // 260612Codex: Return the SEM image with the acquisition sweep count used by the research selector.
        private static LoadedPtsData? LoadPtsData(string filePath)
        {
            using var pts = new PTSFile(filePath);
            byte[,]? image = pts.TryReadSemImage();
            if (image is null)
                return null;

            int width = image.GetLength(0);
            int height = image.GetLength(1);
            var values = new double[width * height];

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    values[x + y * width] = image[x, y];

            return new LoadedPtsData(new PseudoBitmap(values, width), pts.TotalFrames);
        }

        // 260519Codex: 新しい PTS を読み込む前に古い画像・キャッシュ・グラフ表示を破棄します。
        // 260526Claude: 走行中マッピングを無効化・中止し、マップ表示と結果も破棄する。
        private void ClearLoadedPtsData()
        {
            _currentPtsFilePath = null;
            _pixelSpectrumCache.Clear();
            // 260825Claude: 別の PTS を読んだ後もエクスポートが古いスペクトルを書き出していた。併設レポートが PTS 名・モデル・
            //   分類結果まで記録するようになり、古い状態が正規の記録に見えるようになったので、ここで確実に捨てる。
            _lastDisplayedSpectrum = null;
            InitializeSweepOptions(0);
            _spectrumReadVersion++;
            _mapBuildVersion++;
            _mapClassificationCancellation?.Cancel();
            _classificationMap = null;
            // 260528Claude: Items.Clear が SelectedIndexChanged を発火して RebuildMapBitmapForSelection が走るので、源データを先に無効化する。
            _mapImage = null;
            _highlightLegendIndex = -1;
            // 260526Claude: 凡例も合わせて破棄（PTS が変わったら色対応も無効）。
            // 260717Codex: 現在は表示中の凡例だけを破棄し、鉱物名ごとのグローバル色設定は維持する。
            listBoxLegend.Items.Clear();
            SetMapPseudoBitmap(null);
            SetSemPseudoBitmap(null);
            graphControl1.GraphTitle = "";
            graphControl1.ClearProfile();
        }

        // 260516Codex: 単一の .pts ファイルだけをSEM画像表示用のドロップ対象として受け付けます。
        private void AnalyzerForm_DragEnter(object? sender, DragEventArgs e)
            => e.Effect = TryGetSingleDroppedPtsFile(e, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        // 260523Claude: scalablePictureBoxSEM に表示する SEM 画像を差し替え、置き換え前の PseudoBitmap を破棄する。
        private void SetSemPseudoBitmap(PseudoBitmap? semImage)
        {
            // 260605Claude: 新しい SEM 画像は明るさ・コントラストを初期状態(撮影したまま)へ戻す。
            // viewer へ渡す前に表示窓を確定し、差し替え時の描画1回で正しく表示する(余分な再描画を避ける)。
            if (semImage is not null)
            {
                trackBarBrightness.Value = 0;
                trackBarContrast.Value = 0;
                ApplyBrightnessContrastWindow(semImage);
            }
            ReplacePseudoBitmap(scalablePictureBoxSEM, ref _semPseudoBitmap, semImage);
        }

        // 260605Claude: 明るさ・コントラストのスライダー値を SEM 表示へ反映して再描画する。
        private void ApplySemBrightnessContrast()
        {
            if (_semPseudoBitmap is null)
                return;
            ApplyBrightnessContrastWindow(_semPseudoBitmap);
            scalablePictureBoxSEM.drawPictureBox();
        }

        // 260605Claude: 実機SEMと同じ「表示 = コントラスト×raw + 明るさ」を、PseudoBitmap が扱う表示窓(Min/Max)へ逆算して設定する。
        private void ApplyBrightnessContrastWindow(PseudoBitmap bitmap)
        {
            double contrast = Math.Pow(2.0, trackBarContrast.Value / 50.0); // ゲイン: 値0→1倍, ±50→2倍/0.5倍
            double brightness = trackBarBrightness.Value;                   // オフセット(表示の明るさ)
            bitmap.MinValue = -brightness / contrast;
            bitmap.MaxValue = (255.0 - brightness) / contrast;
        }

        // 260526Codex: SEM と鉱物マップの PseudoBitmap 差し替え処理を共通化します。
        private static void ReplacePseudoBitmap(ScalablePictureBox viewer, ref PseudoBitmap? current, PseudoBitmap? next)
        {
            PseudoBitmap? previous = current;
            current = next;
            viewer.ShowAreaRectangle = false;
            if (next is null)
            {
                // 260527Codex: Clear the viewer without drawing a 1x1 fallback image, which shows ScalablePictureBox's green out-of-range color.
                viewer.SkipDrawing = true;
                viewer.pictureBox.Image = null;
                viewer.Refresh();
            }
            else
            {
                viewer.SkipDrawing = false;
                viewer.PseudoBitmap = next;
            }

            previous?.Dispose();
        }

        // 260516Codex: ドロップされたファイル一覧から単一の .pts ファイルだけを安全に取り出します。
        private static bool TryGetSingleDroppedPtsFile(DragEventArgs e, out string filePath)
        {
            filePath = string.Empty;

            IDataObject? dataObject = e.Data;
            if (dataObject is null || !dataObject.GetDataPresent(DataFormats.FileDrop))
                return false;

            if (dataObject.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
                return false;

            if (!File.Exists(files[0]) ||
                !string.Equals(Path.GetExtension(files[0]), ".pts", StringComparison.OrdinalIgnoreCase))
                return false;

            filePath = files[0];
            return true;
        }

        // 260523Codex: Designer-connected ScalablePictureBox MouseDown2 starts click-vs-pan tracking.
        private bool scalablePictureBoxSEM_MouseDown2(object sender, MouseEventArgs e, PointD pt)
        {
            _scalableMouseDownPoint = e.Button == MouseButtons.Left && e.Clicks == 1
                ? e.Location
                : null;
            return false;
        }

        // 260523Codex: Designer-connected ScalablePictureBox MouseUp2 reads the clicked image pixel without blocking pan/zoom behavior.
        // 260526Claude: sender で SEM とマップを分岐。SEM は中心ビニング、マップは作成時条件で該当ブロックを再分類する。
        private bool scalablePictureBoxSEM_MouseUp2(object sender, MouseEventArgs e, PointD pt)
        {
            if (e.Button == MouseButtons.Left && IsScalableClick(e.Location))
                HandleScalableClick(sender, pt);

            _scalableMouseDownPoint = null;
            return false;
        }

        // 260526Codex: イベントハンドラ側の入れ子を減らし、SEM とマップのクリック処理だけを分岐します。
        private void HandleScalableClick(object sender, PointD sourcePoint)
        {
            if (ReferenceEquals(sender, scalablePictureBoxSEM))
            {
                if (TryGetImagePixel(_semPseudoBitmap, sourcePoint, clamp: true, out int x, out int y))
                    _ = RunSpectrumClickAsync(() => ReadAndDisplayBinnedPixelAsync(new Point(x, y)));
                return;
            }

            if (!ReferenceEquals(sender, scalablePictureBoxMap) || _classificationMap is null)
                return;

            if (TryGetImagePixel(_mapPseudoBitmap, sourcePoint, clamp: false, out int blockX, out int blockY))
                _ = RunSpectrumClickAsync(() => DisplayMapPixelAsync(new Point(blockX, blockY)));
        }

        // 260527Codex: TensorFlow の予測と UI 更新が交錯しないよう、マップ作成中は画像クリックを受け付けない。
        private async Task RunSpectrumClickAsync(Func<Task> action)
        {
            if (IsInteractiveClassificationBusy)
                return;

            _isSpectrumClickBusy = true;
            // 260807Codex: クリック分類中はマップ作成条件も変更不可にする。
            UpdateMappingControls();
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"クリック位置のスペクトル表示に失敗しました。\r\n{ex.Message}",
                    "PTS EDXスペクトル",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                _isSpectrumClickBusy = false;
                // 260807Codex: クリック分類の終了時にマッピング関連コントロールを待機状態へ戻す。
                UpdateMappingControls();
            }
        }

        // 260523Codex: Keep ScalablePictureBox click handling tolerant of tiny hand movement but not drag panning.
        private bool IsScalableClick(Point mouseUpPoint)
        {
            if (_scalableMouseDownPoint is not { } mouseDownPoint)
                return false;

            return Math.Abs(mouseUpPoint.X - mouseDownPoint.X) <= ScalableSemClickMoveTolerance &&
                Math.Abs(mouseUpPoint.Y - mouseDownPoint.Y) <= ScalableSemClickMoveTolerance;
        }

        // 260523Codex: Convert ScalablePictureBox source coordinates into an image pixel index.
        // 260526Claude: 対象 PseudoBitmap を引数化。SEM は端へクランプ、マップは範囲外を無視する。
        private static bool TryGetImagePixel(PseudoBitmap? bitmap, PointD sourcePoint, bool clamp, out int imageX, out int imageY)
        {
            imageX = 0;
            imageY = 0;

            if (bitmap is null || bitmap.Width <= 1 || bitmap.Height <= 1)
                return false;

            int x = ToDisplayedPixelIndex(sourcePoint.X);
            int y = ToDisplayedPixelIndex(sourcePoint.Y);

            if (!clamp && ((uint)x >= (uint)bitmap.Width || (uint)y >= (uint)bitmap.Height))
                return false;

            imageX = clamp ? Math.Clamp(x, 0, bitmap.Width - 1) : x;
            imageY = clamp ? Math.Clamp(y, 0, bitmap.Height - 1) : y;
            return true;
        }

        // 260527Codex: Match PseudoBitmap's display sampler, where integer source coordinates represent pixel centers.
        private static int ToDisplayedPixelIndex(double sourceCoordinate)
            => (int)Math.Floor(sourceCoordinate + 0.5);

        // 260523Claude: SEM クリック位置のビニング済みスペクトルを読み込み、グラフ表示と分類まで行う。
        // 260526Claude: グラフ描画・分類は共通メソッドへ寄せ、SEM 固有はコンボの bin/model 取得と SEM 枠への範囲描画のみ。
        private async Task ReadAndDisplayBinnedPixelAsync(Point pixel)
        {
            if (string.IsNullOrWhiteSpace(_currentPtsFilePath))
                return;

            string filePath = _currentPtsFilePath;
            int binSize = GetSelectedBinningSize();
            int? leadingSweepCount = GetSelectedLeadingSweepCount();
            int readVersion = ++_spectrumReadVersion;
            PtsPixelSpectrum? pixelSpectrum = await GetPixelSpectrumAsync(filePath, pixel, binSize, leadingSweepCount);
            if (readVersion != _spectrumReadVersion || pixelSpectrum is null)
                return;

            ShowBinningArea(pixelSpectrum);
            (string modelPath, string modelName) = GetSelectedMappingModel();
            // 260807Codex: SEM 画像の直接クリックは従来どおり常に未学習検知を要求する。
            // 260826Claude: 分析条件を知っているのは呼び出し側なので、context もここで組み立てる。
            var context = new DisplayedSpectrumExportContext(
                pixelSpectrum,
                Path.GetFileName(filePath),
                leadingSweepCount,
                modelName,
                UnknownDetectionRequested: true,
                DetectUnknownForPrediction: true,
                Classification: null);
            await DisplaySpectrumAndClassifyAsync(context, modelPath, readVersion, mapBlock: null);
        }

        // 260523Codex: Draw the actual clamped binning rectangle on the SEM image after a pixel is selected.
        // 260716Claude: SEM クリック・マップクリック共通の選択枠表示に統合。矩形は表示系の「整数座標=ピクセル中心」
        //   に合わせてピクセル端 (-0.5) 基準へ修正し、マップ側にも同じ範囲の黄枠を映す。
        private void ShowBinningArea(PtsPixelSpectrum pixelSpectrum)
        {
            scalablePictureBoxSEM.AreaRectangle = new RectangleD(
                pixelSpectrum.BinLeft - 0.5,
                pixelSpectrum.BinTop - 0.5,
                pixelSpectrum.BinRight - pixelSpectrum.BinLeft + 1,
                pixelSpectrum.BinBottom - pixelSpectrum.BinTop + 1);
            scalablePictureBoxSEM.ShowAreaRectangle = true;
            SyncMapAreaToSem();
        }

        // 260716Claude: SEM 側の黄枠 (SEM ピクセル座標) をマップのブロック座標へ変換し、同じ位置に表示する。
        //   端座標の変換は viewport 同期と同じ式 (map = (sem + 0.5) / bin - 0.5)。マップ未作成時は何もしない。
        private void SyncMapAreaToSem()
        {
            if (_classificationMap is null || _mapPseudoBitmap is null)
                return;

            int binSize = _classificationMap.BinSize;
            if (binSize <= 0)
                return;

            RectangleD semRect = scalablePictureBoxSEM.AreaRectangle;
            scalablePictureBoxMap.AreaRectangle = new RectangleD(
                (semRect.X + 0.5) / binSize - 0.5,
                (semRect.Y + 0.5) / binSize - 0.5,
                semRect.Width / binSize,
                semRect.Height / binSize);
            scalablePictureBoxMap.ShowAreaRectangle = scalablePictureBoxSEM.ShowAreaRectangle;
        }

        // 260526Claude: コンボで選択中のマッピングモデルのパスと名前を返す（SEM クリックとマッピング開始で共有）。
        private (string ModelPath, string ModelName) GetSelectedMappingModel()
        {
            string modelName = comboBoxMappingModellFolder.SelectedItem as string ?? string.Empty;
            string parentPath = _modelCatalog?.ParentPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelName) || string.IsNullOrWhiteSpace(parentPath))
                return (string.Empty, modelName);

            return (Path.Combine(parentPath, modelName), modelName);
        }

        // 260526Claude: 取得済みスペクトルのグラフ表示と分類を SEM クリック/マップクリックで共通化する。確率一覧は 0.1% 未満を非表示。
        // 260831Codex: 現在は固定の 0.1% ではなく、百分率を小数点以下2桁へ丸めて 0.00% になる候補だけを隠す。
        // 260612Codex: マップクリックはブロック座標を渡し、結果を先に置いた位置表示を出せるようにする。
        // 260807Codex: クリック元に応じた未学習検知条件を単発予測へ渡す。
        // 260825Codex: スペクトルと同時に保存する sweep 条件を受け取る。
        // 260826Claude: 分析条件は context へ畳んで受け取る。ここで組み直していた頃は、条件を 1 つ足すたびに
        //   record と引数と両方の呼び出し側を直す必要があった。
        private async Task DisplaySpectrumAndClassifyAsync(DisplayedSpectrumExportContext context, string modelPath, int readVersion, Point? mapBlock)
        {
            PtsPixelSpectrum spectrum = context.Spectrum;
            string binningLabel = context.DescribeBinning();
            string rangeLabel = context.DescribeRange();

            // 260716Claude: CSV エクスポート対象として、表示した生スペクトルをそのまま保持する。
            // 260825Codex: 分類前や分類失敗時でも CSV と測定条件を保存できるよう、結果 null で先に保持する。
            _lastDisplayedSpectrum = context;

            graphControl1.LabelX = "Energy";
            graphControl1.UnitX = "keV";
            graphControl1.LabelY = "Counts";
            graphControl1.UnitY = "";
            graphControl1.GraphTitle = $"{context.FileName} [{rangeLabel}] {binningLabel}";

            // 260830Codex: 測定スペクトルは選択モデルに依存させず、PTS の生カウントをグラフへ表示する。
            graphControl1.Profile = CreateSpectrumProfile(spectrum);
            graphControl1.Refresh();

            // 260830Codex: モデル未選択時は分類用の前処理を読まず、生スペクトル表示だけで終了する。
            if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(context.ModelName))
            {
                textBox1.Text = "モデルフォルダが選択されていません。";
                return;
            }

            // 260626Claude: 分類モデルの前処理を読み、学習時と同じ低エネルギーマスクをクリック分析にも自動適用する。
            //   preprocessing.json が無い既存モデルは None = マスク無しで従来どおり。
            var preprocessing = SpectrumPreprocessing.LoadFromModelFolder(ModelArtifactPaths.GetClassificationFolder(modelPath));

            float[]? normalizedSpectrum = SpectrumDataLoader.CreateNormalizedSpectrum(spectrum, preprocessing, out bool hasSignal);
            if (normalizedSpectrum is null)
            {
                textBox1.Text =
                    $"選択範囲のスペクトル長は {spectrum.ChannelCount} 点です。\r\n" +
                    $"分類モデルは {SpectrumDataLoader.SpectrumLength} 点の入力に対応しています。";
                return;
            }

            // 260825Claude: マップ側は NormalizeInto が信号なしを false で返して未判定にする。クリック側も同じ規則で弾く。
            //   ここを通していた頃は、X 線が 1 つも来ていない範囲でも全ゼロのスペクトルがそのまま分類へ流れ、
            //   根拠のない鉱物名と信頼度が表示されていた。マップとクリックで同じ画素の扱いをそろえる。
            if (!hasSignal)
            {
                // 260830Codex: 生スペクトルが全ゼロの場合と、前処理後に信号が残らない場合のどちらにも正しい文言にする。
                textBox1.Text =
                    $"この範囲には分類に使用できるX線信号がありません。\r\n" +
                    $"範囲 [{rangeLabel}] / ビニング {binningLabel}\r\n" +
                    "マップでは未判定として扱われる画素です。";
                return;
            }

            textBox1.Text = $"分類中... 範囲 [{rangeLabel}] / ビニング {binningLabel}";
            UseWaitCursor = true;

            try
            {
                // 260606Claude: Task.Run はプールの別スレッドに乗り TF ワーカーを増殖させるため、専用スレッドへ集約する PredictAsync を直接 await する。
                // 260807Codex: マップクリックでは作成時の実適用条件を再現し、SEM クリックでは常に true を受け取る。
                var result = await _classificationService.PredictAsync(modelPath, normalizedSpectrum, context.DetectUnknownForPrediction);

                if (readVersion != _spectrumReadVersion)
                    return;

                // 260825Codex: 分類成功後だけ、保存対象の文脈に予測結果を追加する。
                // 260826Claude: 結果欄の条件ブロックも同じ context から作り、画面と .txt の条件がずれないようにする。
                // 260828Codex: 条件と結果は併設ファイルではなく、スペクトルと同じ自己記述 CSV に保存する。
                var classified = context with { Classification = result };
                _lastDisplayedSpectrum = classified;
                textBox1.Lines = BuildClickAnalysisLines(result, classified, mapBlock).ToArray();
                // 260828Codex: ON 要求を適用できなかった場合は、通常表示とは別に原因を明示する。
                if (context.UnknownDetectionRequested && !result.UnknownDetectionApplied)
                    WarnUnknownDetectionUnavailable(modelPath);
            }
            catch (Exception ex)
            {
                if (readVersion == _spectrumReadVersion)
                    textBox1.Text = $"分類に失敗しました。\r\n{ex.Message}";
            }
            finally
            {
                if (readVersion == _spectrumReadVersion)
                    UseWaitCursor = false;
            }
        }

        // 260522Codex: Read or reuse the clicked spectrum for the selected binning size.
        private async Task<PtsPixelSpectrum?> GetPixelSpectrumAsync(string filePath, Point pixel, int binSize, int? leadingSweepCount)
        {
            var cacheKey = (Pixel: pixel, BinSize: binSize, LeadingSweepCount: leadingSweepCount);
            if (_pixelSpectrumCache.TryGetValue(cacheKey, out var cachedSpectrum))
                return cachedSpectrum;

            UseWaitCursor = true;
            try
            {
                PtsPixelSpectrum? spectrum = await Task.Run(() =>
                {
                    using var pts = new PTSFile(filePath);
                    return pts.TryReadBinnedPixelSpectrum(pixel.X, pixel.Y, binSize, leadingSweepCount);
                });
                if (spectrum is null)
                {
                    MessageBox.Show(
                        "このピクセルのEDXスペクトルを読み取れませんでした。",
                        "PTS EDXスペクトル",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return null;
                }

                _pixelSpectrumCache[cacheKey] = spectrum;
                return spectrum;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"PTSファイルからEDXスペクトルを読み取れませんでした。\r\n{ex.Message}",
                    "PTS EDXスペクトル",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        // 260830Codex: クリック範囲の全チャンネルカウントを GraphControl 用 Profile に変換する。
        // 260830Codex: グラフは測定値の確認に使うため、モデルの低エネルギーマスクを適用しない。
        private static Profile CreateSpectrumProfile(PtsPixelSpectrum spectrum)
        {
            var points = new List<PointD>(spectrum.ChannelCount);
            for (int channel = 0; channel < spectrum.ChannelCount; channel++)
                points.Add(new PointD(spectrum.GetEnergy(channel), spectrum.GetCount(channel)));

            return new Profile(points);
        }

        // 260526Claude: 全ブロック鉱物マッピングを開始する。実行中は中止ボタン側を有効化し、完了時に stale/キャンセルを判定する。
        private async void buttonClassifyMap_Click(object sender, EventArgs e)
        {
            if (IsInteractiveClassificationBusy)
                return;

            // 260717Codex: Warn before an empty fallback palette can assign replacement colors.
            ShowMineralColorPaletteLoadWarning();

            if (string.IsNullOrWhiteSpace(_currentPtsFilePath))
            {
                MessageBox.Show("先にPTSファイルを読み込んでください。", "鉱物マッピング", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            (string modelPath, string modelName) = GetSelectedMappingModel();
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                MessageBox.Show("モデルフォルダが選択されていません。", "鉱物マッピング", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string filePath = _currentPtsFilePath;
            int binSize = GetSelectedBinningSize();
            int? leadingSweepCount = GetSelectedLeadingSweepCount();
            // 260807Codex: 実行開始時のチェック状態を分析条件として固定する。
            bool detectUnknown = checkBoxunknown.Checked;
            int buildVersion = ++_mapBuildVersion;
            using var cancellation = new CancellationTokenSource();
            _mapClassificationCancellation = cancellation;
            _isMappingBusy = true;
            // 260807Codex: 実行中は未学習検知設定を含むマッピング関連コントロールを固定する。
            UpdateMappingControls();
            var progress = new Progress<double>(ReportMappingProgress);

            try
            {
                PtsClassificationMapResult result = await Task.Run(() => PtsClassificationMapWorkflow.Run(
                    // 260807Codex: マップ開始時に固定した要求値をワークフローへ渡す。
                    filePath, binSize, modelPath, modelName, leadingSweepCount, detectUnknown, _classificationService, progress, cancellation.Token));

                // 260526Claude: 計算中に PTS や条件が変わっていたら反映しない。
                if (buildVersion != _mapBuildVersion || !string.Equals(_currentPtsFilePath, filePath, StringComparison.Ordinal))
                    return;

                // 260717Codex: Resolve and display colors only after stale-map checks pass.
                ApplyMineralMap(result);
                // 260807Codex: ON 要求を適用できなかった理由を、生成済みマップを表示した後に通知する。
                if (result.UnknownDetectionRequested && !result.UnknownDetectionApplied)
                    WarnUnknownDetectionUnavailable(result.ModelPath);
                SetMappingStatus(string.Empty, 0);
            }
            catch (OperationCanceledException)
            {
                // 260526Claude: 中止時は既存マップを残し、途中結果は反映しない。
                SetMappingStatus("マッピングを中止しました", 0);
            }
            catch (Exception ex)
            {
                SetMappingStatus(string.Empty, 0);
                MessageBox.Show($"鉱物マッピングに失敗しました。\r\n{ex.Message}", "鉱物マッピング", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _isMappingBusy = false;
                if (ReferenceEquals(_mapClassificationCancellation, cancellation))
                    _mapClassificationCancellation = null;
                // 260807Codex: マップ作成終了時に設定コントロールを再び変更可能にする。
                UpdateMappingControls();
            }
        }

        // 260526Claude: 中止ボタンは取り消し要求だけ行い、状態表示を更新する。
        private void buttonCancelMap_Click(object sender, EventArgs e)
        {
            _mapClassificationCancellation?.Cancel();
            // 260807Codex: 中止要求中もマッピング関連コントロールの状態を一元更新する。
            UpdateMappingControls();
        }

        // 260623Claude: 表示中の鉱物マップ視野を、外部 BSE と同寸法の予測画像群 (8bitラベル/RGB/classes.csv/metadata) として出力する。
        // ラベルは RGB の逆算ではなくモデルの top1 クラスID配列を直接 uint8 化し、グリッドを外部 BSE 寸法へ最近傍スケールして同視野・同座標に揃える。
        // ROI を引く BSE はユーザーの外部電子像 (JEOL View0xx IMG1.bmp 等) を使う前提なので、アプリは BSE を出力しない。
        private async void exportMapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 260717Codex: IsInteractiveClassificationBusy にマッピング中の状態も含まれる。
            if (IsInteractiveClassificationBusy)
                return;

            PtsClassificationMapResult? map = _classificationMap;
            MineralMapImage? image = _mapImage;
            if (map is null || image is null)
            {
                MessageBox.Show("先に鉱物マッピングを実行してください。", "エクスポート", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new FolderBrowserDialog { Description = "出力先フォルダを選択" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            string outputDir = dialog.SelectedPath;
            UseWaitCursor = true;
            try
            {
                await Task.Run(() => MineralMapImageExporter.ExportCurrentView(outputDir, map, image, map.LabelNames));
                // 260717Codex: A successful export is a persistence boundary for newly assigned or manually changed colors.
                SaveMineralColorPalette();
                SetMappingStatus($"エクスポート完了: {outputDir}", 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エクスポートに失敗しました。\r\n{ex.Message}", "エクスポート", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        // 260716Claude: 最後にグラフ表示したスペクトル（SEM/マップクリック）を、マスク適用前の生カウントで CSV 出力する（Designer で Click を接続）。
        // 260721Codex: Designer のコントロール名にイベントハンドラー名を合わせる。
        // 260828Codex: 測定条件と分類結果も、別ファイルを作らず同じ自己記述 CSV へ保存する。
        private void exportSpectrumToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_lastDisplayedSpectrum is not { } last)
            {
                MessageBox.Show(
                    "エクスポートできるスペクトルがありません。先に SEM 画像かマップをクリックしてスペクトルを表示してください。",
                    "スペクトルCSV出力", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 260825Codex: モデル名を既定名に安全に含め、異なるモデルの結果を区別する。
            // 260825Claude: サニタイズは既存の SpectrumPoolRepository.SanitizeFileName を使う (SpectrumPredictionExporter が同じ用途で呼んでいる共有実装)。
            PtsPixelSpectrum spectrum = last.Spectrum;
            string modelNameSuffix = string.IsNullOrWhiteSpace(last.ModelName)
                ? string.Empty
                : $"_{SpectrumPoolRepository.SanitizeFileName(last.ModelName)}";

            using var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                // 260716Claude: グラフタイトルと同じ視野情報（範囲・ビニング）を既定名に残す。
                // 260825Codex: 同一範囲を別モデルで分類した CSV の上書きを避ける。
                FileName = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetFileNameWithoutExtension(last.FileName)}_X{spectrum.BinLeft}-{spectrum.BinRight}_Y{spectrum.BinTop}-{spectrum.BinBottom}_bin{spectrum.RequestedBinSize}{modelNameSuffix}")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            // 260825Claude: 併設レポートは拡張子違いの同名なので、CSV 側を .csv に固定する。
            //   保存名を .txt で打たれると両者のパスが一致し、レポートが CSV を上書きしてしまう。
            // 260828Codex: 併設方式を廃止したため、標準ダイアログが確認した保存先へ一つの CSV だけを書き込む。

            try
            {
                SpectrumCsvExporter.WriteDisplayedSpectrum(dialog.FileName, last);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"エクスポートに失敗しました。\r\n{ex.Message}",
                    "スペクトルCSV出力", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // 260526Claude: マッピングの 2 ボタン状態を 1 か所で更新する（待機/実行中/中止中）。例外時も finally から呼んで UI を必ず戻す。
        // 260807Codex: ボタンに加えて未学習検知設定も管理するため、責務に合わせて改名する。
        private void UpdateMappingControls()
        {
            bool cancelling = _mapClassificationCancellation?.IsCancellationRequested ?? false;
            // 260527Codex: 共有予測サービスをクリック分類が使用中の間は、マップ作成を無効にする。
            buttonClassifyMap.Enabled = !IsInteractiveClassificationBusy;
            buttonCancelMap.Enabled = _isMappingBusy && !cancelling;
            buttonCancelMap.Text = cancelling ? "中止中..." : "中止";
            // 260807Codex: 進行中のクリック分類またはマップ作成中は分析条件を切り替えられないようにする。
            checkBoxunknown.Enabled = !IsInteractiveClassificationBusy;
            // 260828Codex: 読み取り・分類途中の文脈を保存しないよう、クリック処理中だけスペクトル出力を無効にする。
            exportSpectrumToolStripMenuItem.Enabled = !_isSpectrumClickBusy;
        }

        // 260526Claude: 進捗を statusStrip のラベルとバーへ反映する（Progress<double> は UI スレッドで生成済み）。
        private void ReportMappingProgress(double fraction)
        {
            int percent = Math.Clamp((int)(fraction * 100), 0, 100);
            string phase = fraction < 0.5 ? "読み取り中" : fraction < 0.95 ? "分類中" : "描画準備中";
            SetMappingStatus($"{phase} {percent}%", percent);
        }

        // 260526Claude: statusStrip の文言と進捗バーをまとめて設定する。
        private void SetMappingStatus(string text, int percent)
        {
            toolStripStatusLabelMapping.Text = text;
            toolStripProgressBarMapping.Value = Math.Clamp(percent, 0, 100);
        }

        // 260526Claude: 案2。表示インデックスの double[] と K 長パレットから PseudoBitmap を作る。MinValue=0/MaxValue=K で 1:1 対応、GrayScale=false で色付け。
        // 260528Claude: paletteOverride を渡せば Values/CategoryCount は据え置きで palette だけ差し替えた bitmap を作れる（凡例ハイライト用）。
        private static PseudoBitmap CreateMapPseudoBitmap(MineralMapImage image, (byte R, byte G, byte B)[]? paletteOverride = null)
        {
            return new PseudoBitmap(image.Values, image.Width, paletteOverride ?? image.Palette)
            {
                GrayScale = false,
                IsNegative = false,
                MinValue = 0,
                MaxValue = image.CategoryCount,
            };
        }

        // 260717Codex: Rebuild map pixels and legend from the same persisted palette after classification or a manual color edit.
        private void ApplyMineralMap(PtsClassificationMapResult result)
        {
            MineralMapImage image = MineralMapColorizer.Build(result, _mineralColorPalette);
            _classificationMap = result;
            _mapImage = image;
            SetMapPseudoBitmap(CreateMapPseudoBitmap(image));
            ShowMapLegend(image, result);
        }

        // 260526Claude: scalablePictureBox1 のマップ画像を差し替え、置き換え前の PseudoBitmap を破棄する。
        private void SetMapPseudoBitmap(PseudoBitmap? mapImage)
        {
            // 260716Claude: bitmap 差し替えはマップ viewport をリセットし DrawingAreaChanged を発火するため、
            //   リセット値が SEM 側へ逆流しないようガードして差し替える。
            _isSyncingViewports = true;
            try { ReplacePseudoBitmap(scalablePictureBoxMap, ref _mapPseudoBitmap, mapImage); }
            finally { _isSyncingViewports = false; }
            // 260604Codex: Map bitmap rebuilds reset its own viewport, so restore the SEM-aligned view immediately.
            SyncMapViewToSem(scalablePictureBoxSEM.Zoom, scalablePictureBoxSEM.Center);
            // 260716Claude: ReplacePseudoBitmap が消した黄枠も SEM 側から復元する（凡例ハイライトの rebuild 後や
            //   マップ新規作成後も、直前のクリック位置が両画像に出続ける）。
            SyncMapAreaToSem();
        }

        // 260604Codex: Designer-connected SEM viewport changes drive the mineral map viewport.
        private void scalablePictureBoxSEM_DrawingAreaChanged(object sender, double zoom, PointD center)
            => SyncMapViewToSem(zoom, center);

        // 260716Claude: Designer-connected map viewport changes drive the SEM viewport (SyncMapViewToSem の逆方向)。
        private void scalablePictureBoxMap_DrawingAreaChanged(object sender, double zoom, PointD center)
            => SyncSemViewToMap(zoom, center);

        // 260604Codex: Convert SEM image coordinates into mineral-map block coordinates for synchronized viewing.
        private void SyncMapViewToSem(double semZoom, PointD semCenter)
        {
            // 260716Claude: 相互同期化に伴い、同期中の再入は止める。
            if (_isSyncingViewports || _classificationMap is null || _semPseudoBitmap is null || _mapPseudoBitmap is null || semCenter.IsNaN)
                return;

            int binSize = _classificationMap.BinSize;
            if (binSize <= 0 || semZoom <= 0)
                return;

            var mapCenter = new PointD(
                (semCenter.X + 0.5) / binSize - 0.5,
                (semCenter.Y + 0.5) / binSize - 0.5);
            double mapZoom = semZoom * binSize;
            // 260710Codex: Upstream ScalablePictureBox now owns the zoom cap, so synchronized zoom is applied directly.
            _isSyncingViewports = true;
            try { scalablePictureBoxMap.ZoomAndCenter = (mapZoom, mapCenter); }
            finally { _isSyncingViewports = false; }
        }

        // 260716Claude: マップのブロック座標を SEM ピクセル座標へ逆変換し、SEM viewport を追従させる。
        private void SyncSemViewToMap(double mapZoom, PointD mapCenter)
        {
            if (_isSyncingViewports || _classificationMap is null || _semPseudoBitmap is null || _mapPseudoBitmap is null || mapCenter.IsNaN)
                return;

            int binSize = _classificationMap.BinSize;
            if (binSize <= 0 || mapZoom <= 0)
                return;

            var semCenter = new PointD(
                (mapCenter.X + 0.5) * binSize - 0.5,
                (mapCenter.Y + 0.5) * binSize - 0.5);
            double semZoom = mapZoom / binSize;
            _isSyncingViewports = true;
            try { scalablePictureBoxSEM.ZoomAndCenter = (semZoom, semCenter); }
            finally { _isSyncingViewports = false; }
        }

        // 260528Claude: 凡例選択状態に合わせてマップ palette を作り直す。Values と CategoryCount は変えず palette だけ差し替える。
        private void RebuildMapBitmapForSelection()
        {
            if (_mapImage is null) return;

            int highlightedIndex = _highlightLegendIndex;
            (byte R, byte G, byte B)[]? palette = highlightedIndex >= 0 && highlightedIndex < _mapImage.Palette.Length
                ? MineralMapColorizer.BuildHighlightedPalette(_mapImage.Palette, highlightedIndex)
                : null;

            SetMapPseudoBitmap(CreateMapPseudoBitmap(_mapImage, palette));
        }

        // 260526Claude: 完了時に上位20＋Other＋未判定の凡例を listBoxLegend へ反映する（色見本は owner-draw）。
        // 260527Codex: textBox1 is limited to map summary and timing diagnostics; the persistent legend stays in listBoxLegend.
        private void ShowMapLegend(MineralMapImage image, PtsClassificationMapResult result)
        {
            _highlightLegendIndex = -1;
            listBoxLegend.BeginUpdate();
            try
            {
                listBoxLegend.Items.Clear();
                foreach (var entry in image.Legend)
                    listBoxLegend.Items.Add(entry);
                // 260526Claude: owner-draw では ListBox が項目幅を測れないため、最長行を実測して HorizontalExtent に渡す。
                // 260607Codex: Size the owner-drawn legend from the same percentage text that will be rendered.
                listBoxLegend.HorizontalExtent = MeasureLegendMaxWidth(image.Legend, result.BlockCount);
            }
            finally
            {
                listBoxLegend.EndUpdate();
            }

            var lines = new List<string>
            {
                $"鉱物マッピング: {result.ModelName}",
                $"ビニング: {result.BinSize}×{result.BinSize} / 格子 {result.GridWidth}×{result.GridHeight}",
                // 260807Codex: 表示と配色に使った実適用条件をマップ要約へ示す。
                // 260827Claude: クリック結果欄・.txt と同じ 3 状態で出す。「なし」だけでは OFF 指定とモデル非対応を
                //   区別できず、同じ画面で語彙が割れていた。
                // 260828Codex: 未適用理由は警告へ分離し、要約には実適用の二状態だけを表示する。
                $"未学習検知: {MineralUnknownDetector.DescribeApplication(result.UnknownDetectionApplied)}",
            };

            // 260527Claude: 格子が表示枠より大きいと縮小描画でスカラー値が平均されカテゴリ色が混ざる（案2 の安全条件）。
            if (result.GridWidth > scalablePictureBoxMap.ClientSize.Width - 1 || result.GridHeight > scalablePictureBoxMap.ClientSize.Height - 1)
                lines.Add("⚠ 格子が表示枠より大きく、縮小表示でカテゴリ色が混ざる場合があります（binを大きくしてください）。");

            PtsClassificationMapTimings timings = result.Timings;
            lines.Add(string.Empty);
            lines.Add("Timing:");
            lines.Add($"  Total: {FormatDuration(timings.Total)}");
            lines.Add($"  Model prep: {FormatDuration(timings.ModelPreparation)}");
            lines.Add($"  Read/Aggregate: {FormatDuration(timings.ReadAndAggregate)}");
            lines.Add($"  Normalize/Pack: {FormatDuration(timings.NormalizeAndPack)}");
            lines.Add($"  Inference: {FormatDuration(timings.Inference)}");
            lines.Add($"  Tiles: {timings.TileCount}, Batch: {timings.BatchSize}, Tile memory: {timings.TileMemoryBudgetBytes / (1024 * 1024)} MB");

            textBox1.Lines = lines.ToArray();
        }

        // 260527Codex: Keep timing diagnostics compact enough for the map summary textbox.
        private static string FormatDuration(TimeSpan value)
            => value.TotalMilliseconds < 1000
                ? string.Format(CultureInfo.InvariantCulture, "{0:F0} ms", value.TotalMilliseconds)
                : string.Format(CultureInfo.InvariantCulture, "{0:F2} s", value.TotalSeconds);

        // 260526Claude: マップクリック。作成時の bin/model/file で該当ブロックを再読みし、SEM クリックと同じ表示・分類へ流す。
        private async Task DisplayMapPixelAsync(Point block)
        {
            PtsClassificationMapResult? map = _classificationMap;
            if (map is null)
                return;

            if ((uint)block.X >= (uint)map.GridWidth || (uint)block.Y >= (uint)map.GridHeight)
                return;

            string filePath = map.PtsFilePath;
            int binSize = map.BinSize;
            int readVersion = ++_spectrumReadVersion;

            PtsPixelSpectrum? spectrum;
            try
            {
                spectrum = await Task.Run(() =>
                {
                    using var pts = new PTSFile(filePath);
                    return pts.TryReadBinnedBlockSpectrum(block.X, block.Y, binSize, map.LeadingSweepCount);
                });
            }
            catch (Exception ex)
            {
                if (readVersion == _spectrumReadVersion)
                    MessageBox.Show($"ブロックのEDXスペクトルを読み取れませんでした。\r\n{ex.Message}", "鉱物マッピング", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (readVersion != _spectrumReadVersion || spectrum is null)
                return;

            // 260716Claude: spectrum の Bin 範囲はブロックの SEM ピクセル座標なので、SEM クリックと同じ経路で両画像に黄枠を出す。
            ShowBinningArea(spectrum);
            // 260807Codex: マップクリックの詳細分類は、そのマップで実際に適用した未学習検知条件を再現する。
            // 260825Codex: マップ作成時に適用した sweep 条件を自己記述 CSV に残す。
            var context = new DisplayedSpectrumExportContext(
                spectrum,
                Path.GetFileName(filePath),
                map.LeadingSweepCount,
                map.ModelName,
                UnknownDetectionRequested: map.UnknownDetectionRequested,
                DetectUnknownForPrediction: map.UnknownDetectionApplied,
                Classification: null);
            await DisplaySpectrumAndClassifyAsync(context, map.ModelPath, readVersion, block);
        }

        // 260612Codex: クリック分析は判定結果を先頭に置き、0.00% の候補は従来どおり隠す。
        // 260826Claude: 本文の書式は SpectrumPredictionBlockFormatter と意図的に別にしている。あちらのラベルは
        //   バッチ予測 CSV の 1 列目になり、勉強用/scripts/point_analysis/analyze_classification.py が「分類結果,」「Top-1 candidate,」
        //   で読んでいるため、揃えると過去の CSV と解析スクリプトの互換が切れる。画面側は一見して判定が分かる語彙を優先する。
        // 260828Codex: 用途別整理後の点分析スクリプトを参照する。
        // 260825Codex: モデル名と画面上で確認できる条件を除き、判定結果だけを組み立てる。
        // 260826Claude: 条件は消さず、スクロールしないと見えない末尾へまとめる。上端は結果だけが見え、
        //   保存物と同じ条件も同じ画面から辿れる。判定に使った実値をそのまま書くので、後でコンボを変えても真のまま。
        private static List<string> BuildClickAnalysisLines(
            MineralClassificationPredictionResult result,
            DisplayedSpectrumExportContext context,
            Point? mapBlock)
        {
            // 260727Claude: % 整形は SpectrumPredictionBlockFormatter を単一の基準にする (FormMain の結果欄・CSV と同じ丸め)。
            string probabilityText = SpectrumPredictionBlockFormatter.FormatPercent(result.Confidence);
            string positionText = mapBlock is { } block
                ? $"位置: マップ ({block.X}, {block.Y}) / SEM {context.DescribeRange()}"
                : $"位置: SEM {context.DescribeRange()}";

            // 260825Codex: 既知・Unknown とも結果、未学習検知、位置、上位候補の同じ構成で表示する。
            // 260830Codex: Unknown の判定と第1候補を分け、結果欄の先頭を読みやすくする。
            var lines = new List<string>();
            if (result.IsUnknown)
            {
                lines.Add($"判定: {result.DisplayMineralName}");
                lines.Add($"（第1候補: {result.PredictedMineral} {probabilityText}%）");
            }
            // 260830Codex: FormMain の「詳細確率」と語彙をそろえ、校正済みの信頼度と誤解される表現を避ける。
            else
                lines.Add($"判定: {result.DisplayMineralName}（確率 {probabilityText}%）");

            // 260825Codex: 距離としきい値がある場合は、既知判定でも未学習検知結果を表示する。
            // 260825Claude: 書式は SpectrumPredictionBlockFormatter.FormatScore に合わせる (バッチ予測の report と同じ刻み)。
            // 260828Codex: 画面では距離の厳密な数式名を使わず、不等号で判定関係を直接示す。
            // 260830Codex: 狭い結果欄でも語の途中で折り返さないよう、説明・距離・しきい値を意味単位で分ける。
            // 260830Codex: CSV の実値は変えず、画面だけ有効数字 4 桁へ丸めて読みやすくする。
            if (result.UnknownDetectionApplied &&
                result.UnknownScore is { } unknownScore &&
                result.UnknownThreshold is { } unknownThreshold)
            {
                string comparisonOperator = result.IsUnknown ? ">" : "≤";
                lines.Add("未学習検知:");
                lines.Add($"  学習分布からの距離 {FormatUnknownDetectionValueForDisplay(unknownScore)}");
                lines.Add($"  {comparisonOperator} しきい値 {FormatUnknownDetectionValueForDisplay(unknownThreshold)}");
            }
            else
                lines.Add("未学習検知: 未適用");

            lines.Add(positionText);
            lines.Add(string.Empty);
            lines.Add("上位候補:");

            int rank = 1;
            // 260830Codex: バッチ出力・自己記述 CSV と同じ丸め基準で、表示対象の候補だけを列挙する。
            foreach (var (probability, percentText) in SpectrumPredictionBlockFormatter.EnumerateVisibleProbabilities(result.Probabilities))
            {
                lines.Add($"{rank}. {probability.MineralName} {percentText}%");
                rank++;
            }

            lines.Add(string.Empty);
            lines.Add("条件:");
            lines.Add($"  PTS: {context.FileName}");
            lines.Add($"  モデル: {context.ModelName}");
            lines.Add($"  ビニング: {context.DescribeBinning()}");
            lines.Add($"  sweep: {context.DescribeSweep()}");
            lines.Add($"  未学習検知: {MineralUnknownDetector.DescribeApplication(result.UnknownDetectionApplied)}");

            return lines;
        }

        // 260830Codex: 結果欄では未学習検知の大小関係を概観できれば十分なので、保存値より短く表示する。
        private static string FormatUnknownDetectionValueForDisplay(double value)
            => value.ToString("G4", CultureInfo.InvariantCulture);

        // 260828Codex: 旧モデル互換は保ったまま、検知器欠落または読み込み失敗を黙って見逃さない。
        private void WarnUnknownDetectionUnavailable(string modelPath)
        {
            if (!_warnedUnknownDetectionModelPaths.Add(modelPath))
                return;

            // 260830Codex: 検知器ファイルだけでなく特徴抽出器の非対応でも利用不可になるため、原因を限定しない。
            MessageBox.Show(
                "このモデルでは未学習検知を利用できません。\r\n" +
                "未学習検知なしで処理しました。",
                "未学習検知",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        // 260526Claude: 凡例 (MeasureLegendMaxWidth と listBoxLegend_DrawItem) のレイアウト定数。両者がドリフトしないよう1か所に集約。
        private const int LegendPadding = 2;
        private const int LegendTextGap = 6;
        private const int LegendTrailingPadding = 4;

        // 260607Codex: Legend rows show each classified map category as a share of the whole map.
        private int MeasureLegendMaxWidth(IReadOnlyList<MineralMapLegendEntry> entries, int totalBlockCount)
        {
            int swatchSize = listBoxLegend.ItemHeight - LegendPadding * 2;
            int swatchAndGap = LegendPadding + swatchSize + LegendTextGap;
            int max = 0;
            foreach (var entry in entries)
            {
                Size textSize = TextRenderer.MeasureText(entry.FormatLabel(totalBlockCount), listBoxLegend.Font);
                // 260607Codex: Keep the width update local instead of carrying a pass-through variable.
                max = Math.Max(max, swatchAndGap + textSize.Width + LegendTrailingPadding);
            }
            return max;
        }

        // 260528Claude: 凡例で同じ項目を再クリックしたら選択解除。MouseDown は ListBox 既定の選択処理より先に発火し、
        // ここで SelectedIndex=-1 を直接代入しても直後の基底処理で再選択される。BeginInvoke で基底処理後に解除する。
        // 260528Codex: SelectedIndex can already be updated here, so the highlight state must not depend on it.
        private void listBoxLegend_MouseDown(object sender, MouseEventArgs e)
        {
            // 260717Codex: Right-click selects the target row for the Designer-owned color-edit context menu.
            if (e.Button == MouseButtons.Right)
            {
                int contextIndex = listBoxLegend.IndexFromPoint(e.Location);
                listBoxLegend.SelectedIndex = contextIndex;
                changeLegendColorToolStripMenuItem.Enabled =
                    contextIndex >= 0 &&
                    listBoxLegend.Items[contextIndex] is MineralMapLegendEntry { Assignment: not MineralColorAssignment.Fixed };
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            int idx = listBoxLegend.IndexFromPoint(e.Location);
            if (idx < 0)
                return;

            // 260528Codex: Toggle our own highlight state instead of racing ListBox native selection.
            _highlightLegendIndex = _highlightLegendIndex == idx ? -1 : idx;
            listBoxLegend.Invalidate();
            RebuildMapBitmapForSelection();
        }

        // 260528Claude: 凡例選択が変わったらマップのハイライト表示を作り直す。マウス・キーボード・プログラム更新の全経路がここに集約される。
        // 260528Codex: Native selection is visual noise only now; the MouseDown handler owns map highlight changes.
        private void listBoxLegend_SelectedIndexChanged(object sender, EventArgs e)
            => listBoxLegend.Invalidate();

        // 260717Codex: Apply a user-selected RGB color only to the chosen mineral and refresh the current map immediately.
        private void changeLegendColorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_classificationMap is null ||
                listBoxLegend.SelectedItem is not MineralMapLegendEntry entry ||
                entry.Assignment == MineralColorAssignment.Fixed)
                return;

            using var dialog = new ColorDialog
            {
                AnyColor = true,
                Color = entry.Color,
                FullOpen = true,
                SolidColorOnly = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            _mineralColorPalette.SetManualColor(entry.MineralName, dialog.Color);
            ApplyMineralMap(_classificationMap);
        }

        private void listBoxLegend_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            bool highlighted = e.Index == _highlightLegendIndex;
            Color backgroundColor = highlighted ? SystemColors.Highlight : listBoxLegend.BackColor;
            Color textColor = highlighted ? SystemColors.HighlightText : listBoxLegend.ForeColor;
            using (var backgroundBrush = new SolidBrush(backgroundColor))
                e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

            if (listBoxLegend.Items[e.Index] is MineralMapLegendEntry entry)
            {
                int swatchSize = e.Bounds.Height - LegendPadding * 2;
                var swatchRect = new Rectangle(e.Bounds.Left + LegendPadding, e.Bounds.Top + LegendPadding, swatchSize, swatchSize);
                using (var brush = new SolidBrush(entry.Color))
                    e.Graphics.FillRectangle(brush, swatchRect);
                using (var borderPen = new Pen(highlighted ? Color.Yellow : Color.Gray))
                    e.Graphics.DrawRectangle(borderPen, swatchRect);

                var textRect = new Rectangle(
                    swatchRect.Right + LegendTextGap,
                    e.Bounds.Top,
                    e.Bounds.Right - swatchRect.Right - LegendTextGap,
                    e.Bounds.Height);
                // 260607Codex: Draw mineral ratios instead of raw category block counts.
                TextRenderer.DrawText(
                    e.Graphics,
                    entry.FormatLabel(_classificationMap?.BlockCount ?? 0),
                    e.Font ?? listBoxLegend.Font,
                    textRect,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        // 260605Claude: コントラストスライダー操作時に SEM 表示へ反映する。
        private void trackBarContrast_Scroll(object sender, EventArgs e) => ApplySemBrightnessContrast();

        // 260605Claude: 明るさスライダー操作時に SEM 表示へ反映する。
        private void trackBarBrightness_Scroll(object sender, EventArgs e) => ApplySemBrightnessContrast();
    }
}
