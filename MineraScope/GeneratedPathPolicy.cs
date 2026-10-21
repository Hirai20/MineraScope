using System.IO;

namespace MineraScope
{
    // 261021Claude: 保存先の深さに対するパス長上限を 1 か所で扱う。
    //   Documents の位置はユーザー名・OneDrive リダイレクトで PC ごとに変わる。LongPathsEnabled が無効な PC や
    //   エクスプローラー・Excel などの非対応ツールでも扱えるよう、生成するファイルを MAX_PATH に収める。
    internal static class GeneratedPathPolicy
    {
        // 261021Claude: 終端 NUL を除いた MAX_PATH。OS 設定や対応ツールに依存せず扱えるフルパスの上限。
        public const int MaxFullPathLength = 259;

        // 261021Claude: 最深ファイル名の見積もりに対する余裕。将来の小さなファイル名追加で即超過しないようにする。
        private const int SafetyMargin = 8;

        // 261021Claude: folder の下で生成される最深ファイルが上限を超える場合に、事前検査で返す拒否理由を作る。
        //   実測した最悪値を渡す場合だけ safetyMargin を 0 にできる。
        public static string? FindTooLong(string folder, int deepestRelativeLength, string target, int safetyMargin = SafetyMargin)
        {
            int length = Path.GetFullPath(folder).Length + 1 + deepestRelativeLength + safetyMargin;
            return length <= MaxFullPathLength ? null
                : $"{target}の保存先が深すぎます。中に作るファイルのパスが最大でおよそ{length}文字になり、"
                    + $"Windowsの上限{MaxFullPathLength}文字を超えます。保存先を浅い場所にするか、名前を短くしてください。\r\n{folder}";
        }
    }
}
