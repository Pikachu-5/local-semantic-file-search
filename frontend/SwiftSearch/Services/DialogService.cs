using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace SwiftSearch.Services
{
    public static class DialogService
    {
        public static async Task<string?> PickFolderAsync()
        {
            try
            {
                var folderPicker = new FolderPicker();
                folderPicker.FileTypeFilter.Add("*");

                // Initialize picker with current Window handle (HWND) to prevent COMExceptions in WinUI 3
                IntPtr hwnd = App.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }
                else
                {
                    Debug.WriteLine("[DialogService] Warning: App.MainWindowHandle is zero. InitializeWithWindow bypassed.");
                }

                var folder = await folderPicker.PickSingleFolderAsync();
                return folder?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DialogService] PickFolderAsync failed: {ex.Message}");
                return null;
            }
        }
    }
}
