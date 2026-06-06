using System.Collections.Concurrent;
using System.Threading;

namespace MineraScope
{
    // 260606Claude: TF.NET の eager 実行は初回推論した OS スレッドごとにネイティブワーカープール(CPU 数ぶん)を作って解放しない。
    // Task.Run で毎回別スレッドに乗るとスレッド/メモリが増殖し最終的に Apply() がハングするため、全推論を専用 1 スレッドへ集約する。
    internal static class TensorFlowExecutor
    {
        private static readonly BlockingCollection<Action> Queue = new();
        private static readonly Thread Worker = StartWorker();

        private static Thread StartWorker()
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "TensorFlowExecutor",
            };
            thread.Start();
            return thread;
        }

        // 260606Claude: 専用スレッドの一生。キューが空ならブロックして待ち、来た仕事を 1 件ずつこのスレッド上で実行する。
        private static void WorkerLoop()
        {
            TensorFlowPredictionDebugLog.Write("executor-worker-start", $"nativeThread={TensorFlowPredictionDebugLog.CurrentNativeThreadId}");
            foreach (Action job in Queue.GetConsumingEnumerable())
                job();
        }

        // 260606Claude: 専用スレッド上で同期実行し、結果が出るまで呼び出し元をブロックする(マップ workflow のチャンクや RunPrediction のバッチ用)。
        // RunAsync に委譲して再入処理・例外伝播を 1 箇所へ集約する(worker からの再入は RunAsync 側で inline 実行され、完了済み Task の GetResult は即座に返るのでデッドロックしない)。
        public static T Run<T>(Func<T> func) => RunAsync(func).GetAwaiter().GetResult();

        // 260606Claude: 専用スレッド上で実行し、UI スレッドを塞がず await できるようにする(単発分類用)。
        public static Task<T> RunAsync<T>(Func<T> func)
        {
            // 260606Claude: worker 自身からの再入はキュー投入すると自己待ちで永久停止するため、その場で実行して完了済み Task を返す。
            if (Thread.CurrentThread == Worker)
            {
                try { return Task.FromResult(func()); }
                catch (Exception ex) { return Task.FromException<T>(ex); }
            }

            // 260606Claude: RunContinuationsAsynchronously で await の継続が worker スレッド上で同期再開しないようにする。
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
