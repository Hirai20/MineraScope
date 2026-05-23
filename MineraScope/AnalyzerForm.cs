using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
// 260517Codex: graphControl1 に渡すスペクトル点列を Profile/PointD として組み立てます。
using Crystallography;

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
        private readonly Dictionary<(Point Pixel, int BinSize), PtsPixelSpectrum> _pixelSpectrumCache = [];

        // 260522Codex: Keep the first binning candidates small enough for experiments while covering low-count spectra.
        private static readonly int[] BinningSizes = [3, 5, 7, 10, 20];

        // 260522Codex: Default to 10x10 as the first low-count classification baseline.
        private const int DefaultBinningSize = 10;

        // 260523Codex: Treat tiny mouse movement as a click so ScalablePictureBox drag panning stays separate.
        private const int ScalableSemClickMoveTolerance = 4;

        // 260523Codex: Keep ownership of the image assigned to scalablePictureBoxSEM so replaced SEM images can be disposed.
        private PseudoBitmap? _semPseudoBitmap;

        // 260523Codex: Remember the ScalablePictureBox left-button start point until MouseUp2 decides click vs pan.
        private Point? _scalableSemMouseDownPoint;

        // 260519Codex: 後から完了した古いクリック読み取りが最新表示を上書きしないようにします。
        private int _spectrumReadVersion;

        // 260522Codex: One service instance keeps the loaded classification model warm across clicks.
        private readonly MineralClassificationPredictionService _classificationService = new();

        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            InitializeBinningOptions();
            // 260523Claude: .pts ドロップを復活。フォーム本体は Designer で DragEnter/DragDrop 済みなので AllowDrop だけ立て、
            // 子孫側は再帰で有効化する（ScalablePictureBox は内部 pictureBox がドロップ先になり、Designer では再帰配線できないため）。
            AllowDrop = true;
            ControlDropHelper.EnableRecursive(this, AnalyzerForm_DragEnter, AnalyzerForm_DragDrop);
        }

        // 260522Codex: Store binning choices as typed combo items instead of parsing display text later.
        private sealed record BinningOption(int Size)
        {
            public override string ToString() => $"{Size}×{Size}";
        }

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
            this.Visible = false;
        }

        // 260519Codex: .pts ドロップ時は SEM画像だけを読み込み、EDXスペクトルはクリック時に1ピクセルだけ読みます。
        private async void AnalyzerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetSingleDroppedPtsFile(e, out var filePath))
                return;

            ClearLoadedPtsData();
            UseWaitCursor = true;
            try
            {
                // 260523Codex: Load the real SEM image into the scalable viewer's native PseudoBitmap format.
                PseudoBitmap? semImage = await Task.Run(() => LoadPtsSemImage(filePath));

                if (semImage is null)
                {
                    MessageBox.Show(
                        "このPTSファイルからSEM画像を読み取れませんでした。",
                        "PTS SEM画像",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                _currentPtsFilePath = filePath;
                SetSemPseudoBitmap(semImage);
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
            }
        }

        // 260519Codex: PTS の SEM画像だけをバックグラウンド側で読み込みます。
        // 260523Codex: Read the PTS SEM byte image and flatten it into ScalablePictureBox's row-major source data.
        private static PseudoBitmap? LoadPtsSemImage(string filePath)
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

            return new PseudoBitmap(values, width);
        }

        // 260519Codex: 新しい PTS を読み込む前に古い画像・キャッシュ・グラフ表示を破棄します。
        private void ClearLoadedPtsData()
        {
            _currentPtsFilePath = null;
            _pixelSpectrumCache.Clear();
            _spectrumReadVersion++;
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
            PseudoBitmap? previousImage = _semPseudoBitmap;
            _semPseudoBitmap = semImage ?? new PseudoBitmap([0d], 1);
            scalablePictureBoxSEM.ShowAreaRectangle = false;
            scalablePictureBoxSEM.PseudoBitmap = _semPseudoBitmap;
            previousImage?.Dispose();
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
            _scalableSemMouseDownPoint = e.Button == MouseButtons.Left && e.Clicks == 1
                ? e.Location
                : null;
            return false;
        }

        // 260523Codex: Designer-connected ScalablePictureBox MouseUp2 reads the clicked image pixel without blocking pan/zoom behavior.
        private bool scalablePictureBoxSEM_MouseUp2(object sender, MouseEventArgs e, PointD pt)
        {
            if (e.Button == MouseButtons.Left &&
                IsScalableSemClick(e.Location) &&
                TryGetImagePixelFromScalablePictureBox(pt, out int x, out int y))
            {
                _ = ReadAndDisplayBinnedPixelAsync(new Point(x, y));
            }

            _scalableSemMouseDownPoint = null;
            return false;
        }

        // 260523Codex: Keep ScalablePictureBox click handling tolerant of tiny hand movement but not drag panning.
        private bool IsScalableSemClick(Point mouseUpPoint)
        {
            if (_scalableSemMouseDownPoint is not { } mouseDownPoint)
                return false;

            return Math.Abs(mouseUpPoint.X - mouseDownPoint.X) <= ScalableSemClickMoveTolerance &&
                Math.Abs(mouseUpPoint.Y - mouseDownPoint.Y) <= ScalableSemClickMoveTolerance;
        }

        // 260523Codex: Convert ScalablePictureBox source coordinates into a clamped SEM pixel index.
        private bool TryGetImagePixelFromScalablePictureBox(PointD sourcePoint, out int imageX, out int imageY)
        {
            imageX = 0;
            imageY = 0;

            if (_semPseudoBitmap is null || _semPseudoBitmap.Width <= 1 || _semPseudoBitmap.Height <= 1)
                return false;

            imageX = Math.Clamp((int)Math.Floor(sourcePoint.X), 0, _semPseudoBitmap.Width - 1);
            imageY = Math.Clamp((int)Math.Floor(sourcePoint.Y), 0, _semPseudoBitmap.Height - 1);
            return true;
        }

        // 260523Claude: SEM クリック位置のビニング済みスペクトルを読み込み、グラフ表示と分類まで行う。
        private async Task ReadAndDisplayBinnedPixelAsync(Point pixel)
        {
            if (string.IsNullOrWhiteSpace(_currentPtsFilePath))
                return;

            string filePath = _currentPtsFilePath;
            int binSize = GetSelectedBinningSize();
            int readVersion = ++_spectrumReadVersion;
            PtsPixelSpectrum? pixelSpectrum = await GetPixelSpectrumAsync(filePath, pixel, binSize);
            if (readVersion != _spectrumReadVersion || pixelSpectrum is null)
                return;

            string fileName = Path.GetFileName(filePath);
            graphControl1.LabelX = "Energy";
            graphControl1.UnitX = "keV";
            graphControl1.LabelY = "Counts";
            graphControl1.UnitY = "";
            graphControl1.GraphTitle = $"{fileName} ({pixel.X}, {pixel.Y}) {pixelSpectrum.RequestedBinSize}×{pixelSpectrum.RequestedBinSize}";
            graphControl1.Profile = CreateSpectrumProfile(pixelSpectrum);
            graphControl1.Refresh();
            ShowBinningArea(pixelSpectrum);

            await ClassifySelectedPixelAsync(pixelSpectrum, readVersion);
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

        // 260522Codex: Runs the selected mapping model against the normalized binned PTS spectrum.
        private async Task ClassifySelectedPixelAsync(PtsPixelSpectrum pixelSpectrum, int readVersion)
        {
            string selectedModelName = comboBoxMappingModellFolder.SelectedItem as string ?? string.Empty;
            string modelParentPath = _modelCatalog?.ParentPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedModelName) || string.IsNullOrWhiteSpace(modelParentPath))
            {
                textBox1.Text = "モデルフォルダが選択されていません。";
                return;
            }

            float[]? normalizedSpectrum = SpectrumDataLoader.CreateNormalizedSpectrum(pixelSpectrum);
            if (normalizedSpectrum is null)
            {
                textBox1.Text =
                    $"選択ピクセルのスペクトル長は {pixelSpectrum.ChannelCount} 点です。\r\n" +
                    $"分類モデルは {SpectrumDataLoader.SpectrumLength} 点の入力に対応しています。";
                return;
            }

            string selectedModelPath = Path.Combine(modelParentPath, selectedModelName);
            string binningLabel = $"{pixelSpectrum.RequestedBinSize}×{pixelSpectrum.RequestedBinSize}";
            textBox1.Text = $"分類中... ピクセル ({pixelSpectrum.X}, {pixelSpectrum.Y}) / ビニング {binningLabel}";
            UseWaitCursor = true;

            try
            {
                var result = await Task.Run(() =>
                    _classificationService.Predict(selectedModelPath, normalizedSpectrum));

                if (readVersion != _spectrumReadVersion)
                    return;

                var lines = new List<string>
                {
                    $"モデル: {selectedModelName}",
                    $"ピクセル: ({pixelSpectrum.X}, {pixelSpectrum.Y})",
                    $"ビニング: {binningLabel}",
                    $"加算範囲: X {pixelSpectrum.BinLeft}-{pixelSpectrum.BinRight}, Y {pixelSpectrum.BinTop}-{pixelSpectrum.BinBottom}",
                    $"加算ピクセル数: {pixelSpectrum.BinnedPixelCount}",
                    $"予測鉱物: {result.PredictedMineral} ({result.Confidence * 100:F2}%)",
                    "",
                    "分類確率:"
                };

                foreach (var probability in result.Probabilities)
                    lines.Add($"  {probability.MineralName}: {probability.Confidence * 100:F2}%");

                textBox1.Lines = lines.ToArray();
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
        private async Task<PtsPixelSpectrum?> GetPixelSpectrumAsync(string filePath, Point pixel, int binSize)
        {
            var cacheKey = (Pixel: pixel, BinSize: binSize);
            if (_pixelSpectrumCache.TryGetValue(cacheKey, out var cachedSpectrum))
                return cachedSpectrum;

            UseWaitCursor = true;
            try
            {
                PtsPixelSpectrum? spectrum = await Task.Run(() =>
                {
                    using var pts = new PTSFile(filePath);
                    return pts.TryReadBinnedPixelSpectrum(pixel.X, pixel.Y, binSize);
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
    }
}
