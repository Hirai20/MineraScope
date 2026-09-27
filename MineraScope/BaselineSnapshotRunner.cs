using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Tensorflow.NumPy;

namespace MineraScope
{
    // 260730Claude: 【デバッグ・開発専用】軸を実行時値化する改修 (SpectrumAxis 導入 / SpectrumLength const 撤去 /
    //   preprocessing の eV 化) の前後で数値が変わっていないことを確かめるための基準値スナップショット。
    //   製品機能ではなく UI からは呼ばれない。環境変数が無ければ通常の GUI 起動には影響しない。
    //   既存コードは読むだけで一切変更しないので、この採取そのものがアプリ挙動を変えることはない。
    //
    //   使い方: 改修前に label=before で 1 回、改修後に label=after で 1 回走らせ、2 つの出力フォルダを diff する。
    //   一致すべきなのは既定軸 (ZeroOffset=0 / ChannelWidth=10 / ChannelCount=2048) の経路だけで、
    //   非 2048 の end-to-end や軸不一致の拒否は別途試験が必要 (この採取では担保されない)。
    //
    //   環境変数:
    //     MINERASCOPE_BASELINE          = "all" | "env" | "predict" | "data" | "smoke" (カンマ区切り可)
    //                                     all = env,predict,data。smoke は TF の状態が混ざらないよう単独指定のときだけ走る
    //     MINERASCOPE_BASELINE_LABEL    = <name>    -> 出力サブフォルダ名 (省略時は日時)
    //     MINERASCOPE_BASELINE_MODEL    = <folder>  -> 対象モデルフォルダ (省略時は FormMainSettings の ModelPath)
    //     MINERASCOPE_BASELINE_SPECTRA  = <folder>  -> 予測に使う実測スペクトルのフォルダ (再帰探索、.msa/.emsa/.eds)
    //     MINERASCOPE_BASELINE_OUT      = <folder>  -> 出力先の親 (省略時は DefaultStoragePaths.BaselinesFolder)
    // 260803Codex: MINERASCOPE_BASELINE_OUT は BaselinesFolder 自身またはその配下だけを許可する。
    internal static class BaselineSnapshotRunner
    {
        private const string SectionEnvironment = "env";
        private const string SectionPredict = "predict";
        private const string SectionData = "data";
        private const string SectionSmoke = "smoke";

        // 260803Claude: 節の名前は const と 1 か所で対応させる (追加時の編集漏れを防ぐ)。
        private static readonly string[] KnownSections = [SectionEnvironment, SectionPredict, SectionData, SectionSmoke];

        // 260730Claude: 浮動小数は round-trip できる桁で出す (G9 は DeepLearning.FormatMetric と同じ)。
        //   桁を落とすと改修後の差分を取りこぼすので、表示用の丸めは使わない。
        private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);

        private static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

        // 260803Claude: 兄弟の SimulationHeadlessRunner と同じく、writer ではなくログのパスだけを持つ。
        //   1 回の実行で数十行しか書かないので追記のたびに開き直す方が、生存期間の管理より単純で落ちにも強い。
        private static string _logPath = string.Empty;

