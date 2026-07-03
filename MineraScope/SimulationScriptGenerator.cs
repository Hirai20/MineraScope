using System.Globalization;
using System.Linq;
using System.Text;

namespace MineraScope
{
    // 260626Codex: Generate DTSA-II no-GUI scripts from MineraScope detector profiles, without findDetector/Derby DB access.
    internal sealed class SimulationScriptGenerator
    {
        private const double DefaultDensity = 3;
        // 260626Codex: Match DTSA-II mcSimulate3's default without importing the missing msi Lib\dtsa2 package.
        private const int DefaultTrajectoryCount = 1000;

        public string Generate(SimulationProperty property, int? parallelIndex = null)
        {
            StringBuilder builder = new();
            (string ElementName, double Weight)[][] atoms = property.Atoms1;
            string[] outputFiles = property.OutputFiles;
            bool jitterCarbon = property.CarbonCoatThicknessJitterPercent > 0;
            DetectorProfile detector = DetectorProfile.CreateWithDefaults(property.DetectorProfile, property.DetectorName);

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
            if (jitterCarbon)
                AppendBodyLine(builder, "import random");
            AppendBodyLine(builder);
            AppendJavaApiHelpers(builder, detector);

            AppendBodyLine(builder, "output_dir = " + ToPythonString(property.OutputFolder));
            AppendBodyLine(builder, "if not os.path.isdir(output_dir):");
            AppendBodyLine(builder, "\tos.makedirs(output_dir)");
            AppendBodyLine(builder, "det = create_detector()");
            AppendBodyLine(builder, "e0 = " + ToInvariantString(property.BeamEnergy));
            AppendBodyLine(builder, "dose = " + ToInvariantString(property.ProbeCurrent) + " * " + ToInvariantString(property.LiveTime));
            AppendBodyLine(builder, "cThickness = " + ToInvariantString(property.CarbonCoatThickness));
            if (jitterCarbon)
                AppendBodyLine(builder, "cJitter = " + ToInvariantString(property.CarbonCoatThicknessJitterPercent / 100));
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
            AppendForHeader(builder, atoms[0].Select(static x => x.ElementName).ToArray());
            AppendSimulationLoopBody(builder, atoms[0].Select(static x => x.ElementName).ToArray(), jitterCarbon);
            AppendBodyLine(builder, "sys.exit(0)");
            builder.AppendLine("except Exception, ex:");
            builder.AppendLine("\tSystem.err.println(\"MINERASCOPE_ERROR|\" + str(ex))");
            builder.AppendLine("\tSystem.err.flush()");
            builder.AppendLine("\tsys.exit(1)");
            return builder.ToString();
        }

        // 260626Codex: Keep the generated loop readable while preserving Python indentation under the top-level try.
        private static void AppendForHeader(StringBuilder builder, string[] elementNames)
        {
            builder.Append('\t').Append("for idx, (");
            builder.Append(string.Join(", ", elementNames.Select(static element => $"{element}_weight")));
            builder.AppendLine(") in enumerate(Weights):");
        }

        private static void AppendSimulationLoopBody(StringBuilder builder, string[] elementNames, bool jitterCarbon)
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
            if (jitterCarbon)
            {
                AppendBodyLine(builder, "\teffectiveThickness = max(0.0, cThickness * (1.0 + random.uniform(-cJitter, cJitter)))");
                AppendBodyLine(builder, "\tsd = simulate_coated_substrate(carbonCoating, effectiveThickness, material, det, e0, dose, " + DefaultTrajectoryCount + ")");
            }
            else
                AppendBodyLine(builder, "\tsd = simulate_coated_substrate(carbonCoating, cThickness, material, det, e0, dose, " + DefaultTrajectoryCount + ")");
            AppendBodyLine(builder, "\toutput_file = os.path.join(output_dir, FileNames[idx])");
            AppendBodyLine(builder, "\tepq.SpectrumUtils.rename(sd, FileNames[idx])");
            AppendBodyLine(builder, "\tsave_spectrum(sd, output_file)");
            AppendBodyLine(builder, "\tSystem.out.println(\"MINERASCOPE_SPECTRUM_SAVED|{0}|{1}\".format(idx + 1, output_file))");
            AppendBodyLine(builder, "\tSystem.out.flush()");
        }

        // 260626Codex: Embed the editable detector profile into the script so parallel jobs never open DTSA-II's detector DB.
        private static void AppendJavaApiHelpers(StringBuilder builder, DetectorProfile detector)
        {
            AppendBodyLine(builder, "def create_detector():");
            AppendBodyLine(builder, "\tdet = epd.EDSDetector.createSDDDetector(" + detector.ChannelCount.ToString(CultureInfo.InvariantCulture)
                + ", " + ToInvariantString(detector.ChannelWidth)
                + ", " + ToInvariantString(detector.ZeroOffset)
                + ", " + ToInvariantString(detector.ResolutionFwhmAtMnKa) + ")");
            AppendBodyLine(builder, "\tdp = det.getDetectorProperties()");
            AppendBodyLine(builder, "\tsp = dp.getProperties()");
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

        private static void AppendBodyLine(StringBuilder builder, string line = "") =>
            builder.Append('\t').AppendLine(line);

        private static string ToPythonString(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string ToInvariantString(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);
    }
}
