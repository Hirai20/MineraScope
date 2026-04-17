using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MineraScope
{
    public partial class AnalyzerForm : Form
    {
        // 260416Codex: AnalyzerForm の UI 配線をフォーム本体にまとめて保守しやすくします。
        public AnalyzerForm()
        {
            InitializeComponent();
            InitializeMineralJudgeEvents();
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
            buttonAnalyze.Click += buttonAnalyze_Click;
            buttonSelectModelFolder.Click += buttonSelectModelFolder_Click;
            buttonRemoveSpectrumFiles.Click += buttonRemoveSpectrumFiles_Click;
            listBoxSpectrumFiles.DragDrop += listBoxSpectrumFiles_DragDrop;
            listBoxSpectrumFiles.DragEnter += listBoxSpectrumFiles_DragEnter;
        }

        // 260416Codex: 解析ログの表示先をフォーム内の結果テキストボックスに統一します。
        private void AnalysisLog(string message)
            => TextBoxLogHelper.AppendLine(textBoxAnalysisResult, message);

        // 260416Codex: モデルフォルダの選択は共通 helper に委譲して UI 側を薄く保ちます。
        private void buttonSelectModelFolder_Click(object? sender, EventArgs e)
            => FolderSelectionHelper.TrySelectFolder(textBoxModelFolder);

        // 260416Codex: ドロップされたファイルやフォルダから解析対象スペクトルだけを取り込みます。
        private void listBoxSpectrumFiles_DragDrop(object? sender, DragEventArgs e)
        {
            var droppedPaths = e.Data?.GetData(DataFormats.FileDrop) as string[] ?? [];
            var spectrumFiles = MineralPredictionWorkflow.CollectSpectrumFiles(droppedPaths);

            if (spectrumFiles.Length == 0)
            {
                return;
            }

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
            {
                listBoxSpectrumFiles.Items.RemoveAt(index);
            }
        }

        // 260416Codex: 解析中だけボタンを無効化してワークフローの実行に集中させます。
        private async void buttonAnalyze_Click(object? sender, EventArgs e)
        {
            buttonAnalyze.Enabled = false;
            textBoxAnalysisResult.Clear();

            try
            {
                await new MineralPredictionWorkflow(AppContext.BaseDirectory, AnalysisLog)
                    .RunAsync(textBoxModelFolder.Text, SpectrumFiles);
            }
            finally
            {
                buttonAnalyze.Enabled = true;
            }
        }

    }
}
