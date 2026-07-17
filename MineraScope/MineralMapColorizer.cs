using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MineraScope
{
    // 260526Claude: 分類結果を案2（ブロック解像度ソース）の PseudoBitmap 表示用データへ変換する。
    // 出現数上位 20 鉱物＋Other(グレー)＋未判定(黒)。値は表示インデックス 0..K-1 で、PseudoBitmap の MinValue=0/MaxValue=K と 1:1 対応する。
    internal static class MineralMapColorizer
    {
        private const int TopMineralCount = 20;

        // 260526Claude: Other はグレー、未判定は黒。上位鉱物色とは別色にする。
        private static readonly Color OtherColor = Color.FromArgb(128, 128, 128);
        private static readonly Color UnclassifiedColor = Color.FromArgb(0, 0, 0);
        // 260622Codex: Unknown has signal and model output, so it needs a color distinct from zero-signal black.
        private static readonly Color UnknownColor = Color.FromArgb(255, 255, 255);

        // 260717Codex: Automatic mineral colors keep a perceptual distance from all fixed map categories.
        private static readonly Color[] ReservedColors = [OtherColor, UnknownColor, UnclassifiedColor];

        // 260528Claude: 凡例で 1 つだけ強調するとき、非選択カテゴリを塗る dim color。Unclassified(黒) と Other(灰) の中間域に置いて区別を保つ。
        private static readonly (byte R, byte G, byte B) DimColor = (48, 48, 48);

        // 260717Codex: Keep TOP20 occurrence-ranked while resolving every mineral color through the persistent name palette.
        public static MineralMapImage Build(PtsClassificationMapResult result, MineralColorPalette mineralColors)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(mineralColors);

            // 260526Claude: ラベル別の出現ブロック数を数える（未判定は別カウント）。
            var occurrence = new Dictionary<int, int>();
            int unclassifiedCount = 0;
            int unknownCount = 0;
            for (int i = 0; i < result.BlockCount; i++)
            {
                int labelId = result.GetLabelIdAt(i);
                if (labelId == PtsClassificationMapResult.UnknownLabelId)
                {
                    unknownCount++;
                    continue;
                }
                if (labelId < 0)
                {
                    unclassifiedCount++;
                    continue;
                }
                occurrence[labelId] = occurrence.TryGetValue(labelId, out int count) ? count + 1 : 1;
            }

            // 260526Claude: 出現数降順（同数は labelId 昇順）で上位 20 を選ぶ。
            var rankedMinerals = occurrence
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(TopMineralCount)
                .Select(pair => new RankedMineral(
                    pair.Key,
                    GetMineralName(result, pair.Key),
                    pair.Value))
                .ToList();

            // 260717Codex: Seed distance checks with all already-fixed TOP20 colors before assigning any new entries.
            var choices = new MineralColorChoice?[rankedMinerals.Count];
            var usedColors = new List<Color>(ReservedColors);
            for (int i = 0; i < rankedMinerals.Count; i++)
            {
                if (!mineralColors.TryGetColor(rankedMinerals[i].Name, out MineralColorChoice choice))
                    continue;

                choices[i] = choice;
                usedColors.Add(choice.Color);
            }

            var topMinerals = new List<TopMineral>(rankedMinerals.Count);
            for (int i = 0; i < rankedMinerals.Count; i++)
            {
                RankedMineral mineral = rankedMinerals[i];
                MineralColorChoice choice = choices[i] ?? mineralColors.AssignAutomaticColor(mineral.Name, usedColors);
                if (!choices[i].HasValue)
                    usedColors.Add(choice.Color);
                topMinerals.Add(new TopMineral(mineral.LabelId, mineral.Name, mineral.BlockCount, choice.Color, choice.Assignment));
            }

            var labelToDisplay = new Dictionary<int, int>(topMinerals.Count);
            for (int display = 0; display < topMinerals.Count; display++)
                labelToDisplay[topMinerals[display].LabelId] = display;

            int otherDisplayIndex = topMinerals.Count;
            int unknownDisplayIndex = topMinerals.Count + 1;
            int unclassifiedDisplayIndex = topMinerals.Count + 2;
            int categoryCount = topMinerals.Count + 3;

            var palette = new (byte R, byte G, byte B)[categoryCount];
            // 260710Codex: Use mineral-name colors so maps stay comparable across sweep/binning changes.
            for (int display = 0; display < topMinerals.Count; display++)
                palette[display] = ToPaletteEntry(topMinerals[display].Color);
            palette[otherDisplayIndex] = ToPaletteEntry(OtherColor);
            palette[unknownDisplayIndex] = ToPaletteEntry(UnknownColor);
            palette[unclassifiedDisplayIndex] = ToPaletteEntry(UnclassifiedColor);

            // 260526Claude: 各ブロックを表示インデックスへ。Other に落ちたブロック数も数える。
            var values = new double[result.BlockCount];
            int otherCount = 0;
            for (int i = 0; i < result.BlockCount; i++)
            {
                int labelId = result.GetLabelIdAt(i);
                int display;
                if (labelId == PtsClassificationMapResult.UnknownLabelId)
                    display = unknownDisplayIndex;
                else if (labelId < 0)
                    display = unclassifiedDisplayIndex;
                else if (labelToDisplay.TryGetValue(labelId, out int mapped))
                    display = mapped;
                else
                {
                    display = otherDisplayIndex;
                    otherCount++;
                }
                values[i] = display;
            }

            // 260717Codex: Preserve the hidden mineral breakdown so scientific legend exports can disclose Other contents.
            var otherMembers = occurrence
                .Where(pair => !labelToDisplay.ContainsKey(pair.Key))
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => new MineralMapOtherEntry(GetMineralName(result, pair.Key), pair.Value))
                .ToList();

            var legend = new List<MineralMapLegendEntry>(categoryCount);
            for (int display = 0; display < topMinerals.Count; display++)
            {
                TopMineral mineral = topMinerals[display];
                legend.Add(new MineralMapLegendEntry(
                    mineral.Name,
                    mineral.Color,
                    mineral.BlockCount,
                    mineral.Assignment,
                    false,
                    false));
            }
            legend.Add(new MineralMapLegendEntry("Other", OtherColor, otherCount, MineralColorAssignment.Fixed, true, false));
            // 260622Codex: Show unknown spectra separately from existing zero-signal unclassified blocks.
            legend.Add(new MineralMapLegendEntry(MineralUnknownDetector.UnknownDisplayName, UnknownColor, unknownCount, MineralColorAssignment.Fixed, false, false));
            legend.Add(new MineralMapLegendEntry("未判定", UnclassifiedColor, unclassifiedCount, MineralColorAssignment.Fixed, false, true));

            return new MineralMapImage(values, result.GridWidth, palette, categoryCount, legend, otherMembers);
        }

        // 260528Claude: 凡例ハイライト用に、選択カテゴリだけ元色・他は DimColor の palette を新規生成する。Values 配列はそのまま使い回せる。
        public static (byte R, byte G, byte B)[] BuildHighlightedPalette((byte R, byte G, byte B)[] source, int selectedDisplayIndex)
        {
            ArgumentNullException.ThrowIfNull(source);
            var result = new (byte R, byte G, byte B)[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = i == selectedDisplayIndex ? source[i] : DimColor;
            return result;
        }

        // 260717Codex: Model artifacts should contain names; retain a deterministic fallback for malformed legacy encoders.
        private static string GetMineralName(PtsClassificationMapResult result, int labelId)
        {
            string mineralName = result.GetMineralName(labelId).Trim();
            return mineralName.Length > 0 ? mineralName : $"Label {labelId}";
        }

        // 260710Codex: Keep palette entries in the tuple format expected by PseudoBitmap.
        private static (byte R, byte G, byte B) ToPaletteEntry(Color color) => (color.R, color.G, color.B);

        // 260717Codex: Hold occurrence-ranked minerals before their persistent colors are resolved.
        private readonly record struct RankedMineral(int LabelId, string Name, int BlockCount);

        // 260717Codex: Carry the resolved color into both palette and legend construction.
        private readonly record struct TopMineral(
            int LabelId,
            string Name,
            int BlockCount,
            Color Color,
            MineralColorAssignment Assignment);
    }

    // 260526Claude: PseudoBitmap 生成に必要な案2 表示データ。Values は表示インデックス、Palette/CategoryCount で色付けする。
    internal sealed class MineralMapImage
    {
        public MineralMapImage(
            double[] values,
            int width,
            (byte R, byte G, byte B)[] palette,
            int categoryCount,
            IReadOnlyList<MineralMapLegendEntry> legend,
            IReadOnlyList<MineralMapOtherEntry> otherMembers)
        {
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(palette);
            ArgumentNullException.ThrowIfNull(legend);
            ArgumentNullException.ThrowIfNull(otherMembers);

            if (width <= 0 || values.Length % width != 0)
                throw new ArgumentException("Values length is not a multiple of width.", nameof(values));

            if (palette.Length != categoryCount)
                throw new ArgumentException("Palette length must match the category count.", nameof(palette));

            Values = values;
            Width = width;
            Palette = palette;
            CategoryCount = categoryCount;
            Legend = legend;
            OtherMembers = otherMembers;
        }

        public double[] Values { get; }

        public int Width { get; }

        public (byte R, byte G, byte B)[] Palette { get; }

        // 260526Claude: PseudoBitmap の MaxValue に使う（MinValue=0, MaxValue=CategoryCount で表示インデックスが 1:1 対応）。
        public int CategoryCount { get; }

        public IReadOnlyList<MineralMapLegendEntry> Legend { get; }

        // 260717Codex: List the model labels aggregated into the displayed Other category.
        public IReadOnlyList<MineralMapOtherEntry> OtherMembers { get; }
    }

    // 260526Claude: 凡例 1 行（色見本・鉱物名・出現ブロック数）。Other/未判定はフラグで区別する。
    // 260717Codex: Include assignment provenance so editing and exported legends distinguish fixed, automatic, and manual colors.
    internal readonly record struct MineralMapLegendEntry(
        string MineralName,
        Color Color,
        int BlockCount,
        MineralColorAssignment Assignment,
        bool IsOther,
        bool IsUnclassified);

    // 260717Codex: Describe each mineral hidden by the displayed Other aggregate.
    internal readonly record struct MineralMapOtherEntry(string MineralName, int BlockCount);
}
