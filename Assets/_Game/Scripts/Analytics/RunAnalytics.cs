using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Analytics
{
    /// <summary>
    /// Owns the run-lifecycle events: run_end and death_cause.
    ///
    /// An analytics component rather than a gameplay one, per the spec's
    /// "this phase adds calls into existing classes, not new gameplay
    /// systems". Nothing here affects a run; removing it changes no
    /// behaviour, which is the property that lets it be added late and
    /// removed if consent is withheld.
    ///
    /// run_end and death_cause are separate events on purpose even though
    /// they fire together. They answer different questions -- "how long do
    /// runs last" versus "what kills people" -- and merging them means every
    /// retention query has to filter on a death reason it does not care
    /// about.
    /// </summary>
    public class RunAnalytics : MonoBehaviour
    {
        private float runSeconds;
        private bool running;
        private bool ended;

        /// <summary>Seconds the current run has lasted.</summary>
        public float RunSeconds => runSeconds;

        public bool IsRunning => running;

        /// <summary>Starts timing. Called when control passes to the player.</summary>
        public void BeginRun()
        {
            runSeconds = 0f;
            running = true;
            ended = false;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>Advances the run clock. Takes deltaTime so a test need not wait.</summary>
        public void Tick(float deltaTime)
        {
            if (running && deltaTime > 0f)
            {
                runSeconds += deltaTime;
            }
        }

        /// <summary>
        /// Reports the end of a run.
        ///
        /// Fires once. A game-over screen that can be reached twice -- which
        /// happens with a rewarded continue -- would otherwise double every
        /// run-length statistic, and the average would silently halve.
        /// </summary>
        public bool EndRun(string cause, int score)
        {
            if (!running || ended)
            {
                return false;
            }
            ended = true;
            running = false;

            AnalyticsService.LogEvent(AnalyticsEvents.RunEnd, new Dictionary<string, object>
            {
                { AnalyticsEvents.ParamDurationSeconds, Mathf.RoundToInt(runSeconds) },
                { AnalyticsEvents.ParamScore, score },
            });
            AnalyticsService.LogEvent(AnalyticsEvents.DeathCause,
                AnalyticsEvents.ParamCause, string.IsNullOrWhiteSpace(cause) ? "unknown" : cause);
            return true;
        }

        /// <summary>
        /// Resumes after a rewarded continue.
        ///
        /// The run clock keeps going rather than restarting: a continued run
        /// is one run to the player, and splitting it into two would make the
        /// continue look like it shortened sessions.
        /// </summary>
        public void ResumeAfterContinue()
        {
            running = true;
            ended = false;
        }
    }
}
