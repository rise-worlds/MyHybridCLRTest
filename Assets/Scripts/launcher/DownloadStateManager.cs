using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

namespace RiseClient
{
    [Serializable]
    public class DownloadState
    {
        public string TargetVersion { get; set; }
        public List<string> CompletedFiles { get; set; } = new List<string>();
        public Dictionary<string, long> PartialDownloads { get; set; } = new Dictionary<string, long>();
    }

    public class DownloadStateManager
    {
        private static DownloadStateManager _instance;
        public static DownloadStateManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new DownloadStateManager();
                }
                return _instance;
            }
        }

        private string StateFilePath => Path.Combine(Application.persistentDataPath, "download_state.json");
        private DownloadState _currentState;

        public DownloadState CurrentState
        {
            get
            {
                if (_currentState == null)
                {
                    LoadState();
                }
                return _currentState;
            }
        }

        private DownloadStateManager()
        {
            LoadState();
        }

        private void LoadState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    string json = File.ReadAllText(StateFilePath);
                    _currentState = JsonUtility.FromJson<DownloadState>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"加载下载状态失败: {ex.Message}");
            }

            _currentState ??= new DownloadState();
        }

        public void SaveState()
        {
            try
            {
                string json = JsonUtility.ToJson(_currentState, true);
                File.WriteAllText(StateFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"保存下载状态失败: {ex.Message}");
            }
        }

        public void InitializeForVersion(string targetVersion)
        {
            if (_currentState.TargetVersion != targetVersion)
            {
                _currentState.TargetVersion = targetVersion;
                _currentState.CompletedFiles.Clear();
                _currentState.PartialDownloads.Clear();
                SaveState();
            }
        }

        public bool IsFileDownloaded(string filePath)
        {
            return _currentState.CompletedFiles.Contains(filePath);
        }

        public void MarkFileAsDownloaded(string filePath)
        {
            if (!_currentState.CompletedFiles.Contains(filePath))
            {
                _currentState.CompletedFiles.Add(filePath);
                _currentState.PartialDownloads.Remove(filePath);
                SaveState();
            }
        }

        public void UpdatePartialDownload(string filePath, long downloadedBytes)
        {
            _currentState.PartialDownloads[filePath] = downloadedBytes;
            SaveState();
        }

        public long GetPartialDownloadSize(string filePath)
        {
            return _currentState.PartialDownloads.TryGetValue(filePath, out long size) ? size : 0;
        }

        public void ClearState()
        {
            _currentState = new DownloadState();
            if (File.Exists(StateFilePath))
            {
                File.Delete(StateFilePath);
            }
        }

        public List<UpdateFileInfo> FilterPendingDownloads(List<UpdateFileInfo> allFiles)
        {
            return allFiles.Where(file => !IsFileDownloaded(file.LocalPath)).ToList();
        }
    }
}
