using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MineraScope
{
    // 260507Codex: 同じ spectrum pool とみなす生成条件だけを正規化して conditionKey を作ります。
    internal sealed class SpectrumConditionKeyBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        // 260507Codex: target 件数・並列数・学習条件を含めず、生成結果に影響する条件だけを snapshot 化します。
        public SpectrumGenerationConditionSnapshot CreateSnapshot(
            SolidSolution solution,
            double resolutionStep,
            SemEdxCondition semEdxCondition) =>
            new()
            {
                MineralName = solution.Name,
                MineralFormula = solution.Formula,
                Endmembers = solution.Members
                    .OrderBy(member => member.Name)
                    .Select(member => new EndmemberConditionSnapshot
                    {
                        Name = member.Name,
                        Formula = member.FormulaText
                    })
                    .ToList(),
                Constraints = solution.Constraints.OrderBy(constraint => constraint).ToList(),
                CompositionResolution = resolutionStep,
                SemEdxCondition = semEdxCondition
            };

        // 260507Codex: snapshot JSON の SHA-256 を短縮せず使い、pool フォルダ名として衝突しにくくします。
        public string BuildKey(SpectrumGenerationConditionSnapshot snapshot)
        {
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
