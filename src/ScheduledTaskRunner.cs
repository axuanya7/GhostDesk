using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Threading;

namespace GhostDesk
{
    internal sealed class ScheduledTaskRunner : IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan TriggerGrace = TimeSpan.FromMinutes(2);

        private readonly AppConfig _config;
        private readonly Func<ScheduledTaskConfig, bool> _runTask;
        private readonly Dictionary<string, PendingTask> _pendingTasks;
        private DispatcherTimer _timer;
        private bool _checking;

        private sealed class PendingTask
        {
            public ScheduledTaskConfig Task;
            public DateTime OccurrenceUtc;
            public ScheduledTaskCountdownWindow Window;
        }

        public ScheduledTaskRunner(
            AppConfig config,
            Func<ScheduledTaskConfig, bool> runTask)
        {
            _config = config;
            _runTask = runTask;
            _pendingTasks = new Dictionary<string, PendingTask>(
                StringComparer.Ordinal);
        }

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            _timer = new DispatcherTimer(DispatcherPriority.Send);
            _timer.Interval = CheckInterval;
            _timer.Tick += Timer_Tick;
            _timer.Start();
            CheckTasks();
        }

        public void CheckNow()
        {
            CheckTasks();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            CheckTasks();
        }

        private void CheckTasks()
        {
            if (_checking)
            {
                return;
            }

            _checking = true;
            try
            {
                DateTime now = DateTime.Now;
                PrunePendingTasks();

                if (_config.ScheduledTasks == null ||
                    _config.ScheduledTasks.Count == 0)
                {
                    return;
                }

                for (int index = 0; index < _config.ScheduledTasks.Count; index++)
                {
                    ScheduledTaskConfig task = _config.ScheduledTasks[index];
                    if (task == null)
                    {
                        continue;
                    }

                    DateTime occurrenceUtc = GetDueOccurrence(
                        task,
                        now,
                        TriggerGrace);
                    if (occurrenceUtc == DateTime.MinValue)
                    {
                        continue;
                    }

                    string taskId = task.Id ?? "";
                    PendingTask pending;
                    if (_pendingTasks.TryGetValue(taskId, out pending))
                    {
                        if (pending.OccurrenceUtc == occurrenceUtc &&
                            object.ReferenceEquals(pending.Task, task))
                        {
                            continue;
                        }

                        ClosePending(pending);
                        _pendingTasks.Remove(taskId);
                    }

                    int countdownSeconds = GetEffectiveCountdownSeconds(task);
                    if (countdownSeconds <= 0)
                    {
                        ExecuteOccurrence(task, occurrenceUtc);
                        continue;
                    }

                    PendingTask created = new PendingTask();
                    created.Task = task;
                    created.OccurrenceUtc = occurrenceUtc;
                    PendingTask captured = created;
                    created.Window = new ScheduledTaskCountdownWindow(
                        task,
                        occurrenceUtc.ToLocalTime(),
                        delegate
                        {
                            ExecutePending(captured);
                        },
                        delegate
                        {
                            CancelPending(captured);
                        });
                    _pendingTasks[taskId] = created;
                    created.Window.Show();
                }
            }
            finally
            {
                _checking = false;
            }
        }

        private void PrunePendingTasks()
        {
            if (_pendingTasks.Count == 0)
            {
                return;
            }

            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, PendingTask> pair in _pendingTasks)
            {
                PendingTask pending = pair.Value;
                ScheduledTaskConfig current = FindTaskById(pair.Key);
                if (!object.ReferenceEquals(pending.Task, current) ||
                    current == null ||
                    !current.Enabled)
                {
                    remove.Add(pair.Key);
                }
            }

            for (int index = 0; index < remove.Count; index++)
            {
                PendingTask pending;
                if (_pendingTasks.TryGetValue(remove[index], out pending))
                {
                    ClosePending(pending);
                    _pendingTasks.Remove(remove[index]);
                }
            }
        }

        private ScheduledTaskConfig FindTaskById(string taskId)
        {
            if (_config.ScheduledTasks == null)
            {
                return null;
            }

            for (int index = 0; index < _config.ScheduledTasks.Count; index++)
            {
                ScheduledTaskConfig task = _config.ScheduledTasks[index];
                if (task != null &&
                    string.Equals(task.Id ?? "", taskId ?? "", StringComparison.Ordinal))
                {
                    return task;
                }
            }

            return null;
        }

        private void ExecutePending(PendingTask pending)
        {
            if (pending == null)
            {
                return;
            }

            string taskId = pending.Task == null ? "" : (pending.Task.Id ?? "");
            PendingTask current;
            if (!_pendingTasks.TryGetValue(taskId, out current) ||
                !object.ReferenceEquals(current, pending))
            {
                return;
            }

            _pendingTasks.Remove(taskId);
            pending.Window = null;
            ExecuteOccurrence(pending.Task, pending.OccurrenceUtc);
        }

        private void CancelPending(PendingTask pending)
        {
            if (pending == null)
            {
                return;
            }

            string taskId = pending.Task == null ? "" : (pending.Task.Id ?? "");
            PendingTask current;
            if (!_pendingTasks.TryGetValue(taskId, out current) ||
                !object.ReferenceEquals(current, pending))
            {
                return;
            }

            _pendingTasks.Remove(taskId);
            pending.Window = null;
            RecordAttempt(pending.Task, pending.OccurrenceUtc);
        }

        private void ExecuteOccurrence(
            ScheduledTaskConfig task,
            DateTime occurrenceUtc)
        {
            bool accepted = false;
            try
            {
                accepted = _runTask == null || _runTask(task);
            }
            catch
            {
                accepted = false;
            }

            RecordAttempt(task, occurrenceUtc);
        }

        private void RecordAttempt(
            ScheduledTaskConfig task,
            DateTime occurrenceUtc)
        {
            if (task == null)
            {
                return;
            }

            task.LastRunUtc = occurrenceUtc.ToUniversalTime().ToString(
                "o",
                CultureInfo.InvariantCulture);
            if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeOnce,
                    StringComparison.Ordinal))
            {
                task.Enabled = false;
            }

            ConfigStore.Save(_config);
        }

        private void ClosePending(PendingTask pending)
        {
            if (pending != null && pending.Window != null)
            {
                pending.Window.CloseSilently();
                pending.Window = null;
            }
        }

        internal static int GetEffectiveCountdownSeconds(ScheduledTaskConfig task)
        {
            if (task == null)
            {
                return 0;
            }

            int seconds = Math.Max(0, Math.Min(600, task.CountdownSeconds));
            if (string.Equals(
                    task.TaskType,
                    ScheduledTaskConfig.TypeShutdown,
                    StringComparison.Ordinal))
            {
                seconds = Math.Max(5, seconds);
            }

            return seconds;
        }

        internal static DateTime GetDueOccurrence(
            ScheduledTaskConfig task,
            DateTime nowLocal,
            TimeSpan grace)
        {
            if (task == null || !task.Enabled)
            {
                return DateTime.MinValue;
            }

            bool shutdown = string.Equals(
                task.TaskType,
                ScheduledTaskConfig.TypeShutdown,
                StringComparison.Ordinal);
            if (!shutdown && string.IsNullOrWhiteSpace(task.Target))
            {
                return DateTime.MinValue;
            }

            DateTime firstRunUtc = ResolveFirstRunUtc(task);
            if (firstRunUtc == DateTime.MinValue)
            {
                return DateTime.MinValue;
            }

            DateTime nowUtc = nowLocal.ToUniversalTime();
            DateTime lastRunUtc = ParseUtc(task.LastRunUtc);
            int countdownSeconds = GetEffectiveCountdownSeconds(task);
            DateTime graceStartUtc = nowUtc.Subtract(grace);
            DateTime candidateUtc;

            if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeOnce,
                    StringComparison.Ordinal))
            {
                candidateUtc = firstRunUtc;
                if (lastRunUtc >= candidateUtc)
                {
                    return DateTime.MinValue;
                }
            }
            else if (string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeInterval,
                    StringComparison.Ordinal))
            {
                TimeSpan interval = TimeSpan.FromMinutes(
                    Math.Max(1, task.IntervalMinutes));
                candidateUtc = lastRunUtc >= firstRunUtc
                    ? lastRunUtc.Add(interval)
                    : firstRunUtc;

                if (candidateUtc < graceStartUtc)
                {
                    long intervalTicks = interval.Ticks;
                    long missingTicks = graceStartUtc.Ticks - candidateUtc.Ticks;
                    long steps = (missingTicks + intervalTicks - 1) / intervalTicks;
                    candidateUtc = candidateUtc.AddTicks(steps * intervalTicks);
                }
            }
            else
            {
                TimeSpan period = string.Equals(
                    task.ScheduleMode,
                    ScheduledTaskConfig.ModeWeekly,
                    StringComparison.Ordinal)
                    ? TimeSpan.FromDays(7)
                    : TimeSpan.FromDays(1);
                candidateUtc = GetPeriodicCandidate(
                    firstRunUtc,
                    lastRunUtc,
                    nowUtc,
                    graceStartUtc,
                    period);
            }

            DateTime occurrenceReminderUtc =
                candidateUtc.AddSeconds(-countdownSeconds);
            if (nowUtc < occurrenceReminderUtc || nowUtc > candidateUtc.Add(grace))
            {
                return DateTime.MinValue;
            }

            if (lastRunUtc >= candidateUtc)
            {
                return DateTime.MinValue;
            }

            return candidateUtc;
        }

        private static DateTime GetPeriodicCandidate(
            DateTime firstRunUtc,
            DateTime lastRunUtc,
            DateTime nowUtc,
            DateTime graceStartUtc,
            TimeSpan period)
        {
            if (nowUtc < firstRunUtc)
            {
                return firstRunUtc;
            }

            long periodTicks = period.Ticks;
            long elapsedTicks = Math.Max(0, nowUtc.Ticks - firstRunUtc.Ticks);
            long periods = elapsedTicks / periodTicks;
            DateTime candidate = firstRunUtc.AddTicks(periods * periodTicks);

            if (candidate < graceStartUtc)
            {
                long missingTicks = graceStartUtc.Ticks - candidate.Ticks;
                long steps = (missingTicks + periodTicks - 1) / periodTicks;
                candidate = candidate.AddTicks(steps * periodTicks);
            }

            if (lastRunUtc >= firstRunUtc)
            {
                while (candidate <= lastRunUtc)
                {
                    candidate = candidate.Add(period);
                }
            }

            return candidate;
        }

        internal static DateTime CalculateFirstRunUtc(
            string mode,
            string time,
            int dayOfWeek,
            DateTime nowLocal)
        {
            DateTime parsedTime;
            if (!DateTime.TryParseExact(
                    (time ?? "").Trim(),
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out parsedTime))
            {
                parsedTime = nowLocal.Date.AddHours(9);
            }

            DateTime firstRun = nowLocal.Date.Add(parsedTime.TimeOfDay);
            if (string.Equals(
                    mode,
                    ScheduledTaskConfig.ModeWeekly,
                    StringComparison.Ordinal))
            {
                int delta = (dayOfWeek - (int)nowLocal.DayOfWeek + 7) % 7;
                firstRun = firstRun.AddDays(delta);
            }

            if (firstRun <= nowLocal)
            {
                firstRun = firstRun.AddDays(
                    string.Equals(
                        mode,
                        ScheduledTaskConfig.ModeWeekly,
                        StringComparison.Ordinal)
                        ? 7
                        : 1);
            }

            return firstRun.ToUniversalTime();
        }

        private static DateTime ResolveFirstRunUtc(ScheduledTaskConfig task)
        {
            DateTime firstRunUtc = ParseUtc(task.FirstRunUtc);
            if (firstRunUtc != DateTime.MinValue)
            {
                return firstRunUtc;
            }

            DateTime createdUtc = ParseUtc(task.CreatedUtc);
            DateTime reference = createdUtc == DateTime.MinValue
                ? DateTime.Now
                : createdUtc.ToLocalTime();
            return CalculateFirstRunUtc(
                task.ScheduleMode,
                task.Time,
                task.DayOfWeek,
                reference);
        }

        private static DateTime ParseUtc(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        public void Dispose()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }

            foreach (KeyValuePair<string, PendingTask> pair in _pendingTasks)
            {
                ClosePending(pair.Value);
            }
            _pendingTasks.Clear();
        }
    }
}
