using System.Runtime.InteropServices;

namespace AudioDual.Core.Platform
{
    /// <summary>
    /// Registers a thread with Windows' Multimedia Class Scheduler Service (MMCSS),
    /// the same mechanism professional audio applications and Windows' own audio
    /// engine use to get glitch-resistant scheduling for real-time audio threads.
    /// This is a real OS-level scheduling boost, not a .NET ThreadPriority hint that
    /// the runtime can still deprioritize under load — it's the correct replacement
    /// for the old code's reflection reach into Task's private thread handle.
    /// </summary>
    public sealed class MmcssThreadBooster : IDisposable
    {
        private const string ProAudioTaskName = "Pro Audio";

        [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, AvThreadPriority priority);

        private enum AvThreadPriority
        {
            Critical = 4
        }

        private IntPtr _avrtHandle = IntPtr.Zero;
        private readonly Diagnostics.IAppLogger _logger;

        public MmcssThreadBooster(Diagnostics.IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Registers the calling thread with the "Pro Audio" MMCSS task. Must be
        /// called from the thread that is to receive the scheduling boost — MMCSS
        /// registration is per-thread, not process-wide. Returns true if the OS
        /// granted the registration; false means the thread continues to run at its
        /// normal .NET priority, which is a safe, if less optimal, fallback.
        /// </summary>
        public bool TryBoostCurrentThread()
        {
            uint taskIndex = 0;
            _avrtHandle = AvSetMmThreadCharacteristicsW(ProAudioTaskName, ref taskIndex);

            if (_avrtHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("MmcssThreadBooster", $"AvSetMmThreadCharacteristicsW failed (Win32 error {error}); continuing without MMCSS scheduling.");
                return false;
            }

            if (!AvSetMmThreadPriority(_avrtHandle, AvThreadPriority.Critical))
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("MmcssThreadBooster", $"AvSetMmThreadPriority failed (Win32 error {error}); thread is MMCSS-registered but not at critical priority.");
            }

            return true;
        }

        public void Dispose()
        {
            if (_avrtHandle != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(_avrtHandle);
                _avrtHandle = IntPtr.Zero;
            }
        }
    }
}
