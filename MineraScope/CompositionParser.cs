using Crystallography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MineraScope
{
    public class CompositionParser
    {
        //public static (string Name, double Mol)[] ParseComposition(string composition)
        //{
        //    var list = new List<(string Name, double Mol)>();
        //    //正規表現で固溶体部分を抽出
        //    Match matches1;
        //    while ((matches1 = Regex.Match(composition, @"(\([^)]+\)\d*\.?\d*)")).Success)
        //    {
        //        var SsText = matches1.Value; // 例: "(Mg,Fe)3"
        //        var innerMatch = Regex.Match(SsText, @"\(([^)]+)\)(\d*\.?\d*)");

        //        string elementsPart = innerMatch.Groups[1].Value; // "Mg,Fe"
        //        string molTotalStr = innerMatch.Groups[2].Value; // "3"
        //        double mol = string.IsNullOrWhiteSpace(molTotalStr) ? 1.0 : double.Parse(molTotalStr);

        //        // 固溶体部分を (Name, Mol) のタプルとして追加
        //        list.Add((elementsPart, mol));

        //        // 解析済みの固溶体部分を文字列から削除
        //        composition = composition.Replace(SsText, "");
        //    }

        //    // 正規表現で元素記号 + モル数（省略可能）を抽出
        //    var matches2 = Regex.Matches(composition, @"([A-Z][a-z]*)([0-9.]*)");

        //        foreach (Match match in matches2)
        //        {
        //            string elements = match.Groups[1].Value;         // 元素記号 
        //            string molStr = match.Groups[2].Value;         // モル数 
        //            if (Regex.IsMatch(elements, @"^[A-Z][a-z]{2,}$")) //// 小文字が2文字以上続く元素記号はエラー
        //            {
        //                return Array.Empty<(string, double)>();
        //            }
        //        double mol = string.IsNullOrWhiteSpace(molStr) ? 1.0 : double.Parse(molStr);
        //        list.Add((elements, mol));
        //    }
        


        //    return list.ToArray();
        //    }
        
        public static (string Name, double Mol)[] ParseComposition(string composition)
        {
            var list = new List<(string Name, double Mol)>();
         // 正規表現で元素記号 + モル数（省略可能）を抽出
            var matches = Regex.Matches(composition, @"([A-Z][a-z]*)([0-9.]*)");

                foreach (Match match in matches)
                {
                    string elements = match.Groups[1].Value;         // 元素記号 
                    string molStr = match.Groups[2].Value;         // モル数 
                    if (Regex.IsMatch(elements, @"^[A-Z][a-z]{2,}$")) //// 小文字が2文字以上続く元素記号はエラー
                    {
                        return Array.Empty<(string, double)>();
                    }
                double mol = string.IsNullOrWhiteSpace(molStr) ? 1.0 : double.Parse(molStr);
                list.Add((elements, mol));
            }
        


            return list.ToArray();
            }
        }
    }


