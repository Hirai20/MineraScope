using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
// 260517Codex: graphControl1 に渡すスペクトル点列を Profile/PointD として組み立てます。
using Crystallography;

namespace MineraScope
{
    public partial class AnalyzerForm : Form
    {
        public FormMain FormMain;

        // 260517Codex: 現在表示中の PTS ファイルをクリック後のグラフタイトルへ反映します。
        private string? _currentPtsFilePath;

        // 260519Codex: 読み取り済みピクセルスペクトルを座標ごとに保持し、同じ点の再クリックを速くします。
        private readonly Dictionary<Point, PtsPixelSpectrum> _pixelSpectrumCache = [];

        // 260519Codex: 後から完了した古いクリック読み取りが最新表示を上書きしないようにします。
        private int _spectrumReadVersion;

        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            InitializeMineralJudgeEvents();
            // 260516Codex: AnalyzerForm と子コントロール上の .pts ドロップでSEM画像を受け取れるようにします。
            InitializeSemImageDrop();
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
            var str = FormMain.ModelPath;
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
                pictureBox1.Image = semImage;
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
            pictureBox1.Image?.Dispose();
            pictureBox1.Image = null;
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
            pictureBox1.SizeMode = PictureBoxSizeMode.Zoom;
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

        // 260520Codex: Designer で接続済みのクリックイベントから、現在のPTSファイル上の1ピクセルスペクトルを表示します。
        private async void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || string.IsNullOrWhiteSpace(_currentPtsFilePath))
                return;

            if (!TryGetImagePixelFromZoomedPictureBox(e.Location, out int x, out int y))
                return;

            string filePath = _currentPtsFilePath;
            Point pixel = new(x, y);
            int readVersion = ++_spectrumReadVersion;
            PtsPixelSpectrum? pixelSpectrum = await GetPixelSpectrumAsync(filePath, pixel);
            if (readVersion != _spectrumReadVersion || pixelSpectrum is null)
                return;

            string fileName = Path.GetFileName(filePath);
            graphControl1.LabelX = "Energy";
            graphControl1.UnitX = "keV";
            graphControl1.LabelY = "Counts";
            graphControl1.UnitY = "";
            graphControl1.GraphTitle = $"{fileName} ({x}, {y})";
            graphControl1.Profile = CreateSpectrumProfile(pixelSpectrum);
            graphControl1.Refresh();
        }

        // 260520Codex: 未読のピクセルだけ PTS stream を走査し、読み取り済みならキャッシュから返します。
        private async Task<PtsPixelSpectrum?> GetPixelSpectrumAsync(string filePath, Point pixel)
        {
            if (_pixelSpectrumCache.TryGetValue(pixel, out var cachedSpectrum))
                return cachedSpectrum;

            UseWaitCursor = true;
            try
            {
                PtsPixelSpectrum? spectrum = await Task.Run(() =>
                {
                    using var pts = new PTSFile(filePath);
                    return pts.TryReadPixelSpectrum(pixel.X, pixel.Y);
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

                _pixelSpectrumCache[pixel] = spectrum;
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

            if (pictureBox1.Image is not { } image)
                return false;

            Rectangle imageBounds = GetZoomedImageBounds(pictureBox1);
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
