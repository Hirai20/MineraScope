using System.Collections.Generic;

namespace MineraScope
{
    // 260430Codex: 学習データフォルダの解析結果 DTO を DeepLearning 本体から分離します。
    public class MineralFolder
    {
        // 260430Codex: フォルダ名から推定した鉱物名を保持します。
        public string Name { get; set; } = string.Empty;

        // 260430Codex: 解析対象になった実フォルダパスを保持します。
        public string FolderPath { get; set; } = string.Empty;

        // 260430Codex: 端成分ラベルが複数見つかったフォルダかどうかを保持します。
        public bool IsSolidSolution { get; set; }

        // 260430Codex: 検出した端成分名の一覧を保持します。
        public List<string> EndMembers { get; set; } = [];
    }
}
