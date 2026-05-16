using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MineraScope
{
    public partial class AnalyzerForm : Form
    {
        public FormMain FormMain;

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

        // 260516Codex: .pts ファイルがドロップされたら PTTD stream 内のSEM画像を読み取って PictureBox に表示します。
        private async void AnalyzerForm_DragDrop(object? sender, DragEventArgs e)
        {
            if (!TryGetSingleDroppedPtsFile(e, out var filePath))
                return;

            UseWaitCursor = true;
            try
            {
                Bitmap? semImage = await Task.Run(() =>
                {
                    using var pts = new PTSFile(filePath);
                    return pts.TryReadSemImageBitmap();
                });

                if (semImage is null)
                {
                    MessageBox.Show(
                        "このPTSファイルからSEM画像を読み取れませんでした。",
                        "PTS SEM画像",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                pictureBox1.Image?.Dispose();
                pictureBox1.Image = semImage;
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
    }
}
