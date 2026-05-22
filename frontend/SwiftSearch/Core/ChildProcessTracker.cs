using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwiftSearch.Core
{
    public static class ChildProcessTracker
    {
        private static readonly IntPtr _jobHandle = IntPtr.Zero;

        static ChildProcessTracker()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                    if (_jobHandle == IntPtr.Zero)
                    {
                        Debug.WriteLine($"[JobObject] Failed to create job object: {Marshal.GetLastWin32Error()}");
                        return;
                    }

                    var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                    int size = Marshal.SizeOf(typeof(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(info, ptr, false);
                        if (!NativeMethods.SetInformationJobObject(_jobHandle, 9, ptr, (uint)size)) // 9 = JobObjectExtendedLimitInformation
                        {
                            Debug.WriteLine($"[JobObject] Failed to set limit options: {Marshal.GetLastWin32Error()}");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JobObject] Exception initializing tracker: {ex.Message}");
                }
            }
        }

        public static void AddProcess(Process process)
        {
            if (_jobHandle != IntPtr.Zero && process != null)
            {
                try
                {
                    NativeMethods.AssignProcessToJobObject(_jobHandle, process.Handle);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[JobObject] Failed to associate process {process.Id} to job: {ex.Message}");
                }
            }
        }
    }
}
