using System;
using System.Collections.Generic;
using System.Linq;
using Tensorflow.NumPy;
using static Tensorflow.Binding;

namespace MineraScope
{
    // 260430Codex: ラベルエンコードと train/test 分割を DeepLearning 本体から分離します。
    internal static class DeepLearningDataSplitter
    {
        // 260430Codex: 鉱物名ラベルを TensorFlow に渡す整数ラベルへ変換します。
        public static (NDArray EncodedLabels, Dictionary<string, int> Encoder) EncodeLabels(List<string> labels)
        {
            var uniqueLabels = labels.Distinct().OrderBy(x => x).ToList();
            var encoder = uniqueLabels.Select((label, index) => new { label, index })
                                      .ToDictionary(x => x.label, x => x.index);

            int[] encoded = labels.Select(label => encoder[label]).ToArray();
            return (np.array(encoded), encoder);
        }

        // 260430Codex: 分類モデル用にスペクトル行列と整数ラベルを固定 seed で分割します。
        public static (NDArray XTrain, NDArray XTest, NDArray YTrain, NDArray YTest) TrainTestSplitClassification(
            NDArray spectra,
            NDArray labels,
            float testSize = 0.2f,
            int randomState = 42)
        {
            int numSamples = (int)spectra.shape[0];
            int testCount = (int)(numSamples * testSize);
            int trainCount = numSamples - testCount;

            var indices = Enumerable.Range(0, numSamples).ToArray();
            var rng = new Random(randomState);

            for (int i = numSamples - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                int temp = indices[i];
                indices[i] = indices[j];
                indices[j] = temp;
            }

            var trainIndices = indices.Take(trainCount).ToArray();
            var testIndices = indices.Skip(trainCount).ToArray();

            var xArray = spectra.ToArray<float>();
            var yArray = labels.ToArray<int>();

            float[,] xTrainArray = new float[trainCount, SpectrumDataLoader.SpectrumLength];
            float[,] xTestArray = new float[testCount, SpectrumDataLoader.SpectrumLength];
            int[] yTrainArray = new int[trainCount];
            int[] yTestArray = new int[testCount];

            for (int i = 0; i < trainCount; i++)
            {
                int idx = trainIndices[i];
                yTrainArray[i] = yArray[idx];
                for (int j = 0; j < SpectrumDataLoader.SpectrumLength; j++)
                    xTrainArray[i, j] = xArray[idx * SpectrumDataLoader.SpectrumLength + j];
            }

            for (int i = 0; i < testCount; i++)
            {
                int idx = testIndices[i];
                yTestArray[i] = yArray[idx];
                for (int j = 0; j < SpectrumDataLoader.SpectrumLength; j++)
                    xTestArray[i, j] = xArray[idx * SpectrumDataLoader.SpectrumLength + j];
            }

            return (np.array(xTrainArray), np.array(xTestArray),
                    np.array(yTrainArray), np.array(yTestArray));
        }

        // 260430Codex: 回帰モデル用にスペクトル行列と端成分ラベル行列を固定 seed で分割します。
        public static (NDArray XTrain, NDArray XTest, NDArray YTrain, NDArray YTest) TrainTestSplitRegression(
           NDArray spectra,
           NDArray labels,
           float testSize = 0.2f,
           int randomState = 42)
        {
            int nSamples = (int)spectra.shape[0];
            int nFeatures = (int)spectra.shape[1];
            int nLabels = (int)labels.shape[1];

            int nTest = (int)(nSamples * testSize);
            int nTrain = nSamples - nTest;

            var rnd = new Random(randomState);
            int[] indices = Enumerable.Range(0, nSamples)
                                      .OrderBy(_ => rnd.Next())
                                      .ToArray();

            float[,] xTrainArr = new float[nTrain, nFeatures];
            float[,] xTestArr = new float[nTest, nFeatures];
            float[,] yTrainArr = new float[nTrain, nLabels];
            float[,] yTestArr = new float[nTest, nLabels];

            var xAll = spectra.ToArray<float>();
            var yAll = labels.ToArray<float>();

            for (int i = 0; i < nTrain; i++)
            {
                int idx = indices[i];
                for (int j = 0; j < nFeatures; j++)
                    xTrainArr[i, j] = xAll[idx * nFeatures + j];

                for (int k = 0; k < nLabels; k++)
                    yTrainArr[i, k] = yAll[idx * nLabels + k];
            }

            for (int i = 0; i < nTest; i++)
            {
                int idx = indices[nTrain + i];
                for (int j = 0; j < nFeatures; j++)
                    xTestArr[i, j] = xAll[idx * nFeatures + j];

                for (int k = 0; k < nLabels; k++)
                    yTestArr[i, k] = yAll[idx * nLabels + k];
            }

            return (
                np.array(xTrainArr),
                np.array(xTestArr),
                np.array(yTrainArr),
                np.array(yTestArr)
            );
        }
    }
}
