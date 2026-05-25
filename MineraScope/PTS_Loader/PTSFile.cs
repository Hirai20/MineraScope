using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace MineraScope
{
    public struct PtsFileHeader
    {
        public uint Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
        public string Id;

        public short Version;

        public short Type;

        public uint AttrOffset;

        public uint AttrSize;

        public uint PttdOffset;

        public uint PttdSize;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string GroupName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Memo;

        public double Date;
    }

    public class PTSFile : IDisposable
    {
        public delegate void ProgressCallBack(int value);

        public delegate void ProgressCallBackD(double val);

        public int ChannelCount = 2048;

        private BufferedStream stream;

        private BinaryReader reader;

        private int imageWidth = 256;

        private int imageHeight = 192;

        private long[] spectrum;

        private byte[,] image1;

        private PtsFileHeader header;

        private Itemize attributeItemize;

        private PtsParameter parameter;

        private long spectrumMaxValue;

        private int realTime;

        private int liveTime;

        private int sweepCount;

        private int frameNumbers;

        private long xcounts;

        private string[] xlines;

        private bool disposedValue;

        public LongArray<int> SpectrumCube;

        private ProgressDialog pd = new ProgressDialog();

        public PtsFileHeader Header => header;

        public bool HasHeader => header.Size != 0;

        public Itemize AttributeItemize => attributeItemize;

        public PtsParameter Parameter
        {
            get
            {
                return parameter;
            }
            set
            {
                parameter = value;
            }
        }

        public double BeamEnergy => parameter.Voltage;

        public double BeamCurrent => parameter.Current;

        public long[] Spectrum => spectrum;

        public int Width => imageWidth;

        public int Height => imageHeight;

        public byte[,] Image1 => image1;

        public long SpectrumMaxValue => spectrumMaxValue;

        public double RealTime => (double)realTime * 0.01;

        public double LiveTime => (double)liveTime * 0.01;

        public int FrameNumbers => frameNumbers;

        public int TotalFrames => sweepCount;

        public long Xcounts => xcounts;

        public double Dwell => parameter.Dwell;

        public bool LiveTimeScan => parameter.LTscan;

        public string[] Xray_lines => xlines;

        public double PixelSize => parameter.PixelSize;

        // 260526Claude: 読み取りに使えるチャンネル数（PTS属性とファイル側の小さい方）。クリック/グリッド/ブロック読みと検証で共有する。
        internal int UsableChannelCount => Math.Min(ChannelCount, parameter.ChannelCount);

        public string StrHeader
        {
            get
            {
                if (!HasHeader)
                {
                    return "No Header";
                }
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append("Header.Size = " + header.Size + "\n");
                stringBuilder.Append("Header.Id = " + header.Id + "\n");
                stringBuilder.Append("Header.Version = " + header.Version + "\n");
                stringBuilder.Append("Header.Type = " + header.Type + "\n");
                stringBuilder.Append("Header.AttrOffset = " + header.AttrOffset + "\n");
                stringBuilder.Append("Header.AttrSize = " + header.AttrSize + "\n");
                stringBuilder.Append("Header.PttdOffset = " + header.PttdOffset + "\n");
                stringBuilder.Append("Header.PttdSize = " + header.PttdSize + "\n");
                stringBuilder.Append("Header.GroupName = " + header.GroupName + "\n");
                stringBuilder.Append("Header.Memo = " + header.Memo + "\n");
                stringBuilder.Append("Header.Date = " + header.Date);
                return stringBuilder.ToString();
            }
        }

        public string StrInfo
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append(StrHeader);
                if (HasHeader)
                {
                    stringBuilder.Append("\n");
                    stringBuilder.Append(attributeItemize.ToTreeString());
                }
                return stringBuilder.ToString();
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposedValue)
            {
                return;
            }
            if (disposing)
            {
                if (reader != null)
                {
                    reader.Dispose();
                }
                else if (stream != null)
                {
                    stream.Dispose();
                }
                image1 = null;
                attributeItemize = null;
            }
            disposedValue = true;
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        ~PTSFile()
        {
            Dispose(disposing: false);
        }

        public PTSFile(string fname)
            : this(fname, 1048576)
        {
        }

        public PTSFile(string fname, int buffer_size)
        {
            if (reader != null)
            {
                Close();
            }
            BufferedStream bufferedStream = new BufferedStream(new FileStream(fname, FileMode.Open, FileAccess.ReadWrite), buffer_size);
            init(bufferedStream);
        }

        public void Close()
        {
            if (SpectrumCube != null)
            {
                SpectrumCube.Dispose();
            }
            if (reader != null)
            {
                reader.Dispose();
            }
            if (stream != null)
            {
                stream.Dispose();
            }
        }

        private void init(BufferedStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }
            if (!stream.CanRead)
            {
                throw new ArgumentException("ストリームが読み込みをサポートしていません。", "stream");
            }
            this.stream = stream;
            reader = new BinaryReader(this.stream);
            spectrum = new long[ChannelCount];
            sweepCount = 1;
            readHeader();
            readAttribute();
        }

        private void readHeader()
        {
            byte[] array = reader.ReadBytes(Marshal.SizeOf(typeof(PtsFileHeader)));
            GCHandle gCHandle = GCHandle.Alloc(array, GCHandleType.Pinned);
            try
            {
                header = (PtsFileHeader)Marshal.PtrToStructure(Marshal.UnsafeAddrOfPinnedArrayElement(array, 0), typeof(PtsFileHeader));
            }
            finally
            {
                gCHandle.Free();
            }
            if (header.Id != "PTTDFILE")
            {
                header = default(PtsFileHeader);
            }
        }

        private void readAttribute()
        {
            if (!HasHeader)
            {
                return;
            }
            stream.Seek(header.AttrOffset, SeekOrigin.Begin);
            attributeItemize = new Itemize(reader);
            parameter = GetParameterFromAttribute();
            ChannelCount = parameter.ChannelCount;
            sweepCount = parameter.Sweep;
            Itemize childItem = AttributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\Meas Cond\\Pixels");
            if (childItem != null)
            {
                string[] array = childItem.GetString().Split('x');
                if (array.Length == 2)
                {
                    imageWidth = int.Parse(array[0]);
                    imageHeight = int.Parse(array[1]);
                }
            }
            else
            {
                childItem = AttributeItemize.GetChildItem("PTTD Cond\\Meas Cond\\CONDPAGE0\\ITC\\TIRes");
                if (childItem != null)
                {
                    string[] array2 = childItem.GetString().Split('x');
                    if (array2.Length == 2)
                    {
                        imageWidth = int.Parse(array2[0]);
                        imageHeight = int.Parse(array2[1]);
                    }
                }
            }
            Parameter.PixelSize = Parameter.ScanSize / (double)Parameter.Mag / (double)Width * 1000000.0;
            childItem = AttributeItemize.GetChildItem("PTTD Cond\\Meas Cond\\CONDPAGE0\\MiniOBJs");
            if (childItem != null)
            {
                string[] strArray = null;
                if (childItem.GetMultiString(ref strArray))
                {
                    if (strArray != null && strArray.Length != 0)
                    {
                        int num = 0;
                        for (int i = 0; i < strArray.Length; i++)
                        {
                            if (strArray[i].Contains(" K") || strArray[i].Contains(" L") || strArray[i].Contains(" M"))
                            {
                                num++;
                            }
                        }
                        xlines = new string[num];
                        num = 0;
                        for (int j = 0; j < strArray.Length; j++)
                        {
                            if (strArray[j].Contains(" K") || strArray[j].Contains(" L") || strArray[j].Contains(" M"))
                            {
                                xlines[num] = strArray[j];
                                num++;
                            }
                        }
                    }
                }
                else
                {
                    xlines = null;
                }
            }
        }

        public PtsParameter GetParameterFromAttribute()
        {
            PtsParameter ptsParameter = new PtsParameter();
            try
            {
                List<Itemize> firstItems = AttributeItemize.GetChildItem("PTTD Param\\Params").GetFirstItems("PARAMPAGE");
                if (firstItems != null)
                {
                    ptsParameter.ChannelCount = firstItems[0].GetInt32("NumCH", ptsParameter.ChannelCount);
                    ptsParameter.ChannelResolution = firstItems[0].GetDouble("CH Res", ptsParameter.ChannelResolution);
                    ptsParameter.ElectricNoise = firstItems[0].GetDouble("E Noise", ptsParameter.ElectricNoise);
                    ptsParameter.FanoFactor = firstItems[0].GetDouble("Fano F", ptsParameter.FanoFactor);
                    string text = (ptsParameter.ProcessTimeName = attributeItemize.GetChildItem("EDS Data\\AnalyzableMap MeasData\\Meas Cond\\Tpl").GetString());
                    Itemize itemize = null;
                    foreach (Itemize item in firstItems)
                    {
                        itemize = item.GetChildItem("Tpl\\" + text);
                        if (itemize != null)
                        {
                            break;
                        }
                    }
                    if (itemize != null)
                    {
                        ptsParameter.ChannelOffset = itemize.GetInt16("DigZ", ptsParameter.ChannelOffset);
                        ptsParameter.DigitalLLD = itemize.GetInt16("DigL", ptsParameter.DigitalLLD);
                        ptsParameter.ExtendedCoefficients = itemize.GetDoubleArray("ExCoef", ptsParameter.ExtendedCoefficients);
                    }
                }
                ptsParameter.Sweep = AttributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\Doc\\Sweep").GetInt16();
                ptsParameter.Elements = AttributeItemize.GetChildItem("PTTD Cond\\Meas Cond\\CONDPAGE0\\MiniOBJs").GetStringArray();
                ptsParameter.LTscan = AttributeItemize.GetBoolean("PTTD Cond\\Meas Cond\\CONDPAGE0\\LTDwellT", defaultValue: false);
                ptsParameter.ScanSize = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\ScanSize").GetSingle();
                ptsParameter.Microscope = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\SEM").GetString();
                ptsParameter.Detector = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\DetT").GetString();
                ptsParameter.InsertionDis = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\InsD").GetSingle();
                ptsParameter.TakeoffAngle = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\TakeAng").GetSingle();
                ptsParameter.SensorArea = AttributeItemize.GetChildItem("PTTD Param\\Params\\PARAMPAGE0\\ValidSize").GetSingle();
                ptsParameter.Voltage = AttributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\MeasCond\\AccKV").GetSingle();
                ptsParameter.Current = AttributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\MeasCond\\AccNA").GetSingle();
                ptsParameter.Mag = AttributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\MeasCond\\Mag").GetInt32();
                ptsParameter.CoefA = attributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\Doc").GetDouble("CoefA", 0.01);
                ptsParameter.CoefB = attributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\Doc").GetDouble("CoefB", 0.0);
                ptsParameter.Dwell = attributeItemize.GetChildItem("PTTD Data\\AnalyzableMap MeasData\\Doc").GetDouble("DwellTime(msec)", 0.1);
                if (ptsParameter.Voltage / ptsParameter.ChannelResolution > (double)ptsParameter.ChannelCount)
                {
                    ChannelCount = ptsParameter.ChannelCount;
                }
                else
                {
                    ChannelCount = (int)(ptsParameter.Voltage / ptsParameter.ChannelResolution);
                }
            }
            catch
            {
                throw new InvalidDataException("アトリビュート アイテムが無効なデータ形式です。");
            }
            return ptsParameter;
        }

        public int[,,] GetSpectrumCube(int startFrame, int frame_cnt)
        {
            BeginRead();
            if (SpectrumCube != null)
            {
                SpectrumCube.Dispose();
            }
            int[,,] array = new int[Width, Height, ChannelCount];
            if (startFrame < 0)
            {
                return null;
            }
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            int num4 = 0;
            int num5 = 0;
            bool flag = true;
            int num6 = 4096 / Width;
            int num7 = 0;
            int num8 = 0;
            long num9 = 0L;
            num7 = 0;
            num8 = 0;
            num9 = 0L;
            num3 = 0;
            long offset = ((header.Id != null) ? header.PttdOffset : 0);
            if (startFrame == 0)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                stream.Seek(offset, SeekOrigin.Begin);
                int num10 = 0;
                flag = true;
                while (flag)
                {
                    ushort num11 = reader.ReadUInt16();
                    if ((num11 & 0xF000) == 36864 && (num11 & 0xFFF) == 0)
                    {
                        num10++;
                        if (num10 - 1 == startFrame)
                        {
                            flag = false;
                            stream.Seek(-2L, SeekOrigin.Current);
                        }
                    }
                }
            }
            try
            {
                flag = true;
                while (flag)
                {
                    ushort num12 = reader.ReadUInt16();
                    switch (num12 & 0xF000)
                    {
                        case 24576:
                            num7++;
                            break;
                        case 28672:
                            liveTime++;
                            num8++;
                            break;
                        case 32768:
                            if (num4 != (num12 & 0xFFF))
                            {
                                num = ((num4 == 0 || (num12 & 0xFFF) != 0) ? ((num12 & 0xFFF) / num6) : 0);
                                num4 = num12 & 0xFFF;
                            }
                            break;
                        case 36864:
                            if (num5 == (num12 & 0xFFF))
                            {
                                break;
                            }
                            if (num5 != 0 && (num12 & 0xFFF) == 0)
                            {
                                num2 = 0;
                                num3++;
                                if (num3 == frame_cnt)
                                {
                                    flag = false;
                                }
                            }
                            else
                            {
                                num2 = (num12 & 0xFFF) / num6;
                            }
                            num = 0;
                            num5 = num12 & 0xFFF;
                            break;
                        case 45056:
                            {
                                int num13 = (num12 & 0xFFF) - parameter.ChannelOffset;
                                if (num13 > parameter.DigitalLLD && num13 < parameter.ChannelCount)
                                {
                                    array[num, num2, num13]++;
                                    num9++;
                                }
                                break;
                            }
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }
            realTime = num7;
            liveTime = num8;
            frameNumbers = num3;
            xcounts = num9;
            return (int[,,])array.Clone();
        }

        public LongArray<int> GetSpectrumCubeT(int startFrame, int frame_cnt)
        {
            BeginRead();
            if (SpectrumCube != null)
            {
                SpectrumCube.Dispose();
            }
            LongArray<int> longArray = new LongArray<int>((long)Height * (long)Width * ChannelCount);
            if (startFrame < 0)
            {
                return null;
            }
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            int num4 = 0;
            int num5 = 0;
            bool flag = true;
            int num6 = 4096 / Width;
            int num7 = 0;
            int num8 = 0;
            long num9 = 0L;
            num7 = 0;
            num8 = 0;
            num9 = 0L;
            num3 = 0;
            long offset = ((header.Id != null) ? header.PttdOffset : 0);
            if (startFrame == 0)
            {
                stream.Seek(offset, SeekOrigin.Begin);
            }
            else
            {
                stream.Seek(offset, SeekOrigin.Begin);
                int num10 = 0;
                flag = true;
                while (flag)
                {
                    ushort num11 = reader.ReadUInt16();
                    if ((num11 & 0xF000) == 36864 && (num11 & 0xFFF) == 0)
                    {
                        num10++;
                        if (num10 - 1 == startFrame)
                        {
                            flag = false;
                            stream.Seek(-2L, SeekOrigin.Current);
                        }
                    }
                }
            }
            try
            {
                flag = true;
                while (flag)
                {
                    ushort num12 = reader.ReadUInt16();
                    switch (num12 & 0xF000)
                    {
                        case 24576:
                            num7++;
                            break;
                        case 28672:
                            liveTime++;
                            num8++;
                            break;
                        case 32768:
                            if (num4 != (num12 & 0xFFF))
                            {
                                num = ((num4 == 0 || (num12 & 0xFFF) != 0) ? ((num12 & 0xFFF) / num6) : 0);
                                num4 = num12 & 0xFFF;
                            }
                            break;
                        case 36864:
                            if (num5 == (num12 & 0xFFF))
                            {
                                break;
                            }
                            if (num5 != 0 && (num12 & 0xFFF) == 0)
                            {
                                num2 = 0;
                                num3++;
                                pd.Value = (int)((double)num3 / (double)frame_cnt * 80.0);
                                if (pd.Canceled)
                                {
                                    flag = false;
                                }
                                if (num3 == frame_cnt)
                                {
                                    flag = false;
                                }
                            }
                            else
                            {
                                num2 = (num12 & 0xFFF) / num6;
                            }
                            num = 0;
                            num5 = num12 & 0xFFF;
                            break;
                        case 45056:
                            {
                                int num13 = (num12 & 0xFFF) - parameter.ChannelOffset;
                                if (num13 > parameter.DigitalLLD && num13 < parameter.ChannelCount)
                                {
                                    longArray[(long)(num2 * Width + num) * (long)ChannelCount + num13]++;
                                    num9++;
                                }
                                break;
                            }
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }
            realTime = num7;
            liveTime = num8;
            frameNumbers = num3;
            xcounts = num9;
            return longArray;
        }

        public IntPtr GetSpectrumCubePtr(int startFrame, int frame_cnt)
        {
            SpectrumCube = GetSpectrumCubeT(startFrame, frame_cnt);
            return SpectrumCube.GetIntPtr();
        }

        // 260522Codex: Sum all PTTD X-ray events inside the selected clamped bin window in one stream pass.
        internal PtsPixelSpectrum? TryReadBinnedPixelSpectrum(int targetX, int targetY, int binSize)
        {
            if (!HasHeader || Width <= 0 || Height <= 0 || ChannelCount <= 0 || Width > 4096 || binSize <= 0)
                return null;

            if (targetX < 0 || targetX >= Width || targetY < 0 || targetY >= Height)
                return null;

            int beforeCenter = binSize / 2;
            int afterCenter = binSize - beforeCenter - 1;
            int binLeft = Math.Max(0, targetX - beforeCenter);
            int binTop = Math.Max(0, targetY - beforeCenter);
            int binRight = Math.Min(Width - 1, targetX + afterCenter);
            int binBottom = Math.Min(Height - 1, targetY + afterCenter);

            int coordinateUnit = 4096 / Width;
            if (coordinateUnit <= 0)
                return null;

            // 260520Codex: PTS 属性とファイル側の小さい方を読み取り対象チャンネル数にします。
            // 260526Claude: グリッド/ブロック読みと同じ算出を共有プロパティに寄せる。
            int usableChannelCount = UsableChannelCount;
            if (usableChannelCount <= 0)
                return null;

            int[] counts = new int[usableChannelCount];
            BeginRead();

            int x = 0;
            int y = 0;
            int previousX = 0;
            int previousY = 0;
            int realTimeRecords = 0;
            int liveTimeRecords = 0;
            int completedFrames = 0;
            long totalXrayCounts = 0;
            long endOffset = stream.Length;
            if (header.Id != null && header.PttdSize > 0)
                endOffset = Math.Min((long)header.PttdOffset + header.PttdSize, stream.Length);

            try
            {
                while (stream.Position + sizeof(ushort) <= endOffset)
                {
                    ushort record = reader.ReadUInt16();
                    int value = record & 0xFFF;
                    switch (record & 0xF000)
                    {
                        case 0x6000:
                            realTimeRecords++;
                            break;
                        case 0x7000:
                            liveTimeRecords++;
                            break;
                        case 0x8000:
                            if (previousX != value)
                            {
                                x = previousX == 0 || value != 0 ? value / coordinateUnit : 0;
                                previousX = value;
                            }
                            break;
                        case 0x9000:
                            if (previousY == value)
                                break;

                            if (previousY != 0 && value == 0)
                            {
                                y = 0;
                                completedFrames++;
                            }
                            else
                                y = value / coordinateUnit;

                            x = 0;
                            previousY = value;
                            break;
                        case 0xB000:
                            if (x < binLeft || x > binRight || y < binTop || y > binBottom)
                                break;

                            // 260526Claude: チャンネル採否を共有 helper に寄せ、グリッド/ブロック読みと規則を一致させる。
                            if (!TryAcceptChannel(value, usableChannelCount, out int channel))
                                break;

                            counts[channel]++;
                            totalXrayCounts++;
                            break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }

            realTime = realTimeRecords;
            liveTime = liveTimeRecords;
            frameNumbers = completedFrames;
            xcounts = totalXrayCounts;

            return new PtsPixelSpectrum(
                targetX,
                targetY,
                usableChannelCount,
                parameter.CoefB,
                parameter.CoefA,
                counts,
                binLeft,
                binTop,
                binRight,
                binBottom,
                binSize);
        }

        // 260526Claude: グリッド集計を1回チェックする間隔（レコード数）。進捗報告とキャンセル確認をまとめて行う。
        private const int GridReadCheckInterval = 1 << 20;

        // 260526Claude: 0xB000 レコード値を採用チャンネルへ変換し LLD/範囲で採否を判定する（クリック/グリッド/ブロックで共有）。
        private bool TryAcceptChannel(int value, int usableChannelCount, out int channel)
        {
            channel = value - parameter.ChannelOffset;
            return channel > parameter.DigitalLLD && channel < usableChannelCount;
        }

        // 260526Claude: 原点基準の非重複ブロック境界（右下端は画像端でクランプ）。グリッド集計の x/binSize と数学的に対になる。
        private (int Left, int Top, int Right, int Bottom) GetBlockBounds(int blockX, int blockY, int binSize)
        {
            int left = blockX * binSize;
            int top = blockY * binSize;
            int right = Math.Min((blockX + 1) * binSize - 1, Width - 1);
            int bottom = Math.Min((blockY + 1) * binSize - 1, Height - 1);
            return (left, top, right, bottom);
        }

        // 260526Claude: PTS イベント列を1パス走査し、非重複ブロックごとの全チャンネルカウントを作る（マップ生成用）。
        // 大配列確保前のチャンネル数・メモリ検証は呼び出し側 (workflow) が行う前提。キャンセルは OperationCanceledException で抜ける。
        internal PtsBinnedSpectrumGrid? TryReadBinnedSpectrumGrid(int binSize, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (!HasHeader || Width <= 0 || Height <= 0 || ChannelCount <= 0 || Width > 4096 || binSize <= 0)
                return null;

            int coordinateUnit = 4096 / Width;
            if (coordinateUnit <= 0)
                return null;

            int usableChannelCount = UsableChannelCount;
            if (usableChannelCount <= 0)
                return null;

            int gridWidth = (Width + binSize - 1) / binSize;
            int gridHeight = (Height + binSize - 1) / binSize;

            long length = (long)gridWidth * gridHeight * usableChannelCount;
            if (length > int.MaxValue)
                throw new InvalidOperationException("ビニング格子が大きすぎて確保できません。binを大きくしてください。");

            int[] counts = new int[(int)length];
            BeginRead();

            int x = 0;
            int y = 0;
            int previousX = 0;
            int previousY = 0;
            int realTimeRecords = 0;
            int liveTimeRecords = 0;
            int completedFrames = 0;
            long totalXrayCounts = 0;
            long endOffset = stream.Length;
            if (header.Id != null && header.PttdSize > 0)
                endOffset = Math.Min((long)header.PttdOffset + header.PttdSize, stream.Length);

            long startPosition = stream.Position;
            long span = Math.Max(1, endOffset - startPosition);
            int sinceCheck = 0;

            try
            {
                while (stream.Position + sizeof(ushort) <= endOffset)
                {
                    if (++sinceCheck >= GridReadCheckInterval)
                    {
                        sinceCheck = 0;
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report((double)(stream.Position - startPosition) / span);
                    }

                    ushort record = reader.ReadUInt16();
                    int value = record & 0xFFF;
                    switch (record & 0xF000)
                    {
                        case 0x6000:
                            realTimeRecords++;
                            break;
                        case 0x7000:
                            liveTimeRecords++;
                            break;
                        case 0x8000:
                            if (previousX != value)
                            {
                                x = previousX == 0 || value != 0 ? value / coordinateUnit : 0;
                                previousX = value;
                            }
                            break;
                        case 0x9000:
                            if (previousY == value)
                                break;

                            if (previousY != 0 && value == 0)
                            {
                                y = 0;
                                completedFrames++;
                            }
                            else
                                y = value / coordinateUnit;

                            x = 0;
                            previousY = value;
                            break;
                        case 0xB000:
                            // 260526Claude: coordinateUnit が Width を割り切らない場合に x/y が範囲外になり得るため、ブロック算出前に必ず弾く。
                            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                                break;

                            if (!TryAcceptChannel(value, usableChannelCount, out int channel))
                                break;

                            int blockIndex = (y / binSize) * gridWidth + x / binSize;
                            counts[blockIndex * usableChannelCount + channel]++;
                            totalXrayCounts++;
                            break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }

            realTime = realTimeRecords;
            liveTime = liveTimeRecords;
            frameNumbers = completedFrames;
            xcounts = totalXrayCounts;
            progress?.Report(1.0);

            return new PtsBinnedSpectrumGrid(
                gridWidth,
                gridHeight,
                binSize,
                usableChannelCount,
                counts);
        }

        // 260526Claude: マップクリック詳細用に、指定ブロックだけをグリッドと同一の原点基準で1パス再読みする。
        internal PtsPixelSpectrum? TryReadBinnedBlockSpectrum(int blockX, int blockY, int binSize)
        {
            if (!HasHeader || Width <= 0 || Height <= 0 || ChannelCount <= 0 || Width > 4096 || binSize <= 0)
                return null;

            if (blockX < 0 || blockY < 0)
                return null;

            var (binLeft, binTop, binRight, binBottom) = GetBlockBounds(blockX, blockY, binSize);
            if (binLeft >= Width || binTop >= Height)
                return null;

            int coordinateUnit = 4096 / Width;
            if (coordinateUnit <= 0)
                return null;

            int usableChannelCount = UsableChannelCount;
            if (usableChannelCount <= 0)
                return null;

            int[] counts = new int[usableChannelCount];
            BeginRead();

            int x = 0;
            int y = 0;
            int previousX = 0;
            int previousY = 0;
            int realTimeRecords = 0;
            int liveTimeRecords = 0;
            int completedFrames = 0;
            long totalXrayCounts = 0;
            long endOffset = stream.Length;
            if (header.Id != null && header.PttdSize > 0)
                endOffset = Math.Min((long)header.PttdOffset + header.PttdSize, stream.Length);

            try
            {
                while (stream.Position + sizeof(ushort) <= endOffset)
                {
                    ushort record = reader.ReadUInt16();
                    int value = record & 0xFFF;
                    switch (record & 0xF000)
                    {
                        case 0x6000:
                            realTimeRecords++;
                            break;
                        case 0x7000:
                            liveTimeRecords++;
                            break;
                        case 0x8000:
                            if (previousX != value)
                            {
                                x = previousX == 0 || value != 0 ? value / coordinateUnit : 0;
                                previousX = value;
                            }
                            break;
                        case 0x9000:
                            if (previousY == value)
                                break;

                            if (previousY != 0 && value == 0)
                            {
                                y = 0;
                                completedFrames++;
                            }
                            else
                                y = value / coordinateUnit;

                            x = 0;
                            previousY = value;
                            break;
                        case 0xB000:
                            if (x < binLeft || x > binRight || y < binTop || y > binBottom)
                                break;

                            if (!TryAcceptChannel(value, usableChannelCount, out int channel))
                                break;

                            counts[channel]++;
                            totalXrayCounts++;
                            break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }

            realTime = realTimeRecords;
            liveTime = liveTimeRecords;
            frameNumbers = completedFrames;
            xcounts = totalXrayCounts;

            return new PtsPixelSpectrum(
                binLeft,
                binTop,
                usableChannelCount,
                parameter.CoefB,
                parameter.CoefA,
                counts,
                binLeft,
                binTop,
                binRight,
                binBottom,
                binSize);
        }

        //private bool CreateStringsAttribute(long objectId, string title, IEnumerable<string> strs)
        //{
        //    long num = 0L;
        //    long num2 = 0L;
        //    long num3 = 0L;
        //    try
        //    {
        //        int num4 = strs.Count();
        //        int rank = 0;
        //        if (num4 > 1)
        //        {
        //            rank = 1;
        //        }
        //        num = H5S.create_simple(rank, new ulong[1] { (ulong)num4 }, null);
        //        num2 = H5T.create(H5T.class_t.STRING, H5T.VARIABLE);
        //        H5T.set_cset(num2, H5T.cset_t.UTF8);
        //        H5T.set_strpad(num2, H5T.str_t.NULLTERM);
        //        num3 = H5A.create(objectId, title, num2, num, 0L, 0L);
        //        GCHandle[] array = new GCHandle[num4];
        //        IntPtr[] array2 = new IntPtr[num4];
        //        int num5 = 0;
        //        foreach (string str in strs)
        //        {
        //            array[num5] = GCHandle.Alloc(Encoding.UTF8.GetBytes(str), GCHandleType.Pinned);
        //            array2[num5] = array[num5].AddrOfPinnedObject();
        //            num5++;
        //        }
        //        GCHandle gCHandle = GCHandle.Alloc(array2, GCHandleType.Pinned);
        //        H5A.write(num3, num2, gCHandle.AddrOfPinnedObject());
        //        gCHandle.Free();
        //        for (int i = 0; i < num4; i++)
        //        {
        //            array[i].Free();
        //        }
        //        return true;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //    finally
        //    {
        //        if (num3 != 0L)
        //        {
        //            H5A.close(num3);
        //        }
        //        if (num2 != 0L)
        //        {
        //            H5T.close(num2);
        //        }
        //        if (num != 0L)
        //        {
        //            H5S.close(num);
        //        }
        //    }
        //}

        //private bool CreateDoubleAttribute(long objectId, string title, double val)
        //{
        //    long num = 0L;
        //    long num2 = 0L;
        //    try
        //    {
        //        double[] source = new double[1] { val };
        //        IntPtr intPtr = Marshal.AllocHGlobal(8);
        //        Marshal.Copy(source, 0, intPtr, 1);
        //        num = H5S.create(H5S.class_t.SCALAR);
        //        num2 = H5A.create(objectId, title, H5T.IEEE_F64LE, num, 0L, 0L);
        //        H5A.write(num2, H5T.NATIVE_DOUBLE, intPtr);
        //        Marshal.FreeHGlobal(intPtr);
        //        return true;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //    finally
        //    {
        //        if (num2 != 0L)
        //        {
        //            H5A.close(num2);
        //        }
        //        if (num != 0L)
        //        {
        //            H5S.close(num);
        //        }
        //    }
        //}

        //private bool CreateIntAttribute(long objectId, string title, int val)
        //{
        //    long num = 0L;
        //    long num2 = 0L;
        //    try
        //    {
        //        int[] source = new int[1] { val };
        //        IntPtr intPtr = Marshal.AllocHGlobal(4);
        //        Marshal.Copy(source, 0, intPtr, 1);
        //        num = H5S.create(H5S.class_t.SCALAR);
        //        num2 = H5A.create(objectId, title, H5T.NATIVE_INT32, num, 0L, 0L);
        //        H5A.write(num2, H5T.NATIVE_INT32, intPtr);
        //        Marshal.FreeHGlobal(intPtr);
        //        return true;
        //    }
        //    catch (Exception)
        //    {
        //        return false;
        //    }
        //    finally
        //    {
        //        if (num2 != 0L)
        //        {
        //            H5A.close(num2);
        //        }
        //        if (num != 0L)
        //        {
        //            H5S.close(num);
        //        }
        //    }
        //}

        //private bool Save2HDF5(ref LongArray<int> spectra, string path)
        //{
        //    bool result = true;
        //    try
        //    {
        //        long num = H5F.create(path, 2u, 0L, 0L);
        //        if (!CreateStringsAttribute(num, "file_format_version", new string[1] { "1.2" }))
        //        {
        //            result = false;
        //        }
        //        if (!CreateStringsAttribute(num, "file_format", new string[1] { "HyperSpy" }))
        //        {
        //            result = false;
        //        }
        //        long num2 = H5G.create(num, "/Experiments", 0L, 0L, 0L);
        //        long num3 = H5G.create(num2, "__unnamed__", 0L, 0L, 0L);
        //        string text = "SEM";
        //        text = ((!(parameter.Voltage > 30.0)) ? "SEM" : "TEM");
        //        long num4 = H5G.create(num3, "metadata", 0L, 0L, 0L);
        //        long num5 = H5G.create(num4, "Acquisition_instrument", 0L, 0L, 0L);
        //        long num6 = H5G.create(num5, text, 0L, 0L, 0L);
        //        long num7 = H5G.create(num6, "Detector", 0L, 0L, 0L);
        //        long num8 = H5G.create(num7, "EDS", 0L, 0L, 0L);
        //        CreateDoubleAttribute(num8, "elevation_angle", parameter.TakeoffAngle);
        //        CreateDoubleAttribute(num8, "live_time", LiveTime);
        //        CreateDoubleAttribute(num8, "real_time", RealTime);
        //        CreateDoubleAttribute(num8, "energy_resolution_MnKa", resolution);
        //        H5G.close(num8);
        //        H5G.close(num7);
        //        CreateDoubleAttribute(num6, "magnification", parameter.Mag);
        //        CreateStringsAttribute(num6, "microscope", new string[1] { parameter.Microscope });
        //        H5G.close(num6);
        //        H5G.close(num5);
        //        long num9 = H5G.create(num4, "General", 0L, 0L, 0L);
        //        CreateStringsAttribute(num9, "title", new string[1] { " " });
        //        CreateStringsAttribute(num9, "notes", new string[1] { header.Memo });
        //        H5G.close(num9);
        //        long num10 = H5G.create(num4, "Signal", 0L, 0L, 0L);
        //        CreateStringsAttribute(num10, "signal_type", new string[1] { "EDS_" + text });
        //        CreateStringsAttribute(num10, "record_by", new string[1] { "spectrum" });
        //        H5G.close(num10);
        //        long num11 = H5G.create(num4, "Sample", 0L, 0L, 0L);
        //        string[] array = new string[xlines.Length];
        //        string[] array2 = new string[xlines.Length];
        //        for (int i = 0; i < xlines.Length; i++)
        //        {
        //            array2[i] = xlines[i].Replace(" ", "_") + "a";
        //            array[i] = xlines[i].Substring(0, xlines[i].IndexOf(" "));
        //        }
        //        CreateStringsAttribute(num11, "elements", array);
        //        CreateStringsAttribute(num11, "xray_lines", array2);
        //        array2 = null;
        //        array = null;
        //        H5G.close(num11);
        //        H5G.close(num4);
        //        H5G.close(H5G.create(num3, "original_metadata", 0L, 0L, 0L));
        //        ulong[] dims = new ulong[3]
        //        {
        //        (ulong)Height,
        //        (ulong)Width,
        //        (ulong)ChannelCount
        //        };
        //        ulong[] array3 = (array3 = new ulong[3]);
        //        if (Width >= 512 || Height >= 512)
        //        {
        //            array3[0] = 256uL;
        //            array3[1] = 256uL;
        //            array3[2] = 512uL;
        //        }
        //        else
        //        {
        //            array3[0] = (ulong)Height;
        //            array3[1] = (ulong)Width;
        //            array3[2] = (ulong)ChannelCount;
        //        }
        //        long num12 = H5S.create_simple(3, dims, null);
        //        long num13 = 0L;
        //        int num14 = -1;
        //        if (num12 >= 0)
        //        {
        //            num13 = H5P.create(H5P.DATASET_CREATE);
        //            if (num13 != -1)
        //            {
        //                num14 = H5P.set_chunk(num13, 3, array3);
        //                num14 = H5P.set_deflate(num13, 6u);
        //            }
        //            else
        //            {
        //                num14 = -1;
        //            }
        //        }
        //        else
        //        {
        //            num14 = -1;
        //        }
        //        long dset_id = ((num14 >= 0) ? H5D.create(num3, "data", H5T.STD_I32LE, num12, 0L, num13, 0L) : H5D.create(num3, "data", H5T.STD_I32LE, num12, 0L, 0L, 0L));

        //        var ptr = spectra.GetIntPtr();
        //        H5D.write(dset_id, H5T.STD_I32LE, 0L, 0L, 0L, ptr);
                
        //        H5D.close(dset_id);
        //        long num15 = H5G.create(num3, "axis-0", 0L, 0L, 0L);
        //        CreateStringsAttribute(num15, "name", new string[1] { "y" });
        //        CreateDoubleAttribute(num15, "offset", 0.0);
        //        CreateDoubleAttribute(num15, "scale", PixelSize);
        //        CreateIntAttribute(num15, "size", Height);
        //        CreateStringsAttribute(num15, "units", new string[1] { "nm" });
        //        H5G.close(num15);
        //        num15 = H5G.create(num3, "axis-1", 0L, 0L, 0L);
        //        CreateStringsAttribute(num15, "name", new string[1] { "x" });
        //        CreateDoubleAttribute(num15, "offset", 0.0);
        //        CreateDoubleAttribute(num15, "scale", PixelSize);
        //        CreateIntAttribute(num15, "size", Width);
        //        CreateStringsAttribute(num15, "units", new string[1] { "nm" });
        //        H5G.close(num15);
        //        num15 = H5G.create(num3, "axis-2", 0L, 0L, 0L);
        //        CreateStringsAttribute(num15, "name", new string[1] { "Energy" });
        //        CreateDoubleAttribute(num15, "offset", parameter.CoefB);
        //        CreateDoubleAttribute(num15, "scale", parameter.CoefA);
        //        CreateIntAttribute(num15, "size", ChannelCount);
        //        CreateStringsAttribute(num15, "units", new string[1] { "keV" });
        //        H5G.close(num15);
        //        H5G.close(num3);
        //        H5G.close(num2);
        //        H5F.close(num);
        //    }
        //    catch (Exception)
        //    {
        //        result = false;
        //    }
        //    return result;
        //}

        //public bool Export2HDF5_All(string path)
        //{
        //    return Export2HDF5(0, -1, path);
        //}

        //public unsafe bool Export2HDF5(int startFrame, int frame_cnt, string path)
        //{
        //    char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
        //    if (path.IndexOfAny(invalidFileNameChars) < 0)
        //    {
        //        Console.WriteLine("Including non-available character.");
        //        return false;
        //    }
        //    if (!Directory.Exists(Path.GetDirectoryName(path)))
        //    {
        //        Console.WriteLine("No directly of " + Path.GetDirectoryName(path));
        //        return false;
        //    }
        //    if (Encoding.GetEncoding("Shift_JIS").GetByteCount(path) > path.Length)
        //    {
        //        Console.WriteLine("This file name(path) has full-letters(ex. Japanese characters).");
        //        return false;
        //    }
        //    pd.Title = "Exporting";
        //    pd.Minimum = 0;
        //    pd.Maximum = 100;
        //    pd.Show();
        //    pd.Value = 0;
        //    pd.Message = "Reading the data.";
        //    LongArray<int> spectra = null;
        //    try
        //    {
        //        if (frame_cnt == -1)
        //        {
        //            frame_cnt = sweepCount - startFrame;
        //        }
        //        spectra = GetSpectrumCubeT(startFrame, frame_cnt);

        //        if (spectra == null)
        //        {
        //            return false;
        //        }
        //        if (pd.Canceled)
        //        {
        //            pd.Message = "Canceled to export.";
        //            Thread.Sleep(1000);
        //            pd.Close();
        //            return false;
        //        }
        //        if (Width > 500 && Height > 380)
        //        {
        //            pd.Message = "Exporting to HDF5. Wait a few minutes for saving.";
        //        }
        //        else
        //        {
        //            pd.Message = "Exporting to HDF5.";
        //        }
        //        bool result = Save2HDF5(ref spectra, path);
        //        spectra.Dispose();
        //        pd.Value = 100;
        //        pd.Message = "Completed to export.";
        //        Thread.Sleep(1000);
        //        pd.Close();
        //        return result;
        //    }
        //    catch (OutOfMemoryException)
        //    {
        //        spectra?.Dispose();
        //        pd.Close();
        //        return false;
        //    }
        //}

        public uint BeginRead()
        {
            long offset = ((header.Id != null) ? header.PttdOffset : 0);
            stream.Seek(offset, SeekOrigin.Begin);
            return 0u;
        }

        // 260516Codex: Reads SEM image samples from 0xA000 PTTD records using the same pixel tracking as the spectrum cube reader.
        public byte[,]? TryReadSemImage()
        {
            if (!HasHeader || Width <= 0 || Height <= 0 || Width > 4096)
                return null;

            BeginRead();
            int[,] rawImage = new int[Width, Height];
            bool[,] hasPixel = new bool[Width, Height];
            int x = 0;
            int y = 0;
            int previousX = 0;
            int previousY = 0;
            int coordinateUnit = 4096 / Width;
            int filledPixels = 0;
            int expectedPixels = Width * Height;

            if (coordinateUnit <= 0)
                return null;

            try
            {
                while (true)
                {
                    ushort record = reader.ReadUInt16();
                    int value = record & 0xFFF;
                    switch (record & 0xF000)
                    {
                        case 0x8000:
                            if (previousX != value)
                            {
                                x = (previousX == 0 || value != 0) ? value / coordinateUnit : 0;
                                previousX = value;
                            }
                            break;
                        case 0x9000:
                            if (previousY == value)
                                break;

                            y = previousY != 0 && value == 0 ? 0 : value / coordinateUnit;
                            x = 0;
                            previousY = value;
                            break;
                        case 0xA000:
                            if (x < 0 || x >= Width || y < 0 || y >= Height)
                                break;

                            rawImage[x, y] = value;
                            if (!hasPixel[x, y])
                            {
                                hasPixel[x, y] = true;
                                filledPixels++;
                            }
                            if (filledPixels == expectedPixels)
                                return NormalizeSemImage(rawImage, hasPixel);
                            break;
                    }
                }
            }
            catch (EndOfStreamException)
            {
            }

            if (filledPixels == 0)
                return null;

            return NormalizeSemImage(rawImage, hasPixel);
        }

        // 260516Codex: Normalizes 0xA000 SEM signal values to the byte image consumed by Image1ToBitmap.
        private byte[,] NormalizeSemImage(int[,] rawImage, bool[,] hasPixel)
        {
            int minValue = int.MaxValue;
            int maxValue = int.MinValue;
            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    if (!hasPixel[i, j])
                        continue;

                    minValue = Math.Min(minValue, rawImage[i, j]);
                    maxValue = Math.Max(maxValue, rawImage[i, j]);
                }
            }

            byte[,] image = new byte[Width, Height];
            if (maxValue <= byte.MaxValue)
            {
                for (int i = 0; i < Width; i++)
                {
                    for (int j = 0; j < Height; j++)
                    {
                        if (hasPixel[i, j])
                            image[i, j] = (byte)rawImage[i, j];
                    }
                }
                return image;
            }

            int range = maxValue - minValue;
            if (range == 0)
            {
                for (int i = 0; i < Width; i++)
                {
                    for (int j = 0; j < Height; j++)
                    {
                        if (hasPixel[i, j])
                            image[i, j] = byte.MaxValue;
                    }
                }
                return image;
            }

            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    if (hasPixel[i, j])
                        image[i, j] = (byte)((rawImage[i, j] - minValue) * byte.MaxValue / range);
                }
            }
            return image;
        }

        // 260516Codex: Converts the SEM image read from 0xA000 PTTD records through the existing image1 bitmap path.
        public Bitmap? TryReadSemImageBitmap()
        {
            image1 = TryReadSemImage();
            return Image1ToBitmap();
        }

        public Bitmap Image1ToBitmap()
        {
            if (image1 == null)
            {
                return null;
            }
            Bitmap bitmap = new Bitmap(imageWidth, imageHeight, PixelFormat.Format24bppRgb);
            for (int i = 0; i < imageWidth; i++)
            {
                for (int j = 0; j < imageHeight; j++)
                {
                    int num = image1[i, j];
                    bitmap.SetPixel(i, j, Color.FromArgb(num, num, num));
                }
            }
            return bitmap;
        }
    }
}
