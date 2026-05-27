namespace MineraScope
{
    internal static class TensorFlowRuntimeGate
    {
        // 260527Codex: Keras/TensorFlow.NET keeps process-wide runtime state, so model load/predict/session reset must not overlap.
        public static readonly object SyncRoot = new();
    }
}
