namespace AudioDual.Core.Diagnostics
{
    /// <summary>
    /// Structured logging seam for the engine. Exists so call sites record a level,
    /// a category, and the originating exception (when there is one) instead of an
    /// ad-hoc string written straight to the console, which was previously the only
    /// error-reporting path and was invisible to end users after a release build.
    /// </summary>
    public interface IAppLogger
    {
        void LogInformation(string category, string message);

        void LogWarning(string category, string message);

        void LogError(string category, string message, Exception? exception = null);
    }
}
