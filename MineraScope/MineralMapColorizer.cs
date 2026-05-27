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

        // 260528Claude: 凡例で 1 つだけ強調するとき、非選択カテゴリを塗る dim color。Unclassified(黒) と Other(灰) の中間域に置いて区別を保つ。
        private static readonly (byte R, byte G, byte B) DimColor = (48, 48, 48);

        // 260526Claude: 視認性重視の固定 20 色（グレー/黒は Other/未判定と衝突するため含めない）。
        private static readonly Color[] TopPalette =
        [
            Color.FromArgb(230, 25, 75),   Color.FromArgb(60, 180, 75),   Color.FromArgb(255, 225, 25),
            Color.FromArgb(0, 130, 200),   Color.FromArgb(245, 130, 48),  Color.FromArgb(145, 30, 180),
            Color.FromArgb(70, 240, 240),  Color.FromArgb(240, 50, 230),  Color.FromArgb(210, 245, 60),
            Color.FromArgb(250, 190, 212), Color.FromArgb(0, 128, 128),   Color.FromArgb(220, 190, 255),
            Color.FromArgb(170, 110, 40),  Color.FromArgb(255, 250, 200), Color.FromArgb(128, 0, 0),
            Color.FromArgb(170, 255, 195), Color.FromArgb(128, 128, 0),   Color.FromArgb(255, 215, 180),
            Color.FromArgb(0, 0, 128),     Color.FromArgb(255, 165, 0),
        ];

        public static MineralMapImage Build(PtsClassificationMapResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            // 260526Claude: ラベル別の出現ブロック数を数える（未判定は別カウント）。
            var occurrence = new Dictionary<int, int>();
            int unclassifiedCount = 0;
            for (int i = 0; i < result.BlockCount; i++)
            {
                int labelId = result.GetLabelIdAt(i);
                if (labelId < 0)
                {
                    unclassifiedCount++;
                    continue;
                }
                occurrence[labelId] = occurrence.TryGetValue(labelId, out int count) ? count + 1 : 1;
            }

            // 260526Claude: 出現数降順（同数は labelId 昇順）で上位 20 を選ぶ。
            var topLabelIds = occurrence
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(TopMineralCount)
                .Select(pair => pair.Key)
                .ToList();

            var labelToDisplay = new Dictionary<int, int>(topLabelIds.Count);
            for (int display = 0; display < topLabelIds.Count; display++)
                labelToDisplay[topLabelIds[display]] = display;

            int otherDisplayIndex = topLabelIds.Count;
            int unclassifiedDisplayIndex = topLabelIds.Count + 1;
            int categoryCount = topLabelIds.Count + 2;

            var palette = new (byte R, byte G, byte B)[categoryCount];
            // 260528Codex: 凡例と palette が同じ色選択を使うよう、色取得と RGB 変換を helper に寄せます。
            for (int display = 0; display < topLabelIds.Count; display++)
            {
                Color color = GetTopColor(display);
                palette[display] = ToPaletteEntry(color);
            }
            palette[otherDisplayIndex] = ToPaletteEntry(OtherColor);
            palette[unclassifiedDisplayIndex] = ToPaletteEntry(UnclassifiedColor);

            // 260526Claude: 各ブロックを表示インデックスへ。Other に落ちたブロック数も数える。
            var values = new double[result.BlockCount];
            int otherCount = 0;
            for (int i = 0; i < result.BlockCount; i++)
            {
                int labelId = result.GetLabelIdAt(i);
                int display;
                if (labelId < 0)
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

            var legend = new List<MineralMapLegendEntry>(categoryCount);
            for (int display = 0; display < topLabelIds.Count; display++)
            {
                int labelId = topLabelIds[display];
                Color color = GetTopColor(display);
                legend.Add(new MineralMapLegendEntry(result.GetMineralName(labelId), color, occurrence[labelId], false, false));
            }
            legend.Add(new MineralMapLegendEntry("Other", OtherColor, otherCount, true, false));
            legend.Add(new MineralMapLegendEntry("未判定", UnclassifiedColor, unclassifiedCount, false, true));

            return new MineralMapImage(values, result.GridWidth, palette, categoryCount, legend);
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

        // 260528Codex: 上位鉱物の色選択を一か所に集め、palette と凡例のずれを防ぎます。
        private static Color GetTopColor(int displayIndex) => TopPalette[displayIndex % TopPalette.Length];

        // 260528Codex: PseudoBitmap 用 palette entry への変換を明示し、Color.FromArgb 往復を避けます。
        private static (byte R, byte G, byte B) ToPaletteEntry(Color color) => (color.R, color.G, color.B);
    }

    // 260526Claude: PseudoBitmap 生成に必要な案2 表示データ。Values は表示インデックス、Palette/CategoryCount で色付けする。
    internal sealed class MineralMapImage
    {
        public MineralMapImage(
            double[] values,
            int width,
            (byte R, byte G, byte B)[] palette,
            int categoryCount,
            IReadOnlyList<MineralMapLegendEntry> legend)
        {
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(palette);
            ArgumentNullException.ThrowIfNull(legend);

            if (width <= 0 || values.Length % width != 0)
                throw new ArgumentException("Values length is not a multiple of width.", nameof(values));

            if (palette.Length != categoryCount)
                throw new ArgumentException("Palette length must match the category count.", nameof(palette));

            Values = values;
            Width = width;
            Palette = palette;
            CategoryCount = categoryCount;
            Legend = legend;
        }

        public double[] Values { get; }

        public int Width { get; }

        public (byte R, byte G, byte B)[] Palette { get; }

        // 260526Claude: PseudoBitmap の MaxValue に使う（MinValue=0, MaxValue=CategoryCount で表示インデックスが 1:1 対応）。
        public int CategoryCount { get; }

        public IReadOnlyList<MineralMapLegendEntry> Legend { get; }
    }

    // 260526Claude: 凡例 1 行（色見本・鉱物名・出現ブロック数）。Other/未判定はフラグで区別する。
    internal readonly record struct MineralMapLegendEntry(string MineralName, Color Color, int BlockCount, bool IsOther, bool IsUnclassified);
}
