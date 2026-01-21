using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

// Utility functions for filesystem operations
namespace Drives.Util
{
    // Verifies digital signatures on files
    public static class SigVerifier
    {
        // Windows trust verification API
        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, IntPtr pgActionID, IntPtr pWVTData);

        // Verification action GUID identifier
        private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

        // No signature found error code
        private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
        // Subject not trusted error code
        private const uint TRUST_E_SUBJECT_NOT_TRUSTED = 0x800B0004;
        // Unknown provider error code
        private const uint TRUST_E_PROVIDER_UNKNOWN = 0x800B0001;
        // Unknown action error code
        private const uint TRUST_E_ACTION_UNKNOWN = 0x800B0002;

        // File information structure for WinTrust
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_FILE_INFO
        {
            // Structure size in bytes
            public uint StructSize = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            // Pointer to file path string
            public IntPtr pszFilePath;
            // Handle to open file
            public IntPtr hFile = IntPtr.Zero;
            // Known subject GUID pointer
            public IntPtr pgKnownSubject = IntPtr.Zero;
        }

        // Trust data structure for WinTrust
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class WINTRUST_DATA
        {
            public uint StructSize = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            public IntPtr PolicyCallbackData = IntPtr.Zero;
            public IntPtr SIPClientData = IntPtr.Zero;
            public uint UIChoice = 2;
            public uint RevocationChecks = 0;
            public uint UnionChoice = 1;
            public IntPtr FileInfoPtr;
            public uint StateAction = 0;
            public IntPtr StateData = IntPtr.Zero;
            public string URLReference = null;
            public uint ProvFlags = 0x00000010;
            public uint UIContext = 0;
            public IntPtr pSignatureSettings = IntPtr.Zero;
        }

        // Checks file digital signature status
        public static string CheckSignature(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }

            try
            {
                var fileInfo = new WINTRUST_FILE_INFO();
                var data = new WINTRUST_DATA();
                
                IntPtr fileInfoPtr = IntPtr.Zero;
                IntPtr dataPtr = IntPtr.Zero;
                IntPtr guidPtr = IntPtr.Zero;
                IntPtr filePathPtr = IntPtr.Zero;

                try
                {
                    filePathPtr = Marshal.StringToCoTaskMemUni(filePath);
                    fileInfo.pszFilePath = filePathPtr;

                    fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
                    Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

                    data.FileInfoPtr = fileInfoPtr;

                    dataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                    Marshal.StructureToPtr(data, dataPtr, false);

                    guidPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(Guid)));
                    Marshal.StructureToPtr(WINTRUST_ACTION_GENERIC_VERIFY_V2, guidPtr, false);

                    uint result = WinVerifyTrust(IntPtr.Zero, guidPtr, dataPtr);

                    if (result == 0)
                    {
                        return "Signed";
                    }
                    else if (result == TRUST_E_NOSIGNATURE)
                    {
                        return "Unsigned";
                    }
                    else
                    {
                        return "Unsigned";
                    }
                }
                finally
                {
                    if (filePathPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(filePathPtr);
                    if (fileInfoPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPtr);
                    if (dataPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(dataPtr);
                    if (guidPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(guidPtr);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking signature for {filePath}: {ex.Message}");
                return "Error";
            }
        }
    }
}
