using NAudio.CoreAudioApi;
using System;
using System.Runtime.InteropServices;

namespace AudioDual.Core
{
    public class VirtualAudioDevice : IDisposable
    {
        // Windows Core Audio API imports for creating virtual audio endpoints
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid iid, out IntPtr ppv);

        // COM interface IDs for audio policy manager and routing
        private static readonly Guid CLSID_PolicyConfigClient = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
        private static readonly Guid IID_IPolicyConfigClient = new Guid("f8679f50-850a-41cf-9c72-430f290290c8");

        // Make the enum internal to match the interface visibility
        internal enum PolicyConfigRole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        // Native interfaces for audio endpoint management
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPolicyConfigClient
        {
            // Set the default endpoint for a specific role
            [PreserveSig]
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PolicyConfigRole role);
            
            // Other methods omitted for brevity
        }

        // Keep track of original default device
        private string? _originalDefaultDeviceId;
        private IPolicyConfigClient? _policyClient;

        public VirtualAudioDevice()
        {
            try
            {
                // Initialize the policy config client for endpoint management
                var clsid = CLSID_PolicyConfigClient;
                var iid = IID_IPolicyConfigClient;
                int result = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out IntPtr ptr);
                if (result >= 0)
                {
                    _policyClient = (IPolicyConfigClient)Marshal.GetObjectForIUnknown(ptr);
                    Marshal.Release(ptr);
                }
                
                // Store the current default device
                var enumerator = new MMDeviceEnumerator();
                var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _originalDefaultDeviceId = defaultDevice.ID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize virtual audio device: {ex.Message}");
            }
        }

        public bool SetDefaultDevice(string deviceId)
        {
            try
            {
                if (_policyClient == null) return false;
                
                // Set the default device for each role
                _policyClient.SetDefaultEndpoint(deviceId, PolicyConfigRole.eConsole);
                _policyClient.SetDefaultEndpoint(deviceId, PolicyConfigRole.eMultimedia);
                _policyClient.SetDefaultEndpoint(deviceId, PolicyConfigRole.eCommunications);
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting default device: {ex.Message}");
                return false;
            }
        }

        public void RestoreOriginalDefaultDevice()
        {
            if (_originalDefaultDeviceId != null)
            {
                SetDefaultDevice(_originalDefaultDeviceId);
            }
        }

        public void Dispose()
        {
            RestoreOriginalDefaultDevice();
            _policyClient = null;
        }
    }
}