        public static void Run(string mode)
        {
            // 260809Claude: 節名の打ち間違いは return ではなく throw で返す。呼び出し元 (Program) が診断ログを
            //   抑止した状態でここを通るため、ログへ書いて return すると「何の記録も残らず終了」になる。
            //   throw なら抑止スコープを抜けてから RunHeadless の catch が記録する。
            var sections = ParseSections(mode);
            if (sections.Count == 0)
                throw new ArgumentException($"unknown MINERASCOPE_BASELINE mode '{mode}'. use all|env|predict|data|smoke");

            string requestedLabel = ReadEnv("MINERASCOPE_BASELINE_LABEL") ?? DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            // 260803Codex: label を単一フォルダ名へ制限し、相対パスで Baselines 外へ出る書込みを防ぎます。
            // 260809Claude: Path.GetFileName は "." / ".." をそのまま返すので、この 2 つだけは別に弾く
            //   (弾かないと出力先が Baselines の親へ逃げて、上のガードが目的を果たさない)。
            string label = Path.GetFileName(requestedLabel);
            if (string.IsNullOrWhiteSpace(label) || !string.Equals(label, requestedLabel, StringComparison.Ordinal) || label is "." or "..")
                throw new ArgumentException("MINERASCOPE_BASELINE_LABEL must be a single folder name.");

            // 260803Codex: 任意出力先を指定する場合も、許可された Baselines ツリー内だけに制限します。
            string parent = ResolveOutputParent();
            string outputFolder = Path.Combine(parent, label);
            Directory.CreateDirectory(outputFolder);

            // 260730Claude: 節を分けて何回か走らせる (重い data と単独実行が必要な smoke) ので、ログは追記にする。
            _logPath = Path.Combine(outputFolder, "run.log");
            Log($"baseline start label={label} sections={string.Join(",", sections)}");
            Log($"output={outputFolder}");

            // 260730Claude: smoke は TF グラフ/セッションを自前で作るので、予測でモデルをロードした状態と混ぜない。
            //   単独指定のときだけ走らせ、混在指定なら理由を書いて飛ばす。
            if (sections.Contains(SectionSmoke) && sections.Count > 1)
            {
                Log("SKIP smoke: TF の状態が予測側と混ざるため単独指定 (MINERASCOPE_BASELINE=smoke) のときだけ実行する");
                sections.Remove(SectionSmoke);
            }

            string modelPath = ResolveModelPath();
            if (sections.Contains(SectionEnvironment))
                WriteEnvironment(outputFolder, modelPath);
            if (sections.Contains(SectionPredict))
                WritePredictions(outputFolder, modelPath);
            if (sections.Contains(SectionData))
                WriteTrainingData(outputFolder);
            if (sections.Contains(SectionSmoke))
                WriteSmoke(outputFolder);

            Log("baseline done");
        }

