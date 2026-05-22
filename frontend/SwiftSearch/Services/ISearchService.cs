using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using SwiftSearch.Models;

namespace SwiftSearch.Services
{
    public interface ISearchService : INotifyPropertyChanged
    {
        bool IsDaemonOnline { get; }
        bool IsDownloadingModel { get; }
        int TotalFiles { get; }
        int TotalVectors { get; }
        string ActiveModel { get; }
        List<string> WatchedFolders { get; }
        List<string> ExcludedDirs { get; }
        List<string> IncludedExtensions { get; }
        List<string> DownloadedModels { get; }
        string DbDir { get; }
        
        // Diagnostic properties
        int DaemonPort { get; }
        int DaemonPid { get; }
        long DaemonMemoryBytes { get; }
        List<string> LogHistory { get; }

        // Diagnostic events and methods
        event Action<string>? LogReceived;
        event Action<string, float, string>? ModelDownloadProgressChanged;
        Task DownloadModelAsync(string modelName);
        void StartDaemon();
        void StopDaemon();
        Task<List<SearchItem>> SearchAsync(string query, int topK);
        Task<List<SearchItem>> SearchEverythingAsync(string query, int topK);
        Task<bool> IndexFolderAsync(string folderPath);
        Task<bool> RemoveFolderAsync(string folderPath);
        Task<bool> UpdateSettingsAsync(string activeModel, List<string> excludedDirs, List<string> includedExtensions);
        Task RefreshStatusAsync();
        Task<long> PingDaemonAsync();
        void ClearLogs();
    }
}
