namespace MineraScope
{
    // 260924Codex: Softmaxエンコーダーを固定し、この3 → 16 → 8 → 2のUnknown headだけを学習する。
    internal sealed class SoftmaxOcnHead
    {
        // 261001Codex: 少数鉱物の集合では保存した入力幅を使い、旧3入力重みの配置を保持する。
        private const int H1 = 16, H2 = 8, Out = 2;
        // 261001Codex: 保存するheadの出力配置を、独立した実験用エピソードの型に依存させない。
        // 261001Claude: 保存済み head の重みは「出力0 = 未学習、出力1 = 学習済み」で学習されています。研究側 head.json を
        //   距離で掃引すると、距離が大きいほど出力0 が 1 へ近づくことを実測しました。並びが逆だったため Predict は
        //   P(学習済み) を未学習スコアとして返し、未学習判定が反転していました。並びの正本はここだけに置き、
        //   学習側も素の 0／1 ではなくこの名前を参照します。
        public const int UnknownOutput = 0, KnownOutput = 1;
        public int InputCount { get; }
        private const float LearningRate = 1e-3f;
        // 260925Codex: 順伝播と逆伝播で、保存する層の配置を共用する。
        private const int FirstWeights = 0;
        // 261001Codex: 単純な転送プロパティを使わず、保存した入力数を直接使う。
        private int FirstBias => InputCount * H1;
        private int SecondWeights => FirstBias + H1;
        private int SecondBias => SecondWeights + H1 * H2;
        private int OutputWeights => SecondBias + H2;
        private int OutputBias => OutputWeights + H2 * Out;
        private readonly float[] _w;
        private readonly float[] _g, _m, _v;
        private int _step;

        // 260924Codex: 保存済みheadは、予測に使う前にこの配置と完全一致する必要がある。
        // 260925Codex: 最後の層のオフセットから、保存する重みの数を求める。
        public static int WeightCount => GetWeightCount(3);
        // 261001Codex: 重みの確保・復元前に、保存した構造を検証する。
        public static int GetWeightCount(int inputCount) => inputCount is >= 1 and <= 3
            ? inputCount * H1 + H1 + H1 * H2 + H2 + H2 * Out + Out
            : throw new ArgumentOutOfRangeException(nameof(inputCount));

        // 260925Codex: 共通の配置を使いつつ、既存の初期化順序を保持する。
        // 261001Codex: 既定の幅で、既存の研究用呼び出しと旧保存headの互換性を保つ。
        public SoftmaxOcnHead(Random random, int inputCount = 3)
        {
            InputCount = inputCount;
            _w = new float[GetWeightCount(inputCount)];
            _g = new float[_w.Length];
            _m = new float[_w.Length];
            _v = new float[_w.Length];
            int offset = 0;
            foreach ((int fanIn, int fanOut) in new[] { (InputCount, H1), (H1, H2), (H2, Out) })
            {
                float limit = MathF.Sqrt(6f / (fanIn + fanOut));
                for (int i = 0; i < fanIn * fanOut; i++) _w[offset + i] = (float)(random.NextDouble() * 2 - 1) * limit;
                offset += fanIn * fanOut + fanOut;
            }
        }

        public float[] Snapshot() => (float[])_w.Clone();
        // 260924Codex: 不完全なheadを黙って受け入れず、途中で切れた成果物や別形式の成果物を拒否する。
        public void Restore(float[] weights)
        {
            ArgumentNullException.ThrowIfNull(weights);
            if (weights.Length != _w.Length || !Array.TrueForAll(weights, float.IsFinite))
                throw new InvalidDataException("Softmax＋OCN判定器の重みが不正です。");
            Array.Copy(weights, _w, _w.Length);
        }

        public double Predict(float[] input)
        {
            var (_, _, logits) = Forward(input);
            double a = logits[KnownOutput], b = logits[UnknownOutput], max = Math.Max(a, b);
            double ea = Math.Exp(a - max), eb = Math.Exp(b - max);
            return eb / (ea + eb);
        }

