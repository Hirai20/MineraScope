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

        // 260517Codex: PTS ドロップ時に読み込んだ全フレーム合算スペクトルキューブを保持します。
        private PtsSpectrumCube? _currentSpectrumCube;

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

        // 260518Codex: .pts ドロップ時に SEM画像と全フレーム合算スペクトルキューブを同時に読み込みます。
        private async void AnalyzerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetSingleDroppedPtsFile(e, out var filePath))
                return;

            ClearLoadedPtsData();
            UseWaitCursor = true;
            try
            {
                var (semImage, spectrumCube) = await Task.Run(() => LoadPtsSemImageAndSpectrumCube(filePath));

                if (semImage is null)
                {
                    MessageBox.Show(
                        "このPTSファイルからSEM画像を読み取れませんでした。",
                        "PTS SEM画像",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (spectrumCube is null)
                {
                    semImage.Dispose();
                    MessageBox.Show(
                        "このPTSファイルからEDXスペクトルキューブを読み取れませんでした。",
                        "PTS EDXスペクトル",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                _currentPtsFilePath = filePath;
                _currentSpectrumCube = spectrumCube;
                pictureBox1.Image = semImage;
                graphControl1.GraphTitle = Path.GetFileName(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"PTSファイルからSEM画像またはEDXスペクトルを読み取れませんでした。\r\n{ex.Message}",
                    "PTS SEM/EDX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        // 260517Codex: PTS 読み取り処理をバックグラウンド側でまとめ、UI スレッドには結果だけ返します。
        private static (Bitmap? SemImage, PtsSpectrumCube? SpectrumCube) LoadPtsSemImageAndSpectrumCube(string filePath)
        {
            using var pts = new PTSFile(filePath);
            Bitmap? semImage = pts.TryReadSemImageBitmap();
            if (semImage is null)
                return (null, null);

            try
            {
                return (semImage, pts.TryReadSpectrumCube());
            }
            catch
            {
                semImage.Dispose();
                throw;
            }
        }

        // 260517Codex: 新しい PTS を読み込む前に古い画像・キューブ・グラフ表示を破棄します。
        private void ClearLoadedPtsData()
        {
            _currentPtsFilePath = null;
            _currentSpectrumCube = null;
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

        // 260518Codex: Designer で接続済みのクリックイベントから PTS ピクセルのEDXスペクトルを表示します。
        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _currentSpectrumCube is not { } spectrumCube)
                return;

            if (!TryGetImagePixelFromZoomedPictureBox(e.Location, out int x, out int y))
                return;

            if (x >= spectrumCube.Width || y >= spectrumCube.Height)
                return;

            string fileName = string.IsNullOrWhiteSpace(_currentPtsFilePath)
                ? "PTS"
                : Path.GetFileName(_currentPtsFilePath);
            graphControl1.LabelX = "Energy";
            graphControl1.UnitX = "keV";
            graphControl1.LabelY = "Counts";
            graphControl1.UnitY = "";
            graphControl1.GraphTitle = $"{fileName} ({x}, {y})";
            graphControl1.Profile = CreateSpectrumProfile(spectrumCube, x, y);
            graphControl1.Refresh();
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

        // 260518Codex: クリックされた1ピクセルの全チャンネルカウントを GraphControl 用 Profile に変換します。
        private static Profile CreateSpectrumProfile(PtsSpectrumCube spectrumCube, int x, int y)
        {
            var points = new List<PointD>(spectrumCube.ChannelCount);
            for (int channel = 0; channel < spectrumCube.ChannelCount; channel++)
                points.Add(new PointD(spectrumCube.GetEnergy(channel), spectrumCube.GetCount(x, y, channel)));

            return new Profile(points);
        }
    }
}
