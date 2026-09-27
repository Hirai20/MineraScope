using System.IO;

namespace MineraScope
{
    // 260727Claude: 学習成果物のフォルダ・ファイル名レイアウトを 1 か所に集約する。
    //   同じ "AllMinerals_Classification" 文字列が学習・単発予測・バッチ予測・PTS マップ・表示前処理の
    //   6 ファイル 10 箇所へ散っており、片方だけ変えると経路ごとに違うフォルダを見に行くため。
    //   命名規約 (分類サブフォルダ・回帰サブフォルダ) の互換性を壊さないよう、値は従来と同一。
    internal static class ModelArtifactPaths
    {
        // 260727Claude: モデル親フォルダ直下の分類モデルフォルダ名。labelEncoder.json / preprocessing.json はこの中。
        public const string ClassificationFolderName = "AllMinerals_Classification";

        // 260727Claude: 回帰モデルフォルダの接尾辞。"{鉱物名}_Regression" が命名規約。
        public const string RegressionFolderSuffix = "_Regression";

        // 260728Claude: 学習が書き、予測が読む成果物のファイル名。フォルダ名だけ集約して
        //   ファイル名を各所のリテラルに残すと「どこで定義されているか」の規則が分裂するため、
        //   互換性クリティカルな 4 つもここへ寄せる。
        //   (preprocessing.json / unknownDetector.json は各型が自分の FileName を持つ既存規約に従う)
        public const string LabelEncoderFileName = "labelEncoder.json";
        public const string ComponentIndexFileName = "componentIndex.json";
        public const string ModelTypeFileName = "modelType.txt";
        public const string MineralNameFileName = "mineralName.txt";

        // 260901Codex: The model root records exactly which multi-time manifest entries were selected for training.
        public const string TrainingDataProvenanceFileName = "trainingDataProvenance.json";

        // 260906Codex: The model root records the validation metrics observed while this model was trained.
        public const string TrainingResultsFileName = "trainingResults.json";

        // 260727Claude: 選択中モデルフォルダから分類モデルフォルダへの解決を 1 か所にする。
        public static string GetClassificationFolder(string modelPath) =>
            Path.Combine(modelPath, ClassificationFolderName);

        // 260727Claude: 予測鉱物名に対応する回帰モデルフォルダの検索パターン (単発・バッチ予測で共有)。
        public static string GetRegressionSearchPattern(string mineralName) =>
            $"{mineralName}*{RegressionFolderSuffix}";
    }
}
