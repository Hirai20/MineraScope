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

        // 260527Codex: PTS イベント列を1パス走査し、指定したブロック行帯のチャンネルカウントだけを返す（タイル読み対応）。
        // 全グリッドが要るときは startBlockY=0, blockRowCount=gridHeight を渡す。大配列確保前のチャンネル/メモリ検証は呼び出し側 (workflow) が行う前提。
        // 260526Claude: ホットループの band 比較は分岐予測が効くため、フルスキャン時の余分なコストは無視できる範囲。
        internal PtsBinnedSpectrumGrid? TryReadBinnedSpectrumGridRows(
            int binSize,
            int startBlockY,
            int blockRowCount,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            if (!HasHeader || Width <= 0 || Height <= 0 || ChannelCount <= 0 || Width > 4096 || binSize <= 0)
                return null;

            if (startBlockY < 0 || blockRowCount <= 0)
                throw new ArgumentOutOfRangeException(startBlockY < 0 ? nameof(startBlockY) : nameof(blockRowCount));

            int coordinateUnit = 4096 / Width;
            if (coordinateUnit <= 0)
                return null;

            int usableChannelCount = UsableChannelCount;
            if (usableChannelCount <= 0)
                return null;

            int gridWidth = (Width + binSize - 1) / binSize;
            int gridHeight = (Height + binSize - 1) / binSize;
            if (startBlockY >= gridHeight)
                return null;

            int localGridHeight = Math.Min(blockRowCount, gridHeight - startBlockY);
            int endBlockY = startBlockY + localGridHeight;
            long length = (long)gridWidth * localGridHeight * usableChannelCount;
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

                            int blockY = y / binSize;
                            if (blockY < startBlockY || blockY >= endBlockY)
                                break;

                            int blockIndex = ((blockY - startBlockY) * gridWidth) + x / binSize;
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
                localGridHeight,
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

        // 260612Codex: Keep the nullable bitmap contract explicit because SEM image data may be absent.
        public Bitmap? Image1ToBitmap()
        {
            if (image1 == null)
                return null;

            Bitmap bitmap = new(imageWidth, imageHeight, PixelFormat.Format24bppRgb);
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
