using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxMultiByJayy
{
    internal sealed class ProcessPerformance : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

        [DllImport("kernel32.dll")]
        private static extern uint GetPriorityClass(IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern bool SetPriorityClass(IntPtr process, uint priority);

        internal static uint ForegroundPid()
        {
            uint pid;
            return GetWindowThreadProcessId(GetForegroundWindow(), out pid) == 0 ? 0 : pid;
        }

        internal static bool ShouldEconomize(bool enabled, uint foreground, int pid, bool primary)
        {
            return enabled && foreground != 0 && foreground != (uint)pid && !primary;
        }

        private const uint NormalPriority = 0x20;
        private const uint BelowNormalPriority = 0x4000;
        private readonly Process process;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TimeSpan lastElapsed;
        private TimeSpan lastCpu;
        private bool hasSample;
        private bool changedPriority;

        internal ProcessPerformance(Process process)
        {
            this.process = process;
            Started = process.StartTime;
        }

        internal DateTime Started { get; private set; }

        internal double? CpuPercent { get; private set; }
        internal long MemoryBytes { get; private set; }
        internal bool IsEconomical { get { return changedPriority; } }

        internal void Update(bool economical)
        {
            process.Refresh();
            var elapsed = clock.Elapsed;
            var cpu = process.TotalProcessorTime;
            var interval = (elapsed - lastElapsed).TotalMilliseconds;
            CpuPercent = hasSample && interval > 0
                ? (double?)Math.Max(0, Math.Min(100, (cpu - lastCpu).TotalMilliseconds * 100 /
                    interval / Environment.ProcessorCount))
                : null;
            lastElapsed = elapsed;
            lastCpu = cpu;
            hasSample = true;
            MemoryBytes = process.WorkingSet64;
            SetEconomical(economical);
        }

        internal void SetEconomical(bool enabled)
        {
            if (enabled == changedPriority) return;
            uint current = GetPriorityClass(process.Handle);
            if (enabled)
            {
                // Leave any priority selected by Windows or the user in place.
                changedPriority = current == NormalPriority && SetPriorityClass(process.Handle, BelowNormalPriority);
            }
            else if (current != BelowNormalPriority || SetPriorityClass(process.Handle, NormalPriority))
            {
                changedPriority = false;
            }
        }

        public void Dispose()
        {
            try { SetEconomical(false); }
            finally { process.Dispose(); }
        }
    }
}