        // 260925Codex: 保存した重み配置と同じオフセットで、各層を読む。
        private (float[] H1Act, float[] H2Act, float[] Logits) Forward(float[] x)
        {
            // 261001Codex: モデルと入力の契約が不一致の場合に、距離特徴量を暗黙に捨てない。
            if (x.Length != InputCount || !Array.TrueForAll(x, float.IsFinite))
                throw new ArgumentException("OCN判定器の入力が不正です。", nameof(x));
            var h1 = new float[H1];
            var h2 = new float[H2];
            var o = new float[Out];
            for (int j = 0; j < H1; j++)
            {
                float sum = _w[FirstBias + j];
                for (int i = 0; i < InputCount; i++) sum += x[i] * _w[FirstWeights + i * H1 + j];
                h1[j] = Math.Max(0, sum);
            }
            for (int j = 0; j < H2; j++)
            {
                float sum = _w[SecondBias + j];
                for (int i = 0; i < H1; i++) sum += h1[i] * _w[SecondWeights + i * H2 + j];
                h2[j] = Math.Max(0, sum);
            }
            for (int j = 0; j < Out; j++)
            {
                float sum = _w[OutputBias + j];
                for (int i = 0; i < H2; i++) sum += h2[i] * _w[OutputWeights + i * Out + j];
                o[j] = sum;
            }
            return (h1, h2, o);
        }

        // 260925Codex: 共通の層配置に従って勾配を累積する。
        // 261001Claude: targets は UnknownOutput／KnownOutput の出力番号で渡します。この対応を素の 0／1 で書くと
        //   呼び出し側ごとに向きが食い違い、実際に学習側と推論側が逆を向いていました。
        public double TrainStep(List<float[]> inputs, List<int> targets)
        {
            Array.Clear(_g);
            double loss = 0;
            float scale = 1f / inputs.Count;
            for (int n = 0; n < inputs.Count; n++)
            {
                float[] x = inputs[n];
                var (h1, h2, o) = Forward(x);
                float max = Math.Max(o[0], o[1]);
                float e0 = MathF.Exp(o[0] - max), e1 = MathF.Exp(o[1] - max), sum = e0 + e1;
                float p0 = e0 / sum, p1 = e1 / sum;
                loss -= Math.Log(Math.Max(targets[n] == 0 ? p0 : p1, 1e-12f));
                float[] dO = [(p0 - (targets[n] == 0 ? 1 : 0)) * scale, (p1 - (targets[n] == 1 ? 1 : 0)) * scale];
                var dH2 = new float[H2];
                for (int j = 0; j < Out; j++)
                {
                    _g[OutputBias + j] += dO[j];
                    for (int i = 0; i < H2; i++)
                    {
                        _g[OutputWeights + i * Out + j] += h2[i] * dO[j];
                        dH2[i] += _w[OutputWeights + i * Out + j] * dO[j];
                    }
                }
                var dH1 = new float[H1];
                for (int j = 0; j < H2; j++)
                {
                    if (h2[j] <= 0) continue;
                    _g[SecondBias + j] += dH2[j];
                    for (int i = 0; i < H1; i++)
                    {
                        _g[SecondWeights + i * H2 + j] += h1[i] * dH2[j];
                        dH1[i] += _w[SecondWeights + i * H2 + j] * dH2[j];
                    }
                }
                for (int j = 0; j < H1; j++)
                {
                    if (h1[j] <= 0) continue;
                    _g[FirstBias + j] += dH1[j];
                    for (int i = 0; i < InputCount; i++) _g[FirstWeights + i * H1 + j] += x[i] * dH1[j];
                }
            }
            // Adam（β1 0.9、β2 0.999、ε 1e-7はKerasと同じ）。エピソードごとに1回更新する。
            _step++;
            float lr = LearningRate * MathF.Sqrt(1 - MathF.Pow(0.999f, _step)) / (1 - MathF.Pow(0.9f, _step));
            for (int i = 0; i < _w.Length; i++)
            {
                _m[i] = 0.9f * _m[i] + 0.1f * _g[i];
                _v[i] = 0.999f * _v[i] + 0.001f * _g[i] * _g[i];
                _w[i] -= lr * _m[i] / (MathF.Sqrt(_v[i]) + 1e-7f);
            }
            return loss / inputs.Count;
        }
    }
}
