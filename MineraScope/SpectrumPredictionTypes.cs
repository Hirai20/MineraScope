namespace MineraScope
{
    // 260620Claude: 1スペクトル分の予測結果（分類・回帰端成分・組成式）を構造化して保持する。
    //               失敗時は Classification=null かつ Error にメッセージを持たせ、CSV からは除外し report 末尾に一覧する。
    internal sealed record SpectrumPredictionItem(
        string FilePath,
        MineralClassificationPredictionResult? Classification,
        string? RegressionModelName,
        IReadOnlyList<MineralComponentRatio> Endmembers,
        string ChemicalFormula,
        string? Error)
    {
        public string FileName => Path.GetFileName(FilePath);

        // 260623Claude: 回帰のみ予測 (分類スキップ) では Classification=null・端成分ありで成功とみなす。
        public bool IsSuccess => Error is null && (Classification is not null || Endmembers.Count > 0);
    }

    // 260620Claude: バッチ全体の結果。Model 名（コンボ選択名）と各 item を真実源にし、画面表示・CSV・report はここから作る。
    internal sealed record SpectrumPredictionBatch(
        string ModelName,
        IReadOnlyList<SpectrumPredictionItem> Items);
}
