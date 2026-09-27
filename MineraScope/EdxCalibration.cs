using System.Globalization;
using MathNet.Numerics.LinearAlgebra.Double;

namespace MineraScope
{
    // 260724Claude: EmsaConverter (別リポジトリ) の Emsa.cs から EDX 較正の純ロジックをそのまま移植。
    /// <summary>
    /// スペクトル1本の読み込み結果。Offset と XperChannel (単位: eV) は、エネルギー列を持つ形式のときだけ値を持つ
    /// </summary>
    internal sealed record Spectrum(List<double> Counts, double? Offset, double? XperChannel);

    /// <summary>
    /// 実測スペクトルとシミュスペクトルの Rwp を計算し、検出器の Offset / eV per channel を較正するロジック。
    /// </summary>
    internal static class EdxCalibration
    {
        // 260803Codex: Rwp は最小二乗でスケール係数を合わせるため必ず 0～1 に収まり、1e3 はどの正当な結果よりも悪い値になる。
        // NaN は NelderMead の大小比較を全て false にして探索を壊すため、使用不能な軸には有限の定数を返す。
        public const double UnusableAxisScore = 1e3;

        #region ReadSpectrum　テキスト行からスペクトルを読み込む
        /// <summary>
        /// テキスト行からスペクトルを読み込む。
        /// カウントだけの1列形式と、"Energy (keV),Counts" のようなエネルギー列つきの2列形式の両方に対応する
        /// </summary>
        //260723Claude 2列形式のCSVに対応
        public static Spectrum ReadSpectrum(string[] lines)
        {
            var counts = new List<double>();
            var energies = new List<double>();
            double toEv = 1000;//エネルギー列の単位変換係数。単位表記が見つからないときは keV とみなす

            foreach (var line in lines)
            {
                var str = line.TrimStart('﻿').Trim();
                if (str.Length == 0 || str.StartsWith('#')) continue;

                //末尾のカンマだけの要素は取り除かれるので、"123," のような1列形式は items.Length == 1 になる
                // 260803Codex: FormMain と同じく空白・タブ・カンマ区切りを受け付ける。
                var items = str.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (items.Length == 1)
                {
                    if (TryParse(items[0], out var value))
                        counts.Add(value);
                }
                else if (TryParse(items[0], out var energy) && TryParse(items[1], out var count))
                {
                    energies.Add(energy);
                    counts.Add(count);
                }
                else
                {
                    // 260803Claude: 単位表記は第1カンマ列にある ("Energy (eV)")。空白でも分割すると "(eV)" が切れるので元行から取り直す。
                    //   ヘッダー行でしか要らないので、数値行 2048 本ぶんの無駄な分割を避けてこの分岐の中で求める。
                    var energyColumn = str.Split(',', 2, StringSplitOptions.TrimEntries)[0];
                    if (energyColumn.Contains("eV", StringComparison.OrdinalIgnoreCase) && !energyColumn.Contains("keV", StringComparison.OrdinalIgnoreCase))
                        toEv = 1;//"Energy (eV)" のようなヘッダーのときだけ単位変換が不要
                }
            }

            //エネルギー列があるときは等間隔とみなし、両端から Offset と 1チャンネルあたりのエネルギーを求める
            if (energies.Count < 2)
                return new Spectrum(counts, null, null);

            return new Spectrum(counts, energies[0] * toEv, (energies[^1] - energies[0]) * toEv / (energies.Count - 1));

            static bool TryParse(string str, out double value)
                => double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        #endregion



        #region GetValues　指定されたxの値に対してorder次関数で補間した値を返す
        /// <summary>
        /// 指定されたxの値に対してorder次関数で補間した値を返す
        /// </summary>
        /// <param name="x"></param>
        /// <param name="pointNum">探索する点数</param>
        /// <param name="order">フィッティングする次数</param>
        /// <param name="eachside">両サイドでなるべく均等な点数を採用</param>
        /// <param name="shareSameFunction"></param>
        ///<returns></returns>
        public static double[] GetValues((double X, double Y)[] Pt, double[] x, int pointNum, int order, bool eachside = false, bool shareSameFunction = false)
        {
            if (Pt.Length == 0) return [];
            if (pointNum < 2) pointNum = 2;
            if (order > pointNum - 1) order = pointNum - 1;
            double[] value = new double[x.Length];

            for (int n = 0; n < x.Length; n++)
            {
                bool flag = true;
                //まず点の位置を探す
                int position = 0;//この値と この値+1の間のindexにxが存在する
                if (Pt[0].X > x[n])
                    position = -1;
                else if (Pt[^1].X < x[n])
                    position = Pt.Length - 1;
                else
                    for (int i = 0; i < Pt.Length - 1; i++)
                        if (Pt[i + 1].X == x[n])
                        {
                            value[n] = Pt[i + 1].Y;
                            flag = false;
                            break;
                        }
                        else if (Pt[i].X <= x[n] && x[n] <= Pt[i + 1].X)
                        {
                            position = i;
                            break;
                        }
                if (flag)
                {
                    //次に、この点から前後にPointNumだけ近い点を探す
                    var pt = new List<(double X, double Y)>(pointNum);

                    if (eachside)//両側でなるべく均等な点数がほしいとき
                    {
                        int i = 1;
                        while (pt.Count < pointNum)
                        {
                            if (position - i + 1 >= 0)
                                pt.Add(Pt[position - i + 1]);
                            if (position + i < Pt.Length)
                                pt.Add(Pt[position + i]);
                            i++;
                        }
                    }
                    else//とにかく近い点を見つけるとき
                    {
                        for (int i = Math.Max(position - pointNum, 0); i < Math.Min(position + pointNum + 1, Pt.Length); i++)
                            pt.Add(Pt[i]);
                        while (pt.Count > pointNum)
                            pt.RemoveAt(Math.Abs(pt[0].X - x[n]) > Math.Abs(pt[^1].X - x[n]) ? 0 : pt.Count - 1);
                    }

                    if (pt.Count < pointNum)
                    {
                        pointNum = pt.Count;
                        if (order > pointNum - 1)
                            order = pointNum - 1;
                    }

                    //計算精度のため、xの範囲を1から+2に変換する 式は X = c1 x + c2;
                    double c1 = 1.0 / (pt[^1].X - pt[0].X);
                    double c2 = 1 - pt[0].X * c1;

                    var m = new DenseMatrix(pointNum, order + 1);
                    var y = new DenseMatrix(pointNum, 1);
                    for (int j = 0; j < pointNum; j++)
                    {
                        y[j, 0] = pt[j].Y;
                        for (int i = 0; i < order + 1; i++)
                            m[j, i] = Math.Pow(c1 * pt[j].X + c2, i);
                    }

                    var a = (m.TransposeThisAndMultiply(m)).Inverse() * m.TransposeThisAndMultiply(y);
                    if (a == null || a.ColumnCount == 0)
                        value[n] = pt[position >= 0 ? position : 0].Y;
                    else
                    {
                        value[n] = 0;
                        for (int j = 0; j < order + 1; j++)
                            value[n] += a[j, 0] * Math.Pow(c1 * x[n] + c2, j);
                    }
                }
            }
            return value;
        }
        #endregion

        //Rwpの計算
        public static double CalculationRwp(List<double> ResultValues, List<double> SimulatedValues, int skip = 32)
        {
            // 260803Codex: skip が全点数以上なら集計ループが一度も実行されず 0/0 になるため、比較不能として扱う。
            if (ResultValues.Count != SimulatedValues.Count || ResultValues.Count == 0 || skip >= ResultValues.Count)
                return double.NaN;


            var weights = ResultValues.Select(e => e > 1 ? 1.0 / e : 1.0).ToList();

            double numeratora = 0, denominatora = 0;
            for (int i = skip; i < ResultValues.Count; i++)
            {
                //sim値をフィッテイング
                numeratora += weights[i] * ResultValues[i] * SimulatedValues[i];
                denominatora += weights[i] * SimulatedValues[i] * SimulatedValues[i];
            }
            double a = numeratora / denominatora;

            double numeratorRwp = 0, denominatorRwp = 0;
            for (int i = skip; i < ResultValues.Count; i++)
            {
                double diff = ResultValues[i] - a * SimulatedValues[i];

                numeratorRwp += weights[i] * diff * diff;
                denominatorRwp += weights[i] * ResultValues[i] * ResultValues[i];
            }

            return Math.Sqrt(numeratorRwp / denominatorRwp);

        }
    }
}