        private static HashSet<string> ParseSections(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode) || mode.Equals("1", StringComparison.Ordinal) || mode.Equals("all", StringComparison.OrdinalIgnoreCase))
                return [SectionEnvironment, SectionPredict, SectionData];

            var requested = mode
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.ToLowerInvariant())
                .Where(KnownSections.Contains);

            return [.. requested];
        }

        #region 環境 — 採取時点の定数と成果物

        // 260730Claude: どの軸・どの前処理で採った基準値なのかを最初に固定する。
        //   改修後に diff したとき「前提が違っただけ」を切り分けられるようにするための節。
        private static void WriteEnvironment(string outputFolder, string modelPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 採取時点の軸定数 (改修対象)");
            sb.AppendLine($"SpectrumDataLoader.SpectrumLength={SpectrumDataLoader.SpectrumLength.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"EdsSpectrumReader.EnergyPerChannelEv={F(EdsSpectrumReader.EnergyPerChannelEv)}");
            sb.AppendLine();

            var training = SpectrumPreprocessing.ForTraining();
            sb.AppendLine("# 前処理");
            sb.AppendLine($"training.describe={training.Describe()}");
            sb.AppendLine($"training.maskChannelCount={training.MaskChannelCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"training.maskBeforeNormalize={training.MaskBeforeNormalize}");

            sb.AppendLine();
            sb.AppendLine("# モデル");
            sb.AppendLine($"modelPath={modelPath}");
            bool modelExists = Directory.Exists(modelPath);
            sb.AppendLine($"modelExists={modelExists}");
            if (modelExists)
            {
                string classificationFolder = ModelArtifactPaths.GetClassificationFolder(modelPath);
                var predict = SpectrumPreprocessing.LoadFromModelFolder(classificationFolder);
                sb.AppendLine($"predict.describe={predict.Describe()}");
                sb.AppendLine($"predict.maskChannelCount={predict.MaskChannelCount.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine($"predict.maskBeforeNormalize={predict.MaskBeforeNormalize}");
                sb.AppendLine();

                AppendFileDigest(sb, classificationFolder, ModelArtifactPaths.LabelEncoderFileName);
                AppendFileDigest(sb, classificationFolder, SpectrumPreprocessing.FileName);
                AppendFileDigest(sb, classificationFolder, MineralUnknownDetector.FileName);
                AppendFileDigest(sb, modelPath, DetectorProfile.FileName);

                // 260730Claude: detectorProfile.json は軸の権威なので、ハッシュだけでなく中身も残す (小さいので全文)。
                string detectorProfilePath = Path.Combine(modelPath, DetectorProfile.FileName);
                if (File.Exists(detectorProfilePath))
                {
                    sb.AppendLine();
                    sb.AppendLine($"# {DetectorProfile.FileName} 全文");
                    sb.AppendLine(File.ReadAllText(detectorProfilePath).ReplaceLineEndings("\n").TrimEnd());
                }
            }

            File.WriteAllText(Path.Combine(outputFolder, "environment.txt"), sb.ToString(), Encoding.UTF8);
            Log(modelExists ? "environment.txt written" : "environment.txt written (model folder not found)");
        }

        private static void AppendFileDigest(StringBuilder sb, string folder, string fileName)
        {
            string path = Path.Combine(folder, fileName);
            if (!File.Exists(path))
            {
                sb.AppendLine($"{fileName}=missing");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            sb.AppendLine($"{fileName}.bytes={bytes.Length.ToString(CultureInfo.InvariantCulture)} sha256={Sha256(bytes)}");
        }

        #endregion

        #region 予測 — 実測スペクトル 1 本ごとの出力

        // 260730Claude: 既存モデル + 実測スペクトルで、正規化入力・全クラス確率・未学習判定を 1 行ずつ残す。
        //   学習を回さずに済むうえ、B4a (予測側をモデル軸で動かす) と B6 (入力軸の照合) が壊したら必ずここに出る。
        private static void WritePredictions(string outputFolder, string modelPath)
        {
            string? spectraFolder = ReadEnv("MINERASCOPE_BASELINE_SPECTRA");
            if (string.IsNullOrWhiteSpace(spectraFolder) || !Directory.Exists(spectraFolder))
            {
                Log($"SKIP predict: MINERASCOPE_BASELINE_SPECTRA が未指定か存在しない ({spectraFolder ?? "unset"})");
                return;
            }

            string classificationFolder = ModelArtifactPaths.GetClassificationFolder(modelPath);
            if (!Directory.Exists(classificationFolder))
            {
                Log($"SKIP predict: 分類モデルフォルダが無い ({classificationFolder})");
                return;
            }

            // 260803Claude: 対象スペクトルの収集は SpectrumFileCollector に任せる (拡張子の判定を二重に持たない)。
            //   Collect は HashSet 由来で順不同なので、diff がノイズにならないよう Ordinal で並べ替えるのはここの責務。
            var files = SpectrumFileCollector.Collect([spectraFolder])
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (files.Length == 0)
            {
                Log($"SKIP predict: 対象スペクトルが 0 件 ({spectraFolder})");
                return;
            }

            var preprocessing = SpectrumPreprocessing.LoadFromModelFolder(classificationFolder);
            var service = new MineralClassificationPredictionService();
            var labelNames = service.GetLabelNames(modelPath).OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Log($"predict: files={files.Length} labels={labelNames.Length} preprocessing={preprocessing.Describe()}");

            using var writer = new StreamWriter(Path.Combine(outputFolder, "predictions.tsv"), append: false, Encoding.UTF8);
            writer.Write("file\tinputSha256\ttop1\tconfidence\tisUnknown\tunknownScore\tunknownThreshold\tnearestKnown");
            foreach (string name in labelNames)
                writer.Write($"\tp:{name}");

            writer.WriteLine();

            int failed = 0;
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(spectraFolder, file).Replace('\\', '/');
                var normalized = SpectrumDataLoader.LoadNormalizedSpectrum(file, preprocessing);
                if (normalized is null)
                {
                    failed++;
                    writer.WriteLine($"{relative}\tLOAD-FAILED");
                    continue;
                }

                var result = service.Predict(modelPath, normalized);
                var byName = result.Probabilities.ToDictionary(p => p.MineralName, p => p.Confidence, StringComparer.Ordinal);

                writer.Write(string.Join(
                    '\t',
                    relative,
                    HashFloats(normalized),
                    result.PredictedMineral,
                    F(result.Confidence),
                    result.IsUnknown,
                    // 260831Codex: 軸改修用の既存 baseline と比較できるよう、ここだけ従来の float 精度を維持する。
                    result.UnknownScore is { } score ? F((float)score) : "-",
                    result.UnknownThreshold is { } threshold ? F((float)threshold) : "-",
                    result.NearestKnownMineral ?? "-"));

                foreach (string name in labelNames)
                    writer.Write($"\t{(byName.TryGetValue(name, out float value) ? F(value) : "-")}");

                writer.WriteLine();
            }

            Log($"predictions.tsv written (rows={files.Length} loadFailed={failed})");
        }

        #endregion

        #region 学習データ — 選抜・正規化・分割

        // 260730Claude: 学習は回さず、その手前までを再現する。B3a (軸引数化) が正規化・選抜・分割の
        //   どこかを変えたらここのハッシュが動く。選抜乱数は seed=42 固定なので再実行しても同じ結果になる。
        private static void WriteTrainingData(string outputFolder)
        {
            if (TryCreateRequest() is not { } request)
                return;

            var repository = new SpectrumPoolRepository(new SpectrumConditionKeyBuilder());
            // 260803Codex: LoadState の修復結果は採取値へ反映しつつ、ユーザーの manifest へは保存しません。
            var workflow = new SpectrumPoolWorkflow(repository, new SimulationPlanBuilder(), persistManifestRepairs: false);
            var selectedPools = workflow.CreateTrainingPools(request, out var shortages);
            if (shortages.Count > 0)
            {
                Log($"SKIP data: spectrum pool が不足しているため学習入力を作れない ({shortages.Count} 件)");
                File.WriteAllText(
                    Path.Combine(outputFolder, "training-shortages.txt"),
                    SpectrumPoolWorkflow.FormatShortageMessage(shortages),
                    Encoding.UTF8);
                return;
            }

            // 260803Claude: 学習は CreateTrainingPools の出力をそのまま使わず、DeepLearning.RunTraining と同じ
            //   「空 pool を除いて鉱物名順」へ並べ替えてから読み込む。pool の並び順は行の並び順になり、
            //   固定 seed の train/test シャッフルの入力になるので、ここで揃えないと記録するハッシュが
            //   学習が実際に作る配列と別物になる (鉱物 DB の XML は五十音順でも英字順でもない)。
            var pools = selectedPools
                .Where(pool => pool.Samples.Count > 0)
                .OrderBy(pool => pool.MineralName)
                .ToArray();
            // 260901Codex: Mirror DeepLearning's flattening order so source metadata and split rows remain aligned.
            var classificationSamples = pools
                .SelectMany(pool => pool.Samples.Select(sample => (pool.MineralName, Sample: sample)))
                .ToArray();
            int sourceCount = classificationSamples.Count(item => item.Sample.Source is not null);
            if (sourceCount > 0 && sourceCount != classificationSamples.Length)
                throw new InvalidDataException(
                    $"Training source metadata is incomplete: expected={classificationSamples.Length}, actual={sourceCount}.");

            string spectrumRoot = request.Paths.SpectrumOutputFolder;
            using (var writer = new StreamWriter(Path.Combine(outputFolder, "training-pools.tsv"), append: false, Encoding.UTF8))
            {
                // 260901Codex: Include the exact manifest rows needed to reproduce a multi-time selection.
                writer.WriteLine("mineral\tindex\tpath\tliveTime\tconditionKey\tsimulationId\tmanifestPath");
                foreach (var pool in pools)
                    for (int i = 0; i < pool.Samples.Count; i++)
                    {
                        var sample = pool.Samples[i];
                        string relative = Path.GetRelativePath(spectrumRoot, sample.FilePath).Replace('\\', '/');
                        var source = sample.Source;
                        string manifestPath = source is null
                            ? string.Empty
                            : Path.GetRelativePath(spectrumRoot, source.ManifestPath).Replace('\\', '/');
                        writer.WriteLine(string.Join(
                            '\t',
                            pool.MineralName,
                            i.ToString(CultureInfo.InvariantCulture),
                            relative,
                            source?.LiveTime.ToString("G17", CultureInfo.InvariantCulture) ?? string.Empty,
                            source?.ConditionKey ?? string.Empty,
                            source?.SimulationId.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                            manifestPath));
                    }
            }

            Log($"training-pools.tsv written (minerals={pools.Length} samples={pools.Sum(pool => pool.Samples.Count)})");

            var preprocessing = SpectrumPreprocessing.ForTraining();
            // 260902Codex: Baseline は既存の件数検証を維持し、UI 用の個別失敗情報はここでは使いません。
            var (spectra, labels, stats, _) = SpectrumDataLoader.LoadClassificationData(pools, default, null, preprocessing);
            // 260903Codex: 実学習と同じく全入力を必須とし、旧形式 pool でも部分データの基準値を作りません。
            if (stats.SkippedSamples > 0)
                throw new InvalidDataException(
                    $"Training spectrum load failed for baseline: expected={stats.InputSamples}, loaded={stats.LoadedSamples}.");
            var (encodedLabels, encoder) = DeepLearningDataSplitter.EncodeLabels(labels);
            float testSplit = request.Training.ValidationSplit;
            // 260907Codex: Match the unchanged seed-42 non-stratified training split independently of composition/time planning.
            var (xTrain, xTest, yTrain, yTest) = DeepLearningDataSplitter.TrainTestSplitClassification(
                spectra,
                encodedLabels,
                testSize: testSplit,
                randomState: 42);

            var sb = new StringBuilder();
            sb.AppendLine("# 読み込み");
            sb.AppendLine($"preprocessing={preprocessing.Describe()}");
            sb.AppendLine($"inputSamples={stats.InputSamples.ToString(CultureInfo.InvariantCulture)} loadedSamples={stats.LoadedSamples.ToString(CultureInfo.InvariantCulture)} skippedSamples={stats.SkippedSamples.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"spectra.shape={FormatShape(spectra)} sha256={HashFloats(spectra.ToArray<float>())}");
            sb.AppendLine($"labels.count={labels.Count.ToString(CultureInfo.InvariantCulture)} sha256={Sha256(Encoding.UTF8.GetBytes(string.Join("\n", labels)))}");
            sb.AppendLine();

            sb.AppendLine("# ラベルエンコード (index 昇順)");
            foreach (var pair in encoder.OrderBy(pair => pair.Value))
                sb.AppendLine($"[{pair.Value.ToString(CultureInfo.InvariantCulture)}] {pair.Key} ({labels.Count(label => label == pair.Key).ToString(CultureInfo.InvariantCulture)}件)");

            sb.AppendLine();
            sb.AppendLine("# train/test 分割 (randomState=42)");
            sb.AppendLine($"testSize={F(testSplit)}");
            sb.AppendLine("stratifiedBy=none");
            sb.AppendLine($"xTrain.shape={FormatShape(xTrain)} sha256={HashFloats(xTrain.ToArray<float>())}");
            sb.AppendLine($"xTest.shape={FormatShape(xTest)} sha256={HashFloats(xTest.ToArray<float>())}");
            sb.AppendLine($"yTrain.shape={FormatShape(yTrain)} sha256={HashInts(yTrain.ToArray<int>())}");
            sb.AppendLine($"yTest.shape={FormatShape(yTest)} sha256={HashInts(yTest.ToArray<int>())}");

            File.WriteAllText(Path.Combine(outputFolder, "training-arrays.txt"), sb.ToString(), Encoding.UTF8);
            Log($"training-arrays.txt written (spectra={spectra.shape[0]} train={xTrain.shape[0]} test={xTest.shape[0]})");
        }

        // 260730Claude: 保存済み UI 設定からの request 組み立ては SimulationHeadlessRunner と同じ順序・換算にそろえる
        //   (Resolution/100、ValidationSplit/100)。学習側と違う request を作ると pool の conditionKey がずれる。
        private static ModelCreationRequest? TryCreateRequest()
        {
            var formMain = FormUserSettingsStore.Load<FormMainUserSettings>("FormMainSettings.json").Settings;
            // 260901Codex: 初回起動は GUI の Designer 既定 ON、保存済み旧設定の null は互換 OFF として解釈します。
            var generatorLoad = FormUserSettingsStore.Load<GeneratorFormUserSettings>("GeneratorFormSettings.json");
            // 260901Codex: 設定ファイルがない baseline 採取も、GUI と同じ有効な数値条件で B を再現します。
            var generator = generatorLoad.HasStoredValues
                ? generatorLoad.Settings
                : GeneratorFormUserSettings.CreateInitialDefaults();
            bool useMeasurementTimePresetB = generatorLoad.HasStoredValues
                ? generator.MeasurementTimePresetBEnabled ?? false
                : true;
            foreach (string warning in FormUserSettingsStore.DrainWarnings())
                Log($"WARN: {warning}");

            string outputFolder = string.IsNullOrWhiteSpace(formMain.EdxOutputPath)
                ? DefaultStoragePaths.TrainingDataFolder
                : formMain.EdxOutputPath;
            if (!Directory.Exists(outputFolder))
            {
                Log($"SKIP data: spectrum 出力先が存在しない ({outputFolder})");
                return null;
            }

            string assemblyPath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location) ?? AppContext.BaseDirectory;
            var solutions = new MineralDatabaseRepository(assemblyPath).Load();
            if (solutions.Length == 0)
            {
                Log("SKIP data: 鉱物 DB から固溶体を読めなかった");
                return null;
            }

            // 260901Codex: B 選択時は通常学習と同じ共通配分を request へ渡し、単一時間の保存値は変更しません。
            return new ModelCreationRequest(
                new ModelCreationPaths(
                    outputFolder.Trim(),
                    DefaultStoragePaths.PythonScriptsFolder,
                    DtsaMsiInstallation.UseDefaultIfBlank(formMain.DtsaPath),
                    (formMain.ModelPath ?? string.Empty).Trim()),
                generator.ModelName.Trim(),
                new SemEdxCondition(
                    generator.GetDetectorProfile(),
                    generator.CarbonThickness,
                    generator.BeamEnergy,
                    generator.LiveTime,
                    generator.ProbeCurrent),
                new SimulationExecutionSettings(
                    (int)generator.TargetSpectrumCount,
                    generator.Resolution / 100,
                    (int)generator.ParallelCount,
                    generator.CarbonThicknessJitterPercent),
                new ModelTrainingSettings(
                    (int)generator.Epochs,
                    (int)generator.BatchSize,
                    (int)generator.EarlyStopping,
                    (float)generator.ValidationSplit / 100f,
                    generator.UnknownDistanceScale),
                solutions)
            {
                SpectrumTimeAllocations = useMeasurementTimePresetB
                    ? SpectrumTimeSchedule.MeasurementTimeB.CreateAllocations()
                    : [],
                // 260907Codex: Baseline coverage must exercise the same B planner as interactive and headless generation.
                SpectrumTimeSchedule = useMeasurementTimePresetB
                    ? SpectrumTimeSchedule.MeasurementTimeB
                    : null
            };
        }

        #endregion

        #region 重み経路 — 既存の合成データ smoke test

        // 260730Claude: 学習ループと初期重みの経路は、既存の合成データ smoke test (seed 42 固定) で代表させる。
        //   実データの再学習が要らず、CreateClassificationModel と学習ループを通るので、
        //   改修が重みの数値を変えたら最終 loss/accuracy が動く。所要時間は比較対象にしない (実行ごとに変わる)。
        private static void WriteSmoke(string outputFolder)
        {
            var lines = new List<string>();
            DeepLearning.RunHeadlessSmokeTest(line => lines.Add(line));

            string? done = lines.LastOrDefault(line => line.StartsWith("headless smoke done:", StringComparison.Ordinal));
            var sb = new StringBuilder();
            sb.AppendLine("# 合成データ smoke test (seed=42)。totalMs は比較対象外");
            sb.AppendLine(done is null
                ? "result=missing"
                : ExtractComparableSmokeResult(done));
            sb.AppendLine();
            sb.AppendLine("# 生ログ");
            foreach (string line in lines)
                // 260803Codex: 生ログ側にも実行時間を残さず、同一条件の採取結果を決定的にします。
                sb.AppendLine(RemoveSmokeRuntimeValues(line));

            File.WriteAllText(Path.Combine(outputFolder, "smoke.txt"), sb.ToString(), Encoding.UTF8);
            Log("smoke.txt written");
        }

        // 260730Claude: "headless smoke done: totalMs=... epochs=... final=..." から時間だけ落として比較可能な部分を取る。
        private static string ExtractComparableSmokeResult(string doneLine) => RemoveSmokeRuntimeValues(doneLine);

        // 260803Codex: 現在の smoke 出力で実行ごとに変動する totalMs token を全記録経路から除きます。
        private static string RemoveSmokeRuntimeValues(string line)
        {
            var parts = line
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("totalMs=", StringComparison.Ordinal));

            return string.Join(' ', parts);
        }

        #endregion

        #region 共通

        // 260730Claude: 保存設定の ModelPath は「親フォルダ」で、推論に渡すのは「選択中モデルフォルダ」。
        //   両者を取り違えると分類サブフォルダが見つからず予測節が丸ごと skip されるので、必ず結合する。
        private static string ResolveModelPath()
        {
            string? overridePath = ReadEnv("MINERASCOPE_BASELINE_MODEL");
            if (!string.IsNullOrWhiteSpace(overridePath))
                return overridePath.Trim();

            var formMain = FormUserSettingsStore.Load<FormMainUserSettings>("FormMainSettings.json").Settings;
            string parent = (formMain.ModelPath ?? string.Empty).Trim();
            string selected = (formMain.SelectedModelName ?? string.Empty).Trim();
            return string.IsNullOrEmpty(selected) ? parent : Path.Combine(parent, selected);
        }

        private static string? ReadEnv(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // 260803Codex: MINERASCOPE_BASELINE_OUT は Baselines 自身またはその子だけを許可します。
        private static string ResolveOutputParent()
        {
            string baselineRoot = Path.GetFullPath(DefaultStoragePaths.BaselinesFolder);
            string requested = Path.GetFullPath(ReadEnv("MINERASCOPE_BASELINE_OUT") ?? baselineRoot);
            string rootPrefix = baselineRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (string.Equals(requested, baselineRoot, StringComparison.OrdinalIgnoreCase)
                || requested.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return requested;

            throw new ArgumentException("MINERASCOPE_BASELINE_OUT must be inside DefaultStoragePaths.BaselinesFolder.");
        }

        private static string FormatShape(NDArray array) =>
            $"[{string.Join(",", array.shape.dims.Select(dim => dim.ToString(CultureInfo.InvariantCulture)))}]";

        private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        // 260730Claude: 丸めや文字列化を挟まず生のビット列をハッシュする。表示桁で比べると差分を取りこぼす。
        private static string HashFloats(float[] values) => Sha256(MemoryMarshal.AsBytes<float>(values));

        private static string HashInts(int[] values) => Sha256(MemoryMarshal.AsBytes<int>(values));

        private static void Log(string message)
        {
            if (_logPath.Length > 0)
                File.AppendAllText(_logPath, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}"), Encoding.UTF8);

            TensorFlowTrainingDebugLog.Write("baseline", TensorFlowTrainingDebugLog.Clean(message));
        }

        #endregion
    }
}
