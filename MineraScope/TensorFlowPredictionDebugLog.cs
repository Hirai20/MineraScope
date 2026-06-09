namespace MineraScope
{
    // 260604Codex: Writes crash-resistant TensorFlow diagnostics so native aborts still leave a usable breadcrumb trail.
    // 260606Claude: 実体は共有 DiagnosticLog に集約。ここは推論用ファイル名と map-run 採番だけを持つ薄いファサード。
    internal static class TensorFlowPredictionDebugLog
    {
        private static readonly DiagnosticLog Log = new("tf-predict-debug.log");
        private static long _nextMapRunId;

        public static uint CurrentNativeThreadId => DiagnosticLog.CurrentNativeThreadId;

        public static long NextMapRunId() => Interlocked.Increment(ref _nextMapRunId);

        public static void Write(string eventName, string details = "") => Log.Write(eventName, details);

        public static string Clean(string value) => DiagnosticLog.Clean(value);
    }
}
