using System;
using System.Collections.Generic;
using UnityEngine;

namespace JetFighter.Analytics
{
    /// <summary>
    /// The one way anything in the game reports an event.
    ///
    /// Firebase sits behind IAnalyticsBackend for a reason specific to
    /// analytics: the criterion is "every listed event appears in the
    /// dashboard on a real run", and a dashboard check is slow, manual and
    /// only possible after a build ships. A recording backend makes the
    /// wiring assertable in a unit test, so the dashboard visit confirms the
    /// pipe rather than discovering the missing call.
    ///
    /// Nothing here throws. An analytics failure that takes down a run is a
    /// worse outcome than losing a data point, and it is the failure mode that
    /// makes teams rip analytics out entirely.
    /// </summary>
    public static class AnalyticsService
    {
        /// <summary>The slice of an analytics SDK this needs. Deliberately tiny.</summary>
        public interface IAnalyticsBackend
        {
            void LogEvent(string name, IReadOnlyDictionary<string, object> parameters);
        }

        private static IAnalyticsBackend backend;

        /// <summary>Events dropped as invalid. Non-zero means a name or payload bug.</summary>
        public static int DroppedEvents { get; private set; }

        /// <summary>Events the backend accepted.</summary>
        public static int LoggedEvents { get; private set; }

        /// <summary>Whether a backend is attached. False in the editor unless a test attaches one.</summary>
        public static bool IsEnabled => backend != null;

        public static void Bind(IAnalyticsBackend analyticsBackend)
        {
            backend = analyticsBackend;
        }

        /// <summary>Detaches, and resets the counters. For tests and for a privacy opt-out.</summary>
        public static void Reset()
        {
            backend = null;
            DroppedEvents = 0;
            LoggedEvents = 0;
        }

        /// <summary>
        /// Reports one event. Returns false when it was not sent, so a test
        /// can assert wiring without a dashboard.
        ///
        /// An unbound backend is not a failure: analytics is absent in the
        /// editor and in a build with consent withheld, and neither is a bug.
        /// </summary>
        public static bool LogEvent(string name, IReadOnlyDictionary<string, object> parameters = null)
        {
            if (!IsValidName(name))
            {
                // Firebase drops these silently. Counting them here is the
                // difference between a bug and a mystery.
                DroppedEvents++;
                Debug.LogWarning($"[Analytics] refusing to send invalid event name '{name}'");
                return false;
            }
            if (parameters != null && parameters.Count > AnalyticsEvents.MaxParametersPerEvent)
            {
                DroppedEvents++;
                Debug.LogWarning($"[Analytics] '{name}' has {parameters.Count} parameters, over the limit");
                return false;
            }
            if (backend == null)
            {
                return false;
            }

            try
            {
                backend.LogEvent(name, parameters ?? new Dictionary<string, object>());
                LoggedEvents++;
                return true;
            }
            catch (Exception e)
            {
                // An analytics failure that takes down a run is worse than a
                // lost data point, and it is the failure that makes teams rip
                // analytics out entirely.
                DroppedEvents++;
                Debug.LogWarning($"[Analytics] backend threw on '{name}': {e.Message}");
                return false;
            }
        }

        /// <summary>Convenience for the common single-parameter case.</summary>
        public static bool LogEvent(string name, string parameterKey, object value)
        {
            return LogEvent(name, new Dictionary<string, object> { { parameterKey, value } });
        }

        /// <summary>
        /// Whether a name is one Firebase will accept.
        ///
        /// Public so the event constants can be checked in a test rather than
        /// discovered to be too long once the dashboard is missing a funnel
        /// step.
        /// </summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > AnalyticsEvents.MaxNameLength)
            {
                return false;
            }
            if (!char.IsLetter(name[0]))
            {
                // Firebase requires a leading letter; a leading digit or
                // underscore is dropped without a message.
                return false;
            }
            foreach (char c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                {
                    return false;
                }
                if (char.IsUpper(c))
                {
                    // Not a Firebase rule, a consistency one: run_start and
                    // Run_Start are two events on the dashboard, and nobody
                    // notices until the numbers are half of what they should
                    // be.
                    return false;
                }
            }
            return true;
        }
    }
}
