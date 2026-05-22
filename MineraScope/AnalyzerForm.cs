using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
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

        // 260519Codex: 後から完了した古いクリック読み取りが最新表示を上書きしないようにします。
        private int _spectrumReadVersion;

        // 260522Codex: One service instance keeps the loaded classification model warm across clicks.
        private readonly MineralClassificationPredictionService _classificationService = new();

        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            InitializeBinningOptions();
            InitializeMineralJudgeEvents();
            // 260516Codex: AnalyzerForm と子コントロール上の .pts ドロップでSEM画像を受け取れるようにします。
            InitializeSemImageDrop();
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
        // 260416Codex: 解析対象のスペクトルファイル一覧を UI からそのまま取得します。
        private List<string> SpectrumFiles =>
            listBoxSpectrumFiles.Items
                .OfType<string>()
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

        // 260416Codex: AnalyzerForm 内で使う解析 UI のイベントを一か所で登録します。
        private void InitializeMineralJudgeEvents()
        {
        }

        // 260416Codex: 解析ログの表示先をフォーム内の結果テキストボックスに統一します。
        private void AnalysisLog(string message)
            => TextBoxLogHelper.AppendLine(textBoxAnalysisResult, message);


        // 260416Codex: ドロップされたファイルやフォルダから解析対象スペクトルだけを取り込みます。
        private void listBoxSpectrumFiles_DragDrop(object? sender, DragEventArgs e)
        {
            var droppedPaths = e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [];
            var spectrumFiles = MineralPredictionWorkflow.CollectSpectrumFiles(droppedPaths);

            if (spectrumFiles.Length == 0)
                return;

            listBoxSpectrumFiles.Items.Clear();
            listBoxSpectrumFiles.Items.AddRange(spectrumFiles);
        }

        // 260416Codex: スペクトルファイルのドラッグ中だけコピー可能カーソルを表示します。
        private void listBoxSpectrumFiles_DragEnter(object? sender, DragEventArgs e)
            => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        // 260416Codex: 選択中のスペクトルファイルだけを逆順で安全に削除します。
        private void buttonRemoveSpectrumFiles_Click(object? sender, EventArgs e)
        {
            var selectedIndices = listBoxSpectrumFiles.SelectedIndices.Cast<int>().OrderDescending().ToArray();
            foreach (var index in selectedIndices)
                listBoxSpectrumFiles.Items.RemoveAt(index);
        }

        // 260416Codex: 解析中だけボタンを無効化してワークフローの実行に集中させます。
        private async void buttonAnalyze_Click(object? sender, EventArgs e)
        {
            buttonAnalyze.Enabled = false;
            textBoxAnalysisResult.Clear();

            try
            {
            }
            finally
            {
                buttonAnalyze.Enabled = true;
            }
        }

        private void groupBoxModelFolder_VisibleChanged(object sender, EventArgs e)
        {
            if (this.Visible)
            {

            }
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
                Bitmap? semImage = await Task.Run(() => LoadPtsSemImage(filePath));

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
                pictureBoxSEM.Image = semImage;
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
        private static Bitmap? LoadPtsSemImage(string filePath)
        {
            using var pts = new PTSFile(filePath);
            return pts.TryReadSemImageBitmap();
        }

        // 260519Codex: 新しい PTS を読み込む前に古い画像・キャッシュ・グラフ表示を破棄します。
        private void ClearLoadedPtsData()
        {
            _currentPtsFilePath = null;
            _pixelSpectrumCache.Clear();
            _spectrumReadVersion++;
            pictureBoxSEM.Image?.Dispose();
            pictureBoxSEM.Image = null;
            graphControl1.GraphTitle = "";
            graphControl1.ClearProfile();
        }

        // 260516Codex: 単一の .pts ファイルだけをSEM画像表示用のドロップ対象として受け付けます。
        private void AnalyzerForm_DragEnter(object? sender, DragEventArgs e)
            => e.Effect = TryGetSingleDroppedPtsFile(e, out _)
                ? DragDropEffects.Copy
                : DragDropEffects.None;

        // 260516Codex: Designerを触らず、既存フォーム領域全体で .pts ドロップを受けられるようにします。
        private void InitializeSemImageDrop()
        {
            AllowDrop = true;
            pictureBoxSEM.SizeMode = PictureBoxSizeMode.Zoom;
            EnableSemImageDropOnChildControls(this);
        }

        // 260516Codex: 子コントロール上でのドロップも AnalyzerForm のSEM画像表示処理へ集約します。
        private void EnableSemImageDropOnChildControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.AllowDrop = true;
                control.DragEnter += AnalyzerForm_DragEnter;
                control.DragDrop += AnalyzerForm_DragDrop;
                EnableSemImageDropOnChildControls(control);
            }
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

        // 260522Codex: Designer click handling now reads and displays the selected binned PTS spectrum.
        private async void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(_currentPtsFilePath))
                return;

            if (!TryGetImagePixelFromZoomedPictureBox(e.Location, out int x, out int y))
                return;

            string filePath = _currentPtsFilePath;
            Point pixel = new(x, y);
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
            graphControl1.GraphTitle = $"{fileName} ({x}, {y}) {pixelSpectrum.RequestedBinSize}×{pixelSpectrum.RequestedBinSize}";
            graphControl1.Profile = CreateSpectrumProfile(pixelSpectrum);
            graphControl1.Refresh();

            await ClassifySelectedPixelAsync(pixelSpectrum, readVersion);
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

        // 260518Codex: PictureBox の Zoom 表示余白を除外し、クリック位置を実画像ピクセルへ戻します。
        private bool TryGetImagePixelFromZoomedPictureBox(Point point, out int imageX, out int imageY)
        {
            imageX = 0;
            imageY = 0;

            if (pictureBoxSEM.Image is not { } image)
                return false;

            Rectangle imageBounds = GetZoomedImageBounds(pictureBoxSEM);
            if (imageBounds.Width <= 0 || imageBounds.Height <= 0 || !imageBounds.Contains(point))
                return false;

            double scaleX = image.Width / (double)imageBounds.Width;
            double scaleY = image.Height / (double)imageBounds.Height;
            imageX = Math.Clamp((int)((point.X - imageBounds.Left) * scaleX), 0, image.Width - 1);
            imageY = Math.Clamp((int)((point.Y - imageBounds.Top) * scaleY), 0, image.Height - 1);
            return true;
        }

        // 260518Codex: SizeMode.Zoom で実際に画像が描かれる矩形を PictureBox 内に再現します。
        private static Rectangle GetZoomedImageBounds(PictureBox pictureBox)
        {
            if (pictureBox.Image is not { } image)
                return Rectangle.Empty;

            Rectangle client = pictureBox.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0)
                return Rectangle.Empty;

            double imageRatio = image.Width / (double)image.Height;
            double boxRatio = client.Width / (double)client.Height;
            if (boxRatio > imageRatio)
            {
                int height = client.Height;
                int width = Math.Max(1, (int)Math.Round(height * imageRatio));
                int left = client.Left + (client.Width - width) / 2;
                return new Rectangle(left, client.Top, width, height);
            }

            int zoomedWidth = client.Width;
            int zoomedHeight = Math.Max(1, (int)Math.Round(zoomedWidth / imageRatio));
            int top = client.Top + (client.Height - zoomedHeight) / 2;
            return new Rectangle(client.Left, top, zoomedWidth, zoomedHeight);
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
