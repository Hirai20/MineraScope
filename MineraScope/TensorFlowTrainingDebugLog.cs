namespace MineraScope
{
    // 260605Claude: 推論側ロガーと同形式で学習側のステージ時間・GC・スレッド・メモリを即 flush で記録する。ボトルネック特定のための一次データを残す。
    // 260606Claude: 実体は共有 DiagnosticLog に集約。ここは学習用ファイル名だけを持つ薄いファサード。
    internal static class TensorFlowTrainingDebugLog
    {
        private static readonly DiagnosticLog Log = new("tf-train-debug.log");

        public static void Write(string eventName, string details = "") => Log.Write(eventName, details);

        public static string Clean(string value) => DiagnosticLog.Clean(value);
    }
}
