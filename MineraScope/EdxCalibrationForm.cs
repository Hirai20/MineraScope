using System.Diagnostics;
using System.Globalization;
using System.Text;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.Optimization;

namespace MineraScope
{
    public partial class EdxCalibrationForm : Form
    {
        // 260724Claude: EmsaConverter Form1.cs の較正挙動を移植。ロジックは EdxCalibration クラスへ、挙動はこのフォームへ。
        private List<double> SourceValues = [];
        private List<double> ResultValues = [];
        private List<double> SimulatedValues = [];

        private int PointNum { get => (int)numericUpDownPointNum.Value; set => numericUpDownPointNum.Value = value; }

        private int Order { get => (int)numericUpDownOrder.Value; set => numericUpDownOrder.Value = value; }

        private double Offset { get => (double)numericUpDownOffset.Value; set => numericUpDownOffset.Value = Clamp(numericUpDownOffset, value); }

        private double XperChannel { get => (double)numericUpDownXperchan.Value; set => numericUpDownXperchan.Value = Clamp(numericUpDownXperchan, value); }

        //260723Claude NumericUpDown は範囲外の値を代入すると例外を投げるので、ファイルから読んだ較正値は表示範囲に収めてから代入する
        private static decimal Clamp(NumericUpDown control, double value)
            => Math.Clamp((decimal)value, control.Minimum, control.Maximum);

        //260723Claude ファイル読み込み中は ValueChanged による再計算を抑止する (Offset と X/Channel を代入するたびに計算し直さない)
        private bool loading = false;

        private readonly Stopwatch sw = new();

        // 260727Claude: 較正グリッドは 2048ch × 10 eV/ch。長さとエネルギー幅は既存の単一基準
        //   (SpectrumDataLoader.SpectrumLength / EdsSpectrumReader.EnergyPerChannelEv) から作り、
        //   この画面だけ別の定数を持たないようにする。値は従来と同一。
        // 260803Codex: offset を含む軸の式も SpectrumAxis.EnergyAt へ一本化し、既定値は変えません。
        private static readonly double[] CalibrationEnergyGrid = [.. Enumerable
            .Range(0, SpectrumAxis.Default.ChannelCount)
            .Select(SpectrumAxis.Default.EnergyAt)];

        // 260727Claude: 表示・コピーする数値はファイルへ貼り戻せるため、必ず InvariantCulture で整形する
        //   (カンマ小数ロケールでは "1,234567" が CSV の 2 列に化けて読み戻しを壊す)。
        private static string FormatCount(double value) => value.ToString("f6", CultureInfo.InvariantCulture);

        public EdxCalibrationForm()
        {
            InitializeComponent();
            // 260514Codex: FormClosing の接続は Designer 側に寄せ、ここでは初期化だけを行います。
            //260723Claude DragDrop は Designer で接続済みなので、ここでは AllowDrop と DragEnter だけを設定する
            textBoxSource.AllowDrop = true; // textBoxSourceにドラッグアンドドロップを許可
            textBoxSource.DragEnter += textBoxSource_DragEnter;
            textBoxSimulated.AllowDrop = true; // textBoxSimulatedにドラッグアンドドロップを許可
            textBoxSimulated.DragEnter += textBoxSimulated_DragEnter;
        }

        // 260508Codex: EDXキャリブレーション画面は再表示時に入力値を保つため Dispose せず隠します。
        private void EdxCalibrationForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Visible = false;
        }

        private void textBoxSource_DragEnter(object sender, DragEventArgs e)
       => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

        private void textBoxSource_DragDrop(object sender, DragEventArgs e)
        {
            if (ReadDroppedSpectrum(e) is Spectrum spectrum)
                AnalyzeEmsa(spectrum);
        }

        private void textBoxSimulated_DragEnter(object sender, DragEventArgs e)
     => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;

        private void textBoxSimulated_DragDrop(object sender, DragEventArgs e)
        {
            if (ReadDroppedSpectrum(e) is Spectrum spectrum)
                AnalyzeSimEmsa(spectrum);
        }

