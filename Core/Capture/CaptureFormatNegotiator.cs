using NAudio.CoreAudioApi;

namespace AudioDual.Core.Capture
{
    /// <summary>
    /// Resolves which render endpoint to loopback-capture from. WASAPI loopback
    /// capture always uses the captured device's own mix format — there is no
    /// independent "capture format" to negotiate the way there is on the output
    /// side — so what this actually negotiates is which default endpoint role to
    /// capture from if the preferred one is unavailable, since a system can have
    /// different default devices assigned per role.
    /// </summary>
    public sealed class CaptureFormatNegotiator
    {
        private static readonly Role[] RoleFallbackOrder =
        {
            Role.Multimedia,
            Role.Console,
            Role.Communications
        };

        private readonly MMDeviceEnumerator _deviceEnumerator;

        public CaptureFormatNegotiator(MMDeviceEnumerator deviceEnumerator)
        {
            _deviceEnumerator = deviceEnumerator;
        }

        /// <summary>
        /// Returns the first default render endpoint available across the fallback
        /// role order. Throws only if no default render endpoint exists at all,
        /// which means there is no audio output device on the system.
        /// </summary>
        public MMDevice ResolveDefaultRenderEndpoint()
        {
            Exception? lastException = null;

            foreach (var role in RoleFallbackOrder)
            {
                try
                {
                    return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            throw new InvalidOperationException(
                "No default audio render endpoint is available for any device role.", lastException);
        }
    }
}
