namespace MineraScope
{
    internal static class TensorFlowRuntimeGate
    {
        // 260527Codex: Keras/TensorFlow.NET keeps process-wide runtime state, so model load/predict/session reset must not overlap.
        public static readonly object SyncRoot = new();

        // 260527Codex: TensorFlow.NET/NumSharp values are not all statically IDisposable here, so release them opportunistically.
        // 260727Claude: 分類・回帰の両サービスが同じ解放を行うため、private 複製をやめてここへ集約する。
        public static void DisposeIfPossible(object? value)
        {
            try
            {
                if (value is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
            }
        }
    }
}