        //260723Claude ドロップされた単一ファイルを読み込む (テキストは BOM付きUTF-8にも対応)
        // 260726Claude: 他フォーム (FormMain / SpectrumDataLoader) と同じく、バイナリ .eds は専用リーダーで 2048ch カウント列を取る。
        // .eds はエネルギー軸の較正値を持たない (10 eV/ch は公称値) ので、1列 .msa と同じく Offset/XperChannel は null にして現在の入力値を保つ。
        private Spectrum? ReadDroppedSpectrum(DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop, false) is not string[] { Length: 1 } fileNames)
                return null;

            string filePath = fileNames[0];
            if (!EdsSpectrumReader.IsEdsFile(filePath))
                return EdxCalibration.ReadSpectrum(File.ReadAllLines(filePath, Encoding.UTF8));

            int[]? counts = EdsSpectrumReader.TryReadCounts(filePath);
            if (counts is null)
            {
                toolStripStatusLabel1.Text = ".eds スペクトルを読み込めませんでした。";
                return null;
            }

            return new Spectrum([.. counts.Select(count => (double)count)], null, null);
        }

        private void AnalyzeEmsa(Spectrum spectrum)
        {
            // 260726Claude: 新しいスペクトルを読み込むと前回の最適化結果 (offset/X-channel) は対象外になるので結果欄をクリアする。
            ClearOptimizationResult();

            //260723Claude 読み込んだ後に表示する (以前は更新前の値を表示していた)
            SourceValues = spectrum.Counts;

            var sb = new StringBuilder(SourceValues.Count * 10);
            foreach (var v in SourceValues)
                sb.AppendLine(FormatCount(v));

            textBoxSource.Text = sb.ToString();

            //260723Claude エネルギー列を持つファイル ("Energy (keV),Counts" 形式のCSVなど) は、そのエネルギー軸を Offset と X/Channel に反映する
            if (spectrum.Offset is double offset && spectrum.XperChannel is double xPerChannel)
            {
                loading = true;
                Offset = offset;
                XperChannel = xPerChannel;
                loading = false;
            }

            //260723Claude ドロップした時点で Rwp まで計算・表示する
            UpdateDisplay(ConvertProfile(Offset, XperChannel, PointNum, Order, SourceValues));
        }

        private void AnalyzeSimEmsa(Spectrum spectrum)
        {
            // 260726Claude: シミュを差し替えても前回の最適化結果は対象外になるので結果欄をクリアする。
            ClearOptimizationResult();

            SimulatedValues = spectrum.Counts;

            var sb1 = new StringBuilder(SimulatedValues.Count * 10);
            foreach (var v in SimulatedValues)
                sb1.AppendLine(FormatCount(v));

            textBoxSimulated.Text = sb1.ToString();

            //260723Claude 参照側を後からドロップしたときも Rwp を更新する
            if (SourceValues.Count > 0)
                UpdateDisplay(ConvertProfile(Offset, XperChannel, PointNum, Order, SourceValues));
        }

        // 260726Claude: offset/X-channel の結果欄は buttonOptimize でしか書かないため、新規ドロップ時にここで空にして古い最適化結果を残さない。
        private void ClearOptimizationResult()
        {
            textBoxOffset.Clear();
            textBoxenergy.Clear();
        }

        private (double X, double Y)[] srcSpectrum = [];
        private List<double> ConvertProfile(double offset, double xPerChan, int pointNum, int order, List<double> srcValues)
        {
            SourceValues = srcValues;

            //260723Claude チャンネル数の異なるファイルを読み込んだときに前回のデータが残らないようにする
            if (srcSpectrum.Length != SourceValues.Count)
                srcSpectrum = new (double X, double Y)[SourceValues.Count];

            for (int i = 0; i < SourceValues.Count; i++)
                srcSpectrum[i] = (offset + xPerChan * i, SourceValues[i]);

            // 260727Claude: 共有の較正グリッドを使う (旧: 同じ配列を lazy field と UpdateDisplay のローカルで二重に作っていた)。
            var yArray = EdxCalibration.GetValues(srcSpectrum, CalibrationEnergyGrid, pointNum, order);

            return yArray.ToList();
        }

        private void UpdateDisplay(List<double> resultValues)
        {
            ResultValues = resultValues;

            // 260727Claude: エネルギー軸は共有グリッドを使う (毎回の再生成をやめ、ConvertProfile と同じ配列に揃える)。
            var sb = new StringBuilder();
            for (int i = 0; i < resultValues.Count; i++)
                sb.AppendLine($"{CalibrationEnergyGrid[i].ToString(CultureInfo.InvariantCulture)}\t{FormatCount(resultValues[i])}");

            textBoxResult.Text = sb.ToString();

            var sb1 = new StringBuilder();
            SimulatedValues = [.. SimulatedValues.Take(CalibrationEnergyGrid.Length)];

            for (int i = 0; i < SimulatedValues.Count; i++)
                sb1.AppendLine($"{CalibrationEnergyGrid[i].ToString(CultureInfo.InvariantCulture)}\t{FormatCount(SimulatedValues[i])}");

            textBoxSimulated.Text = sb1.ToString();

            // Rwpを計算して表示
            if (ResultValues.Count == SimulatedValues.Count)
            {
                var rwp = EdxCalibration.CalculationRwp(ResultValues, SimulatedValues);
                textBoxRwp.Text = rwp.ToString("f4", CultureInfo.InvariantCulture);
            }
            else
            {
                textBoxRwp.Text = "Rwp: -";
            }
        }

        private void numericUpDownOffset_ValueChanged(object sender, EventArgs e)
        {
            if (loading) return;

            var profile = ConvertProfile(Offset, XperChannel, PointNum, Order, SourceValues);
            UpdateDisplay(profile);
        }

        private void buttonCopy_Click(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ResultValues.Count; i++)
                sb.AppendLine(FormatCount(ResultValues[i]));

            Clipboard.SetDataObject(sb.ToString());
        }

        // 260724Claude: 原本は同期実行。MineraScope 規約 (UI スレッドを塞がない) に合わせ、最適化計算だけ Task.Run に逃がす。計算内容・結果は原本と同一。
        private async void buttonOptimize_Click(object sender, EventArgs e)
        {
            // 260803Claude: 旧コメント「チャンネル数は一致していなくてよい」(260723Claude) は誤りだったので撤回する。
            //   Rwp は index 対 index で比べるため、sim が較正グリッドと同じ点数でないと全評価が NaN になる。
            // 260803Codex: 最適化不能な入力は理由をステータスへ表示し、NelderMead を開始しない。
            if (SourceValues.Count == 0)
            {
                toolStripStatusLabel1.Text = "実測スペクトルが読み込まれていません。";
                return;
            }

            if (SimulatedValues.Count == 0)
            {
                toolStripStatusLabel1.Text = "シミュレーションスペクトルが読み込まれていません。";
                return;
            }

            if (SourceValues.Count < 2)
            {
                toolStripStatusLabel1.Text = "実測スペクトルは2点以上必要です。";
                return;
            }

            if (SimulatedValues.Count != CalibrationEnergyGrid.Length)
            {
                toolStripStatusLabel1.Text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"点数が一致しません（較正グリッド: {CalibrationEnergyGrid.Length}点、シミュレーション: {SimulatedValues.Count}点）。");
                return;
            }

            if (SourceValues.All(value => value <= 0))
            {
                toolStripStatusLabel1.Text = "実測スペクトルに正のカウントがありません。";
                return;
            }

            if (SimulatedValues.All(value => value == 0))
            {
                toolStripStatusLabel1.Text = "シミュレーションスペクトルが全て0です。";
                return;
            }

            sw.Restart();

            var pointNum = PointNum;
            var order = Order;

            //ObjectiveFunctionクラスで、Rwp値を計算するラムダ式を定義
            var objectiveFunc = ObjectiveFunction.Value(
               (Vector<double> x) =>
               {
                   var offset = x[0];
                   var energy = x[1];

                   // 260803Codex: NelderMead は非拘束なので入力欄の下限に関係なく 0 以下の eV/channel も試す。
                   // energy == 0 は GetValues の c1 = 1.0 / (pt[^1].X - pt[0].X) でゼロ除算になり、負値は X が降順となって区間探索の前提を崩す。
                   if (!double.IsFinite(offset) || !double.IsFinite(energy) || energy <= 0)
                       return EdxCalibration.UnusableAxisScore;

                   var profile = ConvertProfile(offset, energy, pointNum, order, SourceValues);

                   var rwp = EdxCalibration.CalculationRwp(profile, SimulatedValues);

                   // 260803Codex: 有効な軸でも極端な offset の外挿で数値が壊れ得るため、比較不能な値を探索へ返さない。
                   return double.IsFinite(rwp) ? rwp : EdxCalibration.UnusableAxisScore;
               });

            //260723Claude 現在の Offset / X per channel を初期値にする (エネルギー列つきCSVでは読み込んだ較正値から探索を始める)
            var initialGuess = new DenseVector([Offset, XperChannel]);

            // 260724Claude: 原本は同期実行で最適化中は UI が固まり操作不可だった。async 化で UI スレッドを塞がない代わりに、実行中は操作系コントロールを無効化してその状態を再現する。
            SetControlsEnabledDuringOptimization(false);
            try
            {
                var result = await Task.Run(() =>
                    new NelderMeadSimplex(1e-6, 10000).FindMinimum(objectiveFunc, initialGuess));

                var offset = result.FunctionInfoAtMinimum.Point[0];
                var energy = result.FunctionInfoAtMinimum.Point[1];
                var profile = ConvertProfile(offset, energy, pointNum, order, SourceValues);

                //RwpはUpdateDisplayの中で計算・表示する
                textBoxOffset.Text = offset.ToString("f4", CultureInfo.InvariantCulture);
                textBoxenergy.Text = energy.ToString("f4", CultureInfo.InvariantCulture);

                UpdateDisplay(profile);

                toolStripStatusLabel1.Text = $"{(sw.ElapsedMilliseconds / 1000.0).ToString("f2", CultureInfo.InvariantCulture)} sec. elapsed";
            }
            finally
            {
                SetControlsEnabledDuringOptimization(true);
            }
        }

        // 260724Claude: 最適化の実行中/完了で操作系コントロールをまとめて開閉する。
        // buttonOptimize=二重実行防止、NumericUpDown/ドロップ=バックグラウンドの ConvertProfile と共有フィールド (SourceValues/srcSpectrum/xArray) が競合するのを防止、buttonCopy=実行中は操作不可という原本の状態を再現。
        private void SetControlsEnabledDuringOptimization(bool enabled)
        {
            buttonOptimize.Enabled = enabled;
            numericUpDownOffset.Enabled = enabled;
            numericUpDownXperchan.Enabled = enabled;
            numericUpDownPointNum.Enabled = enabled;
            numericUpDownOrder.Enabled = enabled;
            textBoxSource.AllowDrop = enabled;
            textBoxSimulated.AllowDrop = enabled;
            buttonCopy.Enabled = enabled;
        }
    }
}
