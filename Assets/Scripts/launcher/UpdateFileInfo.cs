using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RiseClient
{
    [Serializable]
    public class UpdateFileInfo
    {
        public string LocalPath { get; set; }
        public string RemoteUrl { get; set; }
        public ulong FileSize { get; set; }
        public string MD5 { get; set; }
        public bool IsDownloaded { get; set; }

        public string GetFullLocalPath()
        {
            return Path.Combine(Application.persistentDataPath, LocalPath);
        }

        public static List<UpdateFileInfo> ParseVersionFile(string content)
        {
            var result = new List<UpdateFileInfo>();
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (lines.Length <= 1)
            {
                return result;
            }

            // 跳过第一行（版本号）
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length == 4)
                {
                    result.Add(new UpdateFileInfo
                    {
                        LocalPath = parts[0].Trim(),
                        RemoteUrl = parts[1].Trim(),
                        FileSize = ulong.Parse(parts[2].Trim()),
                        MD5 = parts[3].Trim(),
                        IsDownloaded = false
                    });
                }
            }

            return result;
        }

        public static string GetVersionNumber(string content)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : "1.0";
        }
    }

    public class DownloadProgress
    {
        public int TotalFiles { get; set; }
        public int CompletedFiles { get; set; }
        public float CurrentFileProgress { get; set; }
        public string CurrentFileName { get; set; }

        public float TotalProgress
        {
            get
            {
                if (TotalFiles == 0) return 0;
                return ((float)CompletedFiles + CurrentFileProgress) / TotalFiles;
            }
        }
    }
}
