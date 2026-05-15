using System;
using System.Collections.Generic;
using System.Text;

namespace MineraScope
{
    public class PtsParameter
    {
        public string Microscope { get; set; }

        public string Detector { get; set; }

        public int ChannelCount { get; set; }

        public double ChannelResolution { get; set; }

        public string ProcessTimeName { get; set; }

        public double ElectricNoise { get; set; }

        public double FanoFactor { get; set; }

        public double[] ExtendedCoefficients { get; set; }

        public short ChannelOffset { get; set; }

        public short DigitalLLD { get; set; }

        public short Sweep { get; set; }

        public string[] Elements { get; set; }

        public double Voltage { get; set; }

        public double Current { get; set; }

        public double CoefA { get; set; }

        public double CoefB { get; set; }

        public double Dwell { get; set; }

        public bool LTscan { get; set; }

        public double ScanSize { get; set; }

        public int Mag { get; set; }

        public double InsertionDis { get; set; }

        public double TakeoffAngle { get; set; }

        public double SensorArea { get; set; }

        public double PixelSize { get; set; }

        public PtsParameter()
        {
            Microscope = "";
            Detector = "";
            ChannelCount = 2048;
            ChannelResolution = 0.01;
            ProcessTimeName = "T1";
            ElectricNoise = 62.0;
            FanoFactor = 0.14;
            ExtendedCoefficients = null;
            ChannelOffset = 0;
            DigitalLLD = 0;
            Sweep = 0;
            Dwell = 0.1;
            Elements = null;
            Voltage = 10.0;
            Current = 1.0;
            LTscan = false;
            InsertionDis = 10.0;
            TakeoffAngle = 25.0;
            SensorArea = 50.0;
            PixelSize = 1.0;
        }
    }
}
