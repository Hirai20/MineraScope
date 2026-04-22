using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MineraScope
{
    public class GetRatio
    {
        //public static List<List<double>> GetRatios(int elementNum, double total, double step)
        //{
        //    double FixZero(double x) => Math.Abs(x) < 1e-10 ? 0.0 : x;
        //    var result = new List<List<double>>();

        //    //if (total < step * 0.5)
        //    //    return [[.. Enumerable.Repeat(0.0, elementNum)]];

        //    for (double last = 0; last <= total + step * 0.5; last += step)
        //    {
        //        if (elementNum == 2)
        //            result.Add(new List<double>([FixZero(total - last), FixZero(last)]));

        //        else
        //        {
        //            var resultTmp = GetRatios(elementNum - 1, total - last, step);
        //            for (int i = 0; i < resultTmp.Count; i++)
        //            {
        //                resultTmp[i].Add(FixZero(last));
        //                result.Add(resultTmp[i]);
        //            }
        //        }
        //    }
        //    return result;
        //}
        //public static List<List<double>> CombineRatioGroups(List<List<List<double>>> ratioGroups)
        //{
        //    List<List<double>> combinedRatios = ratioGroups[0];

        //    // 2番目以降のListを順番に、ベースに対して結合
        //    for (int i = 1; i < ratioGroups.Count; i++)
        //    {
        //        var nextGroupRatios = ratioGroups[i];
        //        var tempResult = new List<List<double>>();

        //        foreach (var r1 in combinedRatios) 
        //        {
        //            foreach (var r2 in nextGroupRatios) 
        //            {
        //                var newCombination = new List<double>();
        //                newCombination.AddRange(r1); 
        //                newCombination.AddRange(r2); 
        //                tempResult.Add(newCombination);
        //            }
        //        }
        //        combinedRatios = tempResult;
        //    }
        //    return combinedRatios;
        //}

        //端成分の比率リストを生成
        public static List<List<double>> GetRatios(int elementNum, double step)
        {
            double FixZero(double x) => Math.Abs(x) < 1e-10 ? 0.0 : x;
            var result = new List<List<double>>();
            //if (total < step * 0.5)
            //    return [[.. Enumerable.Repeat(0.0, elementNum)]];

            for (double last = 0; last <= 1; last += step)
            {
                if (elementNum == 2)
                    result.Add(new List<double>([FixZero(1 - last), FixZero(last)]));

                else
                {
                    var resultTmp = GetRatios(elementNum - 1, step);
                    for (int i = 0; i < resultTmp.Count; i++)
                    {
                        resultTmp[i].Add(FixZero(last));
                        result.Add(resultTmp[i]);
                    }
                }
            }
            return result;
        }
    }
}

