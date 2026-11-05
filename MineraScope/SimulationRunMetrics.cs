// 261106Codex: 検証済みのバッチ横断実行・計測を公開版へ移す。
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace MineraScope
{
    // 261103Codex: このrunが起動したJavaだけのCPU時間と稼働数を集計し、他アプリの負荷と区別する。
    internal sealed class SimulationRunMetrics : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly HashSet<TrackedProcess> _active = [];
        private readonly SimulationRunLog _log;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly CancellationTokenSource _samplingCancellation = new();
        private readonly Task _samplingTask;
        private readonly int _processorCount = Environment.ProcessorCount;
        private double _finishedCpuSeconds;
        private double _lastCpuSeconds;
        private double _lastElapsedSeconds;
        private int _processStarts;
        private int _maxActive;

        // 261103Codex: 計測は起動間隔を含むrun全体を対象とし、5秒ごとの値と最終平均を同じログへ残す。
        public SimulationRunMetrics(SimulationRunLog log)
        {
            _log = log;
            _log.WriteLine($"resources start: logicalProcessors={_processorCount}, sampleIntervalSeconds=5, cpuScope=dtsa-processes");
            _samplingTask = SampleAsync();
        }

        // 261103Codex: 登録の寿命をProcessの内側に置き、Dispose前に最後のCPU時間を取り込む。
        public IDisposable Track(Process process)
        {
            var tracked = new TrackedProcess(this, process);
            lock (_gate)
            {
                _active.Add(tracked);
                _processStarts++;
                _maxActive = Math.Max(_maxActive, _active.Count);
            }
            return tracked;
        }

        // 261103Codex: 中止や保存失敗でもサンプラーを停止して平均と起動回数を確定する。
        public async ValueTask DisposeAsync()
        {
            _samplingCancellation.Cancel();
            await _samplingTask.ConfigureAwait(false);
            WriteSample(isFinal: true);
            _samplingCancellation.Dispose();
        }

        // 261103Codex: 外部プロセスを待つ処理とは独立した非同期サンプラー。
        private async Task SampleAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _samplingCancellation.Token).ConfigureAwait(false);
                    WriteSample(isFinal: false);
                }
            }
            catch (OperationCanceledException) when (_samplingCancellation.IsCancellationRequested)
            {
            }
        }

        // 261103Codex: 取得できない瞬間は直近値を使い、ログ計測の失敗で生成を止めない。
        private static double ReadCpuSeconds(Process process, double previous)
        {
            try
            {
                return Math.Max(previous, process.TotalProcessorTime.TotalSeconds);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return previous;
            }
        }

        // 261103Codex: 実行中と終了済みのCPU時間を重複なく合算し、論理CPU数で0～100%へ正規化する。
        private void WriteSample(bool isFinal)
        {
            lock (_gate)
            {
                foreach (var tracked in _active)
                    tracked.CpuSeconds = ReadCpuSeconds(tracked.Process, tracked.CpuSeconds);
                // 261104Codex: CPU時間・経過秒・使用率の単位を変数名で区別する。
                double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                double cpuSeconds = _finishedCpuSeconds + _active.Sum(tracked => tracked.CpuSeconds);
                double intervalSeconds = elapsedSeconds - _lastElapsedSeconds;
                double cpuPercent = intervalSeconds > 0 ? 100 * (cpuSeconds - _lastCpuSeconds) / (intervalSeconds * _processorCount) : 0;
                double averageCpuPercent = elapsedSeconds > 0 ? 100 * cpuSeconds / (elapsedSeconds * _processorCount) : 0;
                _log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"resources{(isFinal ? " end" : "")}: active={_active.Count} maxActive={_maxActive} processStarts={_processStarts} " +
                    $"cpuPercent={cpuPercent:F2} averageCpuPercent={averageCpuPercent:F2} cpuSeconds={cpuSeconds:F3} elapsed={elapsedSeconds:F3}s"));
                _lastCpuSeconds = cpuSeconds;
                _lastElapsedSeconds = elapsedSeconds;
            }
        }

        // 261103Codex: サンプラーとProcess破棄の競合を同じロックで避ける。
        private void Untrack(TrackedProcess tracked)
        {
            lock (_gate)
                if (_active.Remove(tracked))
                    _finishedCpuSeconds += ReadCpuSeconds(tracked.Process, tracked.CpuSeconds);
        }

        // 261103Codex: 同じ登録を二度解放してもCPU時間を二重計上しない。
        // 261105Claude: 登録そのものを集合の要素にし、PID のキーと値の組を別に持たない。二度目の解放は Remove が false を返す。
        private sealed class TrackedProcess(SimulationRunMetrics owner, Process process) : IDisposable
        {
            public Process Process { get; } = process;
            public double CpuSeconds { get; set; }

            // 261104Codex: 登録解除と最終CPU時間の取り込みを1回だけ行う。
            public void Dispose() => owner.Untrack(this);
        }
    }
}
