using UnityEngine;

namespace JetFighter.Build
{
    /// <summary>
    /// Tracks the frame-time statistics that actually predict how a build
    /// feels.
    ///
    /// Worst frame and 1% low, not the average. The average is the wrong
    /// statistic: a run at a steady 60fps with one 200ms hitch every ten
    /// seconds averages fine and feels broken, and it is the hitch a player
    /// reports. A profiler capture shows this too, but only while someone is
    /// watching -- this runs in every build, so a regression introduced after
    /// the profiling pass has somewhere to show up.
    ///
    /// Allocation-free by construction: a fixed ring buffer, no lists, no
    /// LINQ. A probe that allocated per frame would be measuring itself.
    /// </summary>
    public class FrameBudgetProbe : MonoBehaviour
    {
        [Tooltip("Frames kept for the percentile. 600 is ten seconds at 60fps.")]
        [Min(60)]
        [SerializeField] private int windowFrames = 600;

        [Tooltip("Frame time above which a frame counts as a hitch, in milliseconds.")]
        [Min(1f)]
        [SerializeField] private float hitchThresholdMs = 33f;

        private float[] window;
        private int writeIndex;
        private int filled;

        /// <summary>Frames sampled since the last reset.</summary>
        public int SampleCount { get; private set; }

        /// <summary>Longest frame seen, in milliseconds.</summary>
        public float WorstFrameMs { get; private set; }

        /// <summary>Frames over the hitch threshold. The number a player would describe.</summary>
        public int HitchCount { get; private set; }

        public float HitchThresholdMs
        {
            get => hitchThresholdMs;
            set => hitchThresholdMs = Mathf.Max(1f, value);
        }

        private void Awake()
        {
            EnsureWindow();
        }

        private void EnsureWindow()
        {
            if (window == null || window.Length != Mathf.Max(60, windowFrames))
            {
                window = new float[Mathf.Max(60, windowFrames)];
                writeIndex = 0;
                filled = 0;
            }
        }

        private void Update()
        {
            Sample(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Records one frame. Takes the delta so a test can feed a known
        /// distribution rather than hoping the editor produces one.
        /// </summary>
        public void Sample(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return;
            }
            EnsureWindow();

            float ms = deltaSeconds * 1000f;
            window[writeIndex] = ms;
            writeIndex = (writeIndex + 1) % window.Length;
            filled = Mathf.Min(filled + 1, window.Length);
            SampleCount++;

            if (ms > WorstFrameMs)
            {
                WorstFrameMs = ms;
            }
            if (ms > hitchThresholdMs)
            {
                HitchCount++;
            }
        }

        /// <summary>
        /// The 1% low frame time over the window: the mean of the worst 1% of
        /// frames.
        ///
        /// Not the single worst frame, which one stall can dominate, and not
        /// the average, which hides every stall. This is the number that
        /// tracks "does it feel smooth".
        /// </summary>
        public float OnePercentLowMs()
        {
            if (filled == 0)
            {
                return 0f;
            }
            int worstCount = Mathf.Max(1, filled / 100);

            // Selection over a copy rather than a sort of the live buffer: the
            // buffer is still being written, and sorting it would reorder the
            // ring.
            var sorted = new float[filled];
            System.Array.Copy(window, sorted, filled);
            System.Array.Sort(sorted);

            float total = 0f;
            for (int i = filled - worstCount; i < filled; i++)
            {
                total += sorted[i];
            }
            return total / worstCount;
        }

        /// <summary>Median frame time over the window.</summary>
        public float MedianMs()
        {
            if (filled == 0)
            {
                return 0f;
            }
            var sorted = new float[filled];
            System.Array.Copy(window, sorted, filled);
            System.Array.Sort(sorted);
            return sorted[filled / 2];
        }

        /// <summary>Clears the statistics. Called at the start of a measured run.</summary>
        public void ResetStatistics()
        {
            writeIndex = 0;
            filled = 0;
            SampleCount = 0;
            WorstFrameMs = 0f;
            HitchCount = 0;
        }
    }
}
