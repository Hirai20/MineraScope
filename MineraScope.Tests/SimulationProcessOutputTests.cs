namespace MineraScope.Tests
{
    // 261107Codex: Javaや利用者のファイルを使わず、40本の出力待ちと中止後の回収を検証する。
    internal static class SimulationProcessOutputTests
    {
        public static void Run()
        {
            using var started = new CountdownEvent(40);
            using var release = new ManualResetEventSlim();
            int threadPoolReaders = 0;
            var readers = Enumerable.Range(0, 40).Select(index =>
                DtsaSimulationProcessRunner.ReadRedirectedOutputAsync(() =>
                {
                    if (Thread.CurrentThread.IsThreadPoolThread)
                        Interlocked.Increment(ref threadPoolReaders);
                    started.Signal();
                    release.Wait();
                    return $"stdout/stderr {index}\r\n";
                })).ToArray();
            try
            {
                TestAssert.True(started.Wait(TimeSpan.FromSeconds(10)), "All twenty jobs' output readers must start without waiting for output.");
                TestAssert.Equal(0, threadPoolReaders, "Silent redirected pipes must not occupy ThreadPool workers needed for cancellation and watchdogs.");
                Task.Run(release.Set).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            }
            finally
            {
                release.Set();
                Task.WhenAll(readers).GetAwaiter().GetResult();
            }
            TestAssert.SequenceEqual(Enumerable.Range(0, 40).Select(index => $"stdout/stderr {index}\r\n"),
                readers.Select(reader => reader.Result), "Cleanup must collect every reader's complete output without changing line endings.");
            TestAssert.Throws<IOException>(() => DtsaSimulationProcessRunner.ReadRedirectedOutputAsync(
                () => throw new IOException("closed output")).GetAwaiter().GetResult(),
                "Reader failures must propagate to the existing output cleanup boundary.");
        }
    }
}
