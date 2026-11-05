// 261106Codex: 検証済みの検出器登録・入力検証を公開版へ移す。
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MineraScope
{
    // 260626Codex: findDetectorやDerby DBを使わず、MineraScopeの検出器プロファイルからGUIなしのDTSA-IIスクリプトを生成する。
    internal sealed class SimulationScriptGenerator
    {
        private const double DefaultDensity = 3;
        // 260626Codex: 欠損しているmsiのLib\dtsa2パッケージを読み込まず、DTSA-II mcSimulate3の既定値に合わせる。
        private const int DefaultTrajectoryCount = 1000;
        // 261103Claude: 生成スクリプトが例外の理由を stderr へ書くときの印。失敗理由を Java の警告と分けて取り出すために使う。
        private const string ScriptErrorTag = "MINERASCOPE_ERROR|";

        // 261103Claude: stderr から生成スクリプト自身が書いた失敗理由 (最後の印付きの行) を取り出す。印が無ければ null。
        internal static string? FindScriptError(string standardError) =>
            standardError
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static line => line.StartsWith(ScriptErrorTag, StringComparison.Ordinal))?[ScriptErrorTag.Length..];

        public string Generate(SimulationProperty property, int? parallelIndex = null)
        {
            StringBuilder builder = new();
            (string ElementName, double Weight)[][] atoms = property.Atoms1;
            string[] outputFiles = property.OutputFiles;
            // 261103Claude: 受け取った検出器の値をここで既定値に埋め直さず、全項目を検査してから DTSA-II へ渡す。
            DetectorProfile detector = property.DetectorProfile
                ?? throw new InvalidOperationException("生成スクリプトに検出器プロファイルが渡されていません。");
            // 261030Claude: 層を参照する前にこのメソッド自身で窓の前提を確かめ、不正な窓を DTSA-II へ渡さない。
            detector.Validate();

            builder.AppendLine("# -*- coding: utf-8 -*-");
            builder.AppendLine("import sys");
            builder.AppendLine("from java.lang import System");
            builder.AppendLine("try:");
            AppendBodyLine(builder, "import os");
            AppendBodyLine(builder, "import java.lang.Math as JMath");
            AppendBodyLine(builder, "sys.packageManager.makeJavaPackage(\"gov.nist.microanalysis.NISTMonte.Gen3\", \"CharacteristicXRayGeneration3, BremsstrahlungXRayGeneration3, FluorescenceXRayGeneration3, XRayTransport3\", None)");
            AppendBodyLine(builder, "import gov.nist.microanalysis.NISTMonte as nm");
            AppendBodyLine(builder, "import gov.nist.microanalysis.NISTMonte.Gen3 as nm3");
            AppendBodyLine(builder, "import gov.nist.microanalysis.EPQLibrary as epq");
            AppendBodyLine(builder, "import gov.nist.microanalysis.EPQLibrary.Detector as epd");
            AppendBodyLine(builder, "import gov.nist.microanalysis.Utility as epu");
            AppendBodyLine(builder, "import gov.nist.microanalysis.EPQTools as ept");
            AppendBodyLine(builder, "from java.io import FileOutputStream");
            AppendBodyLine(builder);
            AppendJavaApiHelpers(builder, detector);

            AppendBodyLine(builder, "output_dir = " + ToPythonString(property.OutputFolder));
            AppendBodyLine(builder, "if not os.path.isdir(output_dir):");
            AppendBodyLine(builder, "\tos.makedirs(output_dir)");
            AppendBodyLine(builder, "det = create_detector()");
            AppendBodyLine(builder, "e0 = " + ToInvariantString(property.BeamEnergy));
            AppendBodyLine(builder, "dose = " + ToInvariantString(property.ProbeCurrent) + " * " + ToInvariantString(property.LiveTime));
            AppendBodyLine(builder, "cThickness = " + ToInvariantString(property.CarbonCoatThickness));
            AppendBodyLine(builder, "carbonCoating = epq.MaterialFactory.createPureElement(epq.Element.C)");
            AppendBodyLine(builder, "Weights = [");

            foreach (var atom in atoms)
            {
                string joined = string.Join(", ", atom.Select(static x => ToInvariantString(x.Weight)));
                AppendBodyLine(builder, $"  [{joined}],");
            }

            AppendBodyLine(builder, "]");
            AppendBodyLine(builder, "FileNames = [");
            foreach (string fileName in outputFiles)
                AppendBodyLine(builder, "  " + ToPythonString(fileName) + ",");

            AppendBodyLine(builder, "]");
            // 261021Codex: 生成ループで使う元素名は一度だけ取り出して共用する。
            string[] elementNames = atoms[0].Select(static x => x.ElementName).ToArray();
            AppendForHeader(builder, elementNames);
            // 261021Codex: 各スペクトルは指定した固定膜厚で生成する。
            AppendSimulationLoopBody(builder, elementNames);
            AppendBodyLine(builder, "sys.exit(0)");
            builder.AppendLine("except Exception, ex:");
            builder.AppendLine("\tSystem.err.println(\"" + ScriptErrorTag + "\" + str(ex))");
            builder.AppendLine("\tSystem.err.flush()");
            builder.AppendLine("\tsys.exit(1)");
            return builder.ToString();
        }

        // 260626Codex: 最上位try内のPythonインデントを保ち、生成ループを読みやすくする。
        private static void AppendForHeader(StringBuilder builder, string[] elementNames)
        {
            builder.Append('\t').Append("for idx, (");
            builder.Append(string.Join(", ", elementNames.Select(static element => $"{element}_weight")));
            builder.AppendLine(") in enumerate(Weights):");
        }

        // 261021Codex: 膜厚を乱数で変える分岐を廃止し、共通条件のcThicknessを直接使う。
        private static void AppendSimulationLoopBody(StringBuilder builder, string[] elementNames)
        {
            foreach (string name in elementNames)
                AppendBodyLine(builder, $"\t{name.ToLowerInvariant()} = {name}_weight");

            builder.Append("\t\tmaterial = epq.Material(epq.Composition([");
            foreach (string name in elementNames)
                builder.Append($"epq.Element.{name}, ");

            builder.Remove(builder.Length - 2, 2);
            builder.Append("], [");
            foreach (string name in elementNames)
                builder.Append(name.ToLowerInvariant() + ", ");

            builder.Remove(builder.Length - 2, 2);
            builder.AppendLine("]), epq.ToSI.gPerCC(" + ToInvariantString(DefaultDensity) + "))");
            AppendBodyLine(builder, "\tSystem.out.println(\"Starting simulation {0}...\".format(idx + 1))");
            AppendBodyLine(builder, "\tSystem.out.flush()");
            AppendBodyLine(builder, "\tsd = simulate_coated_substrate(carbonCoating, cThickness, material, det, e0, dose, " + DefaultTrajectoryCount + ")");
            AppendBodyLine(builder, "\toutput_file = os.path.join(output_dir, FileNames[idx])");
            AppendBodyLine(builder, "\tepq.SpectrumUtils.rename(sd, FileNames[idx])");
            AppendBodyLine(builder, "\tsave_spectrum(sd, output_file)");
            AppendBodyLine(builder, "\tSystem.out.println(\"MINERASCOPE_SPECTRUM_SAVED|{0}|{1}\".format(idx + 1, output_file))");
            AppendBodyLine(builder, "\tSystem.out.flush()");
        }

        // 260626Codex: 編集可能な検出器プロファイルをスクリプトへ埋め込み、並列ジョブからDTSA-IIの検出器DBを開かない。
        private static void AppendJavaApiHelpers(StringBuilder builder, DetectorProfile detector)
        {
            AppendBodyLine(builder, "def create_detector():");
            AppendBodyLine(builder, "\tdet = epd.EDSDetector.createSDDDetector(" + detector.ChannelCount.ToString(CultureInfo.InvariantCulture)
                + ", " + ToInvariantString(detector.ChannelWidth)
                + ", " + ToInvariantString(detector.ZeroOffset)
                + ", " + ToInvariantString(detector.ResolutionFwhmAtMnKa) + ")");
            AppendBodyLine(builder, "\tdp = det.getDetectorProperties()");
            AppendBodyLine(builder, "\tsp = dp.getProperties()");
            // 261030Claude: 窓なしでは何も出力せず、DTSA-II の SDD 既定 (窓なし) と既存のスクリプトを保つ。
            //   厚さは nm から m へ直して渡す。
            //   setWindow は純 Au・Ni の窓層を検出器側の GoldLayer・NickelLayer へ書き写すため、検出器の層より先に窓を付けて後の設定で上書きする。
            AppendWindow(builder, detector);
            AppendBodyLine(builder, "\tsp.setDetectorPosition(JMath.toRadians(" + ToInvariantString(detector.Elevation)
                + "), JMath.toRadians(" + ToInvariantString(detector.Azimuth)
                + "), " + ToInvariantString(detector.SpecimenToDetectorDistance / 1000.0)
                + ", " + ToInvariantString(detector.OptimalWorkingDistance / 1000.0) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.DetectorArea, " + ToInvariantString(detector.DetectorArea) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.DetectorThickness, " + ToInvariantString(detector.SiThickness) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.AluminumLayer, " + ToInvariantString(detector.AluminumLayer) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.GoldLayer, " + ToInvariantString(detector.GoldLayer) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.NickelLayer, " + ToInvariantString(detector.NickelLayer) + ")");
            AppendBodyLine(builder, "\tsp.setNumericProperty(epq.SpectrumProperties.DeadLayer, " + ToInvariantString(detector.DeadLayer) + ")");
            AppendBodyLine(builder, "\tdp.setName(" + ToPythonString(detector.Name) + ")");
            AppendBodyLine(builder, "\treturn det");
            AppendBodyLine(builder);
            AppendBodyLine(builder, "def simulate_coated_substrate(coating, thickness, substrate, det, e0, dose, nTraj):");
            AppendBodyLine(builder, "\torigin = epq.SpectrumUtils.getSamplePosition(det.getProperties())");
            AppendBodyLine(builder, "\tmonte = nm.MonteCarloSS()");
            AppendBodyLine(builder, "\tchamber = monte.getChamber()");
            AppendBodyLine(builder, "\tsc = 1.0e-6");
            AppendBodyLine(builder, "\tmonte.addSubRegion(chamber, coating, nm.MultiPlaneShape.createFilm([0.0, 0.0, -1.0], epu.Math2.plus(origin, [0.0, 0.0, sc * thickness]), sc * thickness))");
            AppendBodyLine(builder, "\tmonte.addSubRegion(chamber, substrate, nm.MultiPlaneShape.createSubstrate([0.0, 0.0, -1.0], epu.Math2.plus(origin, [0.0, 0.0, 2.0 * sc * thickness])))");
            AppendBodyLine(builder, "\tmonte.setBeamEnergy(epq.ToSI.keV(e0))");
            AppendBodyLine(builder, "\tchXR = nm3.CharacteristicXRayGeneration3.create(monte)");
            AppendBodyLine(builder, "\tnm3.XRayTransport3.create(monte, det, chXR)");
            AppendBodyLine(builder, "\tbrXR = nm3.BremsstrahlungXRayGeneration3.create(monte)");
            AppendBodyLine(builder, "\tnm3.XRayTransport3.create(monte, det, brXR)");
            AppendBodyLine(builder, "\tdet.reset()");
            AppendBodyLine(builder, "\tmonte.runMultipleTrajectories(nTraj)");
            AppendBodyLine(builder, "\tspec = det.getSpectrum((dose * 1.0e-9) / (nTraj * epq.PhysicalConstants.ElectronCharge))");
            AppendBodyLine(builder, "\tprops = spec.getProperties()");
            AppendBodyLine(builder, "\tprops.setNumericProperty(epq.SpectrumProperties.LiveTime, dose)");
            AppendBodyLine(builder, "\tprops.setNumericProperty(epq.SpectrumProperties.ProbeCurrent, 1.0)");
            AppendBodyLine(builder, "\tprops.setNumericProperty(epq.SpectrumProperties.BeamEnergy, e0)");
            AppendBodyLine(builder, "\treturn epq.SpectrumUtils.addNoiseToSpectrum(spec, 1.0)");
            AppendBodyLine(builder);
            AppendBodyLine(builder, "def save_spectrum(spec, output_file):");
            AppendBodyLine(builder, "\tfos = FileOutputStream(output_file)");
            AppendBodyLine(builder, "\ttry:");
            AppendBodyLine(builder, "\t\tept.WriteSpectrumAsEMSA1_0.write(spec, fos, ept.WriteSpectrumAsEMSA1_0.Mode.COMPATIBLE)");
            AppendBodyLine(builder, "\tfinally:");
            AppendBodyLine(builder, "\t\tfos.close()");
            AppendBodyLine(builder);
        }

        // 261103Claude: 組成を指定する窓は、端成分と同じく MineraScope が式を読み、元素ごとの個数を addElementByStoiciometry で渡す。
        // 261106Codex: createCompoundと同じ原子数比を渡し、重量比はDTSA-II自身の原子量から求める。
        //   開口率が無ければ今までどおり XRayWindow(1.0) と書く。既製の窓は DTSA-II の名前で作り、その版に無い名前は理由を付けて失敗させる。
        private static void AppendWindow(StringBuilder builder, DetectorProfile detector)
        {
            switch (detector.Window)
            {
                case DetectorWindowKind.Layers:
                    string openArea = detector.WindowOpenAreaFraction is double fraction ? ToInvariantString(fraction) : "1.0";
                    AppendBodyLine(builder, "\twindow = epd.XRayWindow(" + openArea + ")");
                    foreach (var layer in detector.WindowLayers!)
                    {
                        if (!DetectorWindowLayer.TryParseFormula(layer.Formula, out var stoichiometry, out string formulaError))
                            throw new InvalidDataException(formulaError);

                        AppendBodyLine(builder, "\tlayer = epq.Composition()");
                        foreach (var (element, count) in stoichiometry)
                            AppendBodyLine(builder, $"\tlayer.addElementByStoiciometry(epq.Element.{element}, {ToInvariantString(count)})");
                        AppendBodyLine(builder, "\twindow.addLayer(epq.Material(layer, epq.ToSI.gPerCC(" + ToInvariantString(layer.DensityGPerCm3)
                            + ")), " + ToInvariantString(layer.ThicknessNm / 1.0e9) + ")");
                    }

                    AppendBodyLine(builder, "\tdet.setWindow(window)");
                    return;
                case DetectorWindowKind.Preset:
                    // 261103Claude: 例外文に µ などが入ると str(ex) が失敗するため、名前は repr で ASCII にしてから書く。
                    string preset = ToPythonString(detector.WindowPreset!);
                    AppendBodyLine(builder, "\twindow = epd.XRayWindowFactory.createWindow(" + preset + ")");
                    AppendBodyLine(builder, "\tif window is None:");
                    AppendBodyLine(builder, "\t\traise Exception(\"DTSA-II has no X-ray window named \" + repr(" + preset + "))");
                    AppendBodyLine(builder, "\tdet.setWindow(window)");
                    return;
            }
        }

        private static void AppendBodyLine(StringBuilder builder, string line = "") =>
            builder.Append('\t').AppendLine(line);

        // 261103Claude: 非 ASCII・制御文字を含む文字列だけを u"" と \uXXXX で書く。Jython 2 の "" はバイト列なので、日本語の検出器名や
        //   既製の窓の名前 (µ を含む) が Java へ渡るときに化けるため。ASCII だけの文字列は今までと同じ "" で書き、既存のスクリプトを変えない。
        private static string ToPythonString(string value)
        {
            string escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            // 261105Claude: 印字できない文字の判定を 1 つにし、置き換えが無ければ今までと同じ "" で書く。
            string literal = Regex.Replace(escaped, "[^ -~]", static match => "\\u" + ((int)match.Value[0]).ToString("x4", CultureInfo.InvariantCulture));
            return (literal == escaped ? "\"" : "u\"") + literal + "\"";
        }

        private static string ToInvariantString(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
