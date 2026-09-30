using Tensorflow;
using static Tensorflow.Binding;

namespace MineraScope
{
    // 260917Codex: Shared graph loss preserves sparse CE on singleton rows and sums acceptable probability mass on overlap rows.
    internal static class PartialLabelLoss
    {
        public static Tensor PerSample(Tensor logits, Tensor source, Tensor alternative, int classCount)
        {
            var ordinary = tf.nn.sparse_softmax_cross_entropy_with_logits(source, logits);
            var mask = tf.maximum(tf.one_hot(source, classCount), tf.one_hot(alternative, classCount));
            var accepted = tf.where(tf.greater(mask, 0f), logits, tf.fill(tf.shape(logits), float.NegativeInfinity));
            var partial = tf.reduce_logsumexp(logits, axis: 1) - tf.reduce_logsumexp(accepted, axis: 1);
            return tf.where(tf.equal(source, alternative), ordinary, partial);
        }

        public static Tensor Correct(Tensor logits, Tensor source, Tensor alternative)
        {
            var prediction = tf.cast(tf.arg_max(logits, 1), tf.int32);
            return tf.logical_or(tf.equal(prediction, source), tf.equal(prediction, alternative));
        }
    }
}
