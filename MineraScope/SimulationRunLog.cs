using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace MineraScope
{
    // 260717Claude: DTSA-II シミュレーション 1 run (RunAsync 1回) ごとの永続ログ。GUI 終了後も失敗原因を追跡できるよう、
    //   attempt 要約・watchdog 判定・失敗時の stdout/stderr 全文を Documents\MineraScope\Logs\simulate-*.log へ残す。
    //   DiagnosticLog と同じ流儀 (専用フォルダ・lock 直列化・書き込み失敗は握り潰してシミュレーションを止めない) だが、
    //   あちらの TF 診断列と改行潰しは stdout 証跡に不向きなため、行構造を保つ専用実装とする。
    internal sealed class SimulationRunLog
    {
        private readonly object _gate = new();
        private readonly string _logPath;

        private SimulationRunLog(string fileName)
        {
            FileName = fileName;
            // 260717Codex: Reuse the centralized application log folder.
            _logPath = Path.Combine(DefaultStoragePaths.LogsFolder, fileName);
        }

        // 260717Claude: ファイル名 = run ID。manifest の FailureReason からもこの名前で辿れるようにする。
        public string FileName { get; }

        // 260717Claude: 同一秒に複数 run が始まってもファイルが衝突しないよう ms まで含める。
        public static SimulationRunLog CreateForRun() =>
            new($"simulate-{DateTime.Now.ToString("yyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");

        // 260717Claude: 経過時間はログ集計しやすい秒表記へ統一する (呼び出し側の補間文字列を culture 非依存に保つ)。
        public static string FormatSeconds(TimeSpan value) =>
            value.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + "s";

        // 260717Claude: watchdog メッセージや例外メッセージを 1 行ログへ埋め込むときの改行潰し。
        public static string Flatten(string value) =>
            value.Replace('\r', ' ').Replace('\n', ' ');

        // 260717Claude: 時刻付き 1 行ログ。attempt 要約・watchdog 判定・run 開始/終了に使う。
        public void WriteLine(string message) =>
            Append(string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}"));

        // 260717Claude: stdout/stderr 全文をヘッダ付きブロックで残す。1 回の Append で書くため、並行ジョブの
        //   ブロック同士が行単位で混ざることはない (lock で直列化)。
        public void WriteBlock(string header, string content)
        {
            var builder = new StringBuilder();
            builder.Append("--- ").Append(header).Append(" ---").Append(Environment.NewLine);
            builder.Append(string.IsNullOrEmpty(content) ? "(empty)" : content.TrimEnd('\r', '\n'));
            builder.Append(Environment.NewLine);
            builder.Append("--- end ---").Append(Environment.NewLine);
            Append(builder.ToString());
        }

        private void Append(string text)
        {
            try
            {
                // 260717Codex: Create the centralized log folder before the synchronized append.
                Directory.CreateDirectory(DefaultStoragePaths.LogsFolder);
                lock (_gate)
                    File.AppendAllText(_logPath, text, Encoding.UTF8);
            }
            catch
            {
                // 260717Claude: ログ書き込み失敗でシミュレーションを止めない (要件)。
            }
        }
    }
}
