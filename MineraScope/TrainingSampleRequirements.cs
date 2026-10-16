using System;

namespace MineraScope
{
    // 260930Codex: 事前件数の検査と実学習の分割で、同じ切捨て方式を使う。
    // 261016Codex: 公開版でも分類の学習バッチと検証件数を開始前に検査する。
    internal static class TrainingSampleRequirements
    {
        public static (int Training, int Validation) GetSplitCounts(int sampleCount, float validationSplit)
        {
            if (sampleCount < 0)
                throw new ArgumentOutOfRangeException(nameof(sampleCount));
            if (!float.IsFinite(validationSplit) || validationSplit <= 0 || validationSplit >= 1)
                throw new ArgumentOutOfRangeException(nameof(validationSplit), "検証データ割合は0より大きく1より小さい値にしてください。");

            int validation = (int)(sampleCount * validationSplit);
            return (sampleCount - validation, validation);
        }

        // 260930Codex: 重みを学習する前に、完全な学習バッチと空でない検証集合を必須にする。
        public static string? GetFailureReason(int trainingCount, int validationCount, int batchSize)
        {
            if (batchSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(batchSize), "バッチサイズは1以上にしてください。");
            if (trainingCount < batchSize)
                return $"学習用{trainingCount}本がバッチサイズ{batchSize}未満です。スペクトル本数を増やすか、バッチサイズを小さくしてください。";
            if (validationCount == 0)
                return "検証用スペクトルが0本です。スペクトル本数または検証データ割合を見直してください。";
            return null;
        }
    }
}
