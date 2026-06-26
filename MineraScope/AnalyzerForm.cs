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

        // 260522Codex: Keep the first binning candidates small enough for experiments while covering low-count spectra.
        private static readonly int[] BinningSizes = [3, 5, 7, 10, 20];

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

        // 260526Claude: 鉱物マッピングの状態。result は Top-1 のみ軽量保持し、クリック再分類は作成時条件で再読みする。
        private PtsClassificationMapResult? _classificationMap;
        private PseudoBitmap? _mapPseudoBitmap;
        private bool _isMappingBusy;
        private int _mapBuildVersion;
        private CancellationTokenSource? _mapClassificationCancellation;

        // 260528Claude: 凡例ハイライト用に colorizer 出力を保持。palette だけ差し替えて bitmap を作り直すための源データ。
        private MineralMapImage? _mapImage;

        // 260528Codex: Keep legend highlighting independent from ListBox.SelectedIndex timing.
        private int _highlightLegendIndex = -1;

        // 260527Codex: Name the shared prediction-service gate used by click and full-map classification.
        private bool IsInteractiveClassificationBusy => _isMappingBusy || _isSpectrumClickBusy;

        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            InitializeBinningOptions();
            InitializeSweepOptions(0);
            // 260526Claude: 待機状態（マッピング有効・中止無効）を初期化する。
            UpdateMappingButtons();
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

            ModelComboBinder.Populate(comboBoxMappingModellFolder, _modelCatalog.ModelNames, preferredModelName);
        }

        private void AnalyzerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Visible = false;
        }

        // 260519Codex: .pts ドロップ時は SEM画像だけを読み込み、EDXスペクトルはクリック時に1ピクセルだけ読みます。
        private async void AnalyzerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (_isPtsDropLoading)
                return;

            if (!TryGetSingleDroppedPtsFile(e, out var filePath))
                return;

            _isPtsDropLoading = true;
            ClearLoadedPtsData();
            UseWaitCursor = true;
            try
            {
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
                UseWaitCursor = false;
                _isPtsDropLoading = false;
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
            InitializeSweepOptions(0);
            _spectrumReadVersion++;
            _mapBuildVersion++;
            _mapClassificationCancellation?.Cancel();
            _classificationMap = null;
            // 260528Claude: Items.Clear が SelectedIndexChanged を発火して RebuildMapBitmapForSelection が走るので、源データを先に無効化する。
            _mapImage = null;
            _highlightLegendIndex = -1;
            // 260526Claude: 凡例も合わせて破棄（PTS が変わったら色対応も無効）。
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

        // 260527Codex: Keep image clicks idle during map builds so TensorFlow prediction and UI updates do not interleave.
        private async Task RunSpectrumClickAsync(Func<Task> action)
        {
            if (IsInteractiveClassificationBusy)
                return;

            _isSpectrumClickBusy = true;
            UpdateMappingButtons();
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
                UpdateMappingButtons();
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
            await DisplaySpectrumAndClassifyAsync(pixelSpectrum, modelPath, modelName, Path.GetFileName(filePath), readVersion, null);
        }

        // 260523Codex: Draw the actual clamped binning rectangle on the SEM image after a pixel is selected.
        private void ShowBinningArea(PtsPixelSpectrum pixelSpectrum)
        {
            scalablePictureBoxSEM.AreaRectangle = new RectangleD(
                pixelSpectrum.BinLeft,
                pixelSpectrum.BinTop,
                pixelSpectrum.BinRight - pixelSpectrum.BinLeft + 1,
                pixelSpectrum.BinBottom - pixelSpectrum.BinTop + 1);
            scalablePictureBoxSEM.ShowAreaRectangle = true;
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
        // 260612Codex: Map clicks pass their block coordinate so the result text can show a result-first location summary.
        private async Task DisplaySpectrumAndClassifyAsync(PtsPixelSpectrum spectrum, string modelPath, string modelName, string fileName, int readVersion, Point? mapBlock)
        {
            string binningLabel = $"{spectrum.RequestedBinSize}×{spectrum.RequestedBinSize}";
            string rangeLabel = $"X {spectrum.BinLeft}-{spectrum.BinRight}, Y {spectrum.BinTop}-{spectrum.BinBottom}";

            graphControl1.LabelX = "Energy";
            graphControl1.UnitX = "keV";
            graphControl1.LabelY = "Counts";
            graphControl1.UnitY = "";
            graphControl1.GraphTitle = $"{fileName} [{rangeLabel}] {binningLabel}";
            graphControl1.Profile = CreateSpectrumProfile(spectrum);
            graphControl1.Refresh();

            if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(modelName))
            {
                textBox1.Text = "モデルフォルダが選択されていません。";
                return;
            }

            float[]? normalizedSpectrum = SpectrumDataLoader.CreateNormalizedSpectrum(spectrum);
            if (normalizedSpectrum is null)
            {
                textBox1.Text =
                    $"選択範囲のスペクトル長は {spectrum.ChannelCount} 点です。\r\n" +
                    $"分類モデルは {SpectrumDataLoader.SpectrumLength} 点の入力に対応しています。";
                return;
            }

            textBox1.Text = $"分類中... 範囲 [{rangeLabel}] / ビニング {binningLabel}";
            UseWaitCursor = true;

            try
            {
                // 260606Claude: Task.Run はプールの別スレッドに乗り TF ワーカーを増殖させるため、専用スレッドへ集約する PredictAsync を直接 await する。
                var result = await _classificationService.PredictAsync(modelPath, normalizedSpectrum);

                if (readVersion != _spectrumReadVersion)
                    return;

                textBox1.Lines = BuildClickAnalysisLines(result, spectrum, modelName, mapBlock).ToArray();
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

        // 260519Codex: クリックされた1ピクセルの全チャンネルカウントを GraphControl 用 Profile に変換します。
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
            int buildVersion = ++_mapBuildVersion;
            using var cancellation = new CancellationTokenSource();
            _mapClassificationCancellation = cancellation;
            _isMappingBusy = true;
            UpdateMappingButtons();
            var progress = new Progress<double>(ReportMappingProgress);

            try
            {
                PtsClassificationMapResult result = await Task.Run(() => PtsClassificationMapWorkflow.Run(
                    filePath, binSize, modelPath, modelName, leadingSweepCount, _classificationService, progress, cancellation.Token));

                // 260526Claude: 計算中に PTS や条件が変わっていたら反映しない。
                if (buildVersion != _mapBuildVersion || !string.Equals(_currentPtsFilePath, filePath, StringComparison.Ordinal))
                    return;

                MineralMapImage image = MineralMapColorizer.Build(result);
                _classificationMap = result;
                // 260528Claude: 凡例選択時の palette 差し替えに使う源データ。ShowMapLegend の Items.Clear で rebuild が走るので Set より前に必須。
                _mapImage = image;
                SetMapPseudoBitmap(CreateMapPseudoBitmap(image));
                ShowMapLegend(image, result);
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
                UpdateMappingButtons();
            }
        }

        // 260526Claude: 中止ボタンは取り消し要求だけ行い、状態表示を更新する。
        private void buttonCancelMap_Click(object sender, EventArgs e)
        {
            _mapClassificationCancellation?.Cancel();
            UpdateMappingButtons();
        }

        // 260623Claude: 表示中の鉱物マップ視野を、外部 BSE と同寸法の予測画像群 (8bitラベル/RGB/classes.csv/metadata) として出力する。
        // ラベルは RGB の逆算ではなくモデルの top1 クラスID配列を直接 uint8 化し、グリッドを外部 BSE 寸法へ最近傍スケールして同視野・同座標に揃える。
        // ROI を引く BSE はユーザーの外部電子像 (JEOL View0xx IMG1.bmp 等) を使う前提なので、アプリは BSE を出力しない。
        private async void exportMapToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IsInteractiveClassificationBusy || _isMappingBusy)
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

        // 260526Claude: マッピングの 2 ボタン状態を 1 か所で更新する（待機/実行中/中止中）。例外時も finally から呼んで UI を必ず戻す。
        private void UpdateMappingButtons()
        {
            bool cancelling = _mapClassificationCancellation?.IsCancellationRequested ?? false;
            // 260527Codex: Disable map creation while a click classification is still using the shared prediction service.
            buttonClassifyMap.Enabled = !IsInteractiveClassificationBusy;
            buttonCancelMap.Enabled = _isMappingBusy && !cancelling;
            buttonCancelMap.Text = cancelling ? "中止中..." : "中止";
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

        // 260526Claude: scalablePictureBox1 のマップ画像を差し替え、置き換え前の PseudoBitmap を破棄する。
        private void SetMapPseudoBitmap(PseudoBitmap? mapImage)
        {
            ReplacePseudoBitmap(scalablePictureBoxMap, ref _mapPseudoBitmap, mapImage);
            // 260604Codex: Map bitmap rebuilds reset its own viewport, so restore the SEM-aligned view immediately.
            SyncMapViewToSem(scalablePictureBoxSEM.Zoom, scalablePictureBoxSEM.Center);
        }

        // 260604Codex: Designer-connected SEM viewport changes drive the mineral map viewport.
        private void scalablePictureBoxSEM_DrawingAreaChanged(object sender, double zoom, PointD center)
            => SyncMapViewToSem(zoom, center);

        // 260604Codex: Convert SEM image coordinates into mineral-map block coordinates for synchronized viewing.
        private void SyncMapViewToSem(double semZoom, PointD semCenter)
        {
            if (_classificationMap is null || _semPseudoBitmap is null || _mapPseudoBitmap is null || semCenter.IsNaN)
                return;

            int binSize = _classificationMap.BinSize;
            if (binSize <= 0 || semZoom <= 0)
                return;

            var mapCenter = new PointD(
                (semCenter.X + 0.5) / binSize - 0.5,
                (semCenter.Y + 0.5) / binSize - 0.5);
            double mapZoom = semZoom * binSize;
            // 260604Codex: SEM-aligned map zoom can exceed the viewer's default cap when binning is large.
            scalablePictureBoxMap.MaxZoom = Math.Max(scalablePictureBoxMap.MaxZoom, mapZoom);
            scalablePictureBoxMap.ZoomAndCenter = (mapZoom, mapCenter);
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

            ShowMapBlockArea(block);
            await DisplaySpectrumAndClassifyAsync(spectrum, map.ModelPath, map.ModelName, Path.GetFileName(filePath), readVersion, block);
        }

        // 260612Codex: Keep the clicked-point analysis focused on the answer first, with 0.00% candidates hidden as before.
        private static List<string> BuildClickAnalysisLines(
            MineralClassificationPredictionResult result,
            PtsPixelSpectrum spectrum,
            string modelName,
            Point? mapBlock)
        {
            string confidenceText = FormatPercent(result.Confidence);
            string positionText = mapBlock is { } block
                ? $"位置: マップ ({block.X}, {block.Y}) / SEM X {spectrum.BinLeft}-{spectrum.BinRight}, Y {spectrum.BinTop}-{spectrum.BinBottom}"
                : $"位置: SEM X {spectrum.BinLeft}-{spectrum.BinRight}, Y {spectrum.BinTop}-{spectrum.BinBottom}";

            var lines = new List<string>
            {
                "クリック地点の分析結果",
                "",
                $"判定鉱物: {result.DisplayMineralName}",
                $"信頼度: {confidenceText}%",
                positionText,
                $"ビニング: {spectrum.RequestedBinSize} x {spectrum.RequestedBinSize}",
                "",
                "上位候補:",
            };

            if (result.IsUnknown)
            {
                // 260622Codex: Click details show the closed-set candidate and open-set distance for unknown pixels.
                lines.Add($"Open-set: {MineralUnknownDetector.UnknownDisplayName}");
                lines.Add($"Top-1 candidate: {result.PredictedMineral}");
                if (!string.IsNullOrWhiteSpace(result.NearestKnownMineral))
                    lines.Add($"Nearest known: {result.NearestKnownMineral}");
                if (result.UnknownScore.HasValue && result.UnknownThreshold.HasValue)
                    lines.Add($"Unknown score: {result.UnknownScore.Value:G6} / {result.UnknownThreshold.Value:G6}");
                lines.Add("");
            }

            int rank = 1;
            foreach (var probability in result.Probabilities)
            {
                string percentText = FormatPercent(probability.Confidence);
                if (percentText == "0.00")
                    continue;

                lines.Add($"{rank}. {probability.MineralName} {percentText}%");
                rank++;
            }

            lines.Add("");
            lines.Add($"使用モデル: {modelName}");
            return lines;
        }

        // 260612Codex: Keep confidence formatting identical for the headline and candidate rows.
        private static string FormatPercent(float confidence)
            => (confidence * 100).ToString("F2", CultureInfo.InvariantCulture);

        // 260526Claude: クリックしたブロックをマップ側 (scalablePictureBox1) に枠表示する（案2 なので格子座標で 1×1）。
        private void ShowMapBlockArea(Point block)
        {
            // 260527Codex: Center the 1x1 selection on the displayed map pixel instead of starting at its center.
            scalablePictureBoxMap.AreaRectangle = new RectangleD(block.X - 0.5, block.Y - 0.5, 1, 1);
            scalablePictureBoxMap.ShowAreaRectangle = true;
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
                Size textSize = TextRenderer.MeasureText(FormatLegendText(entry, totalBlockCount), listBoxLegend.Font);
                // 260607Codex: Keep the width update local instead of carrying a pass-through variable.
                max = Math.Max(max, swatchAndGap + textSize.Width + LegendTrailingPadding);
            }
            return max;
        }

        // 260607Codex: Keep map legend percentages consistent between measuring and drawing.
        private static string FormatLegendText(MineralMapLegendEntry entry, int totalBlockCount)
        {
            double percent = totalBlockCount > 0
                ? entry.BlockCount * 100.0 / totalBlockCount
                : 0;
            return string.Format(CultureInfo.InvariantCulture, "{0}: {1:F2}%", entry.MineralName, percent);
        }

        // 260528Claude: 凡例で同じ項目を再クリックしたら選択解除。MouseDown は ListBox 既定の選択処理より先に発火し、
        // ここで SelectedIndex=-1 を直接代入しても直後の基底処理で再選択される。BeginInvoke で基底処理後に解除する。
        // 260528Codex: SelectedIndex can already be updated here, so the highlight state must not depend on it.
        private void listBoxLegend_MouseDown(object sender, MouseEventArgs e)
        {
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
                    FormatLegendText(entry, _classificationMap?.BlockCount ?? 0),
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
