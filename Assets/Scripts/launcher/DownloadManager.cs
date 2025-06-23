using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RiseClient
{
    public class DownloadSegment
    {
        public long StartPosition { get; set; }
        public long EndPosition { get; set; }
        public long CurrentPosition { get; set; }
        public string TempFile { get; set; }
        public bool IsCompleted => CurrentPosition >= EndPosition;
    }
    public class DownloadSegmentInfo
    {
        public long StartPosition { get; set; }
        public long EndPosition { get; set; }
        public bool IsCompleted { get; set; }
        public long DownloadedSize { get; set; }
    }

    public class DownloadTask
    {
        public string Url { get; set; }
        public string SavePath { get; set; }
        public string MD5 { get; set; }
        public long FileSize { get; set; }
        public long DownloadedSize { get; set; }
        public float Progress => FileSize > 0 ? (float)DownloadedSize / FileSize : 0;
        public Action<float> OnProgressChanged;
        public bool IsPaused { get; set; }
        public bool IsCompleted { get; set; }
        public List<DownloadSegment> Segments { get; set; }
        public string TempFolder => Path.Combine(Path.GetDirectoryName(SavePath), ".temp");
        public string SegmentInfoPath => Path.Combine(TempFolder, $"{Path.GetFileName(SavePath)}.json");

        public void SaveSegmentsInfo()
        {
            var segmentInfos = Segments.Select(s => new DownloadSegmentInfo
            {
                StartPosition = s.StartPosition,
                EndPosition = s.EndPosition,
                IsCompleted = s.IsCompleted,
                DownloadedSize = s.CurrentPosition - s.StartPosition
            }).ToList();

            string json = JsonUtility.ToJson(new { segments = segmentInfos });
            File.WriteAllText(SegmentInfoPath, json);
        }

        public List<DownloadSegmentInfo> LoadSegmentsInfo()
        {
            if (!File.Exists(SegmentInfoPath)) return null;
            
            try
            {
                string json = File.ReadAllText(SegmentInfoPath);
                var wrapper = JsonUtility.FromJson<SegmentInfoWrapper>(json);
                return wrapper.segments;
            }
            catch
            {
                return null;
            }
        }
    }

    [Serializable]
    public class SegmentInfoWrapper
    {
        public List<DownloadSegmentInfo> segments;
    }

    public class DownloadManager : MonoBehaviour
    {
        private static DownloadManager _instance;
        public static DownloadManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("DownloadManager");
                    _instance = go.AddComponent<DownloadManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private const int MAX_CONCURRENT_DOWNLOADS = 4;
        private const int SEGMENT_SIZE = 100 * 1024 * 1024; // 100MB per segment
        private Dictionary<string, DownloadTask> _tasks = new Dictionary<string, DownloadTask>();
        private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);

        private const int MAX_RETRY_COUNT = 3;

        public async Task<bool> DownloadFileAsync(string url, string savePath, long fileSize, string expectedMD5 = null, Action<float> progressCallback = null)
        {
            if (_tasks.ContainsKey(url))
            {
                Debug.LogWarning($"Download task for {url} already exists");
                return false;
            }

            var task = new DownloadTask
            {
                Url = url,
                SavePath = savePath,
                FileSize = fileSize,
                MD5 = expectedMD5,
                OnProgressChanged = progressCallback,
                Segments = new List<DownloadSegment>()
            };

            _tasks[url] = task;

            try
            {
                // 创建临时文件夹
                Directory.CreateDirectory(task.TempFolder);

                // 检查是否支持断点续传
                bool supportsResume = await CheckResumeSupport(url);
                if (supportsResume)
                {
                    // 创建下载段
                    CreateDownloadSegments(task);

                    // 开始所有段的下载
                    var downloadTasks = task.Segments.Select(segment => DownloadSegmentAsync(task, segment)).ToList();
                    await Task.WhenAll(downloadTasks);
                }
                else
                {
                    // 不支持断点续传，使用单线程下载
                    var segment = new DownloadSegment
                    {
                        StartPosition = 0,
                        EndPosition = task.FileSize - 1,
                        TempFile = Path.Combine(task.TempFolder, "full")
                    };
                    task.Segments.Add(segment);
                    await DownloadSegmentAsync(task, segment);
                }

                if (task.IsCompleted)
                {
                    // 合并所有段
                    await MergeSegmentsAsync(task);

                    // 验证MD5
                    if (!string.IsNullOrEmpty(expectedMD5))
                    {
                        string actualMD5 = CalculateMD5(task.SavePath);
                        if (actualMD5 != expectedMD5)
                        {
                            Debug.LogError($"MD5 verification failed for {url}. Expected: {expectedMD5}, Got: {actualMD5}");
                            // 删除无效文件
                            if (File.Exists(task.SavePath))
                            {
                                File.Delete(task.SavePath);
                            }
                            // 清理临时文件
                            if (Directory.Exists(task.TempFolder))
                            {
                                Directory.Delete(task.TempFolder, true);
                            }
                            return false;
                        }
                    }

                    // 清理临时文件
                    Directory.Delete(task.TempFolder, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Download failed: {ex.Message}");
            }

            return false;
        }

        private void CreateDownloadSegments(DownloadTask task)
        {
            // 尝试加载已有的分段信息
            var savedSegments = task.LoadSegmentsInfo();
            if (savedSegments != null && ValidateSegments(task, savedSegments))
            {
                // 恢复已有的分段
                RestoreSegments(task, savedSegments);
                return;
            }

            // 创建新的分段
            long segmentCount = (task.FileSize + SEGMENT_SIZE - 1) / SEGMENT_SIZE;
            task.Segments = new List<DownloadSegment>();

            for (int i = 0; i < segmentCount; i++)
            {
                var filename = Path.GetFileName(task.SavePath);
                var segment = new DownloadSegment
                {
                    StartPosition = i * SEGMENT_SIZE,
                    EndPosition = Math.Min((i + 1) * SEGMENT_SIZE - 1, task.FileSize - 1),
                    TempFile = Path.Combine(task.TempFolder, $"{filename}_{i}"),
                    CurrentPosition = i * SEGMENT_SIZE
                };
                task.Segments.Add(segment);
            }

            // 保存分段信息
            task.SaveSegmentsInfo();
        }

        private bool ValidateSegments(DownloadTask task, List<DownloadSegmentInfo> savedSegments)
        {
            if (savedSegments == null || savedSegments.Count == 0) return false;

            // 验证文件大小
            long totalSize = savedSegments.Sum(s => s.EndPosition - s.StartPosition + 1);
            if (totalSize != task.FileSize) return false;

            // 验证每个分段文件是否存在且大小正确
            foreach (var segmentInfo in savedSegments)
            {
                var filename = Path.GetFileName(task.SavePath);
                string segmentPath = Path.Combine(task.TempFolder, 
                    $"{filename}_{segmentInfo.StartPosition / SEGMENT_SIZE}");

                if (!File.Exists(segmentPath)) return false;

                // 验证已下载部分的大小
                var fileInfo = new FileInfo(segmentPath);
                if (fileInfo.Length != segmentInfo.DownloadedSize) return false;
            }

            return true;
        }

        private void RestoreSegments(DownloadTask task, List<DownloadSegmentInfo> savedSegments)
        {
            task.Segments = new List<DownloadSegment>();
            task.DownloadedSize = 0;

            foreach (var segmentInfo in savedSegments)
            {
                var filename = Path.GetFileName(task.SavePath);
                var segment = new DownloadSegment
                {
                    StartPosition = segmentInfo.StartPosition,
                    EndPosition = segmentInfo.EndPosition,
                    TempFile = Path.Combine(task.TempFolder, 
                        $"{filename}_{segmentInfo.StartPosition / SEGMENT_SIZE}"),
                };

                if (segmentInfo.IsCompleted)
                {
                    segment.CurrentPosition = segmentInfo.EndPosition + 1;
                    task.DownloadedSize += segmentInfo.EndPosition - segmentInfo.StartPosition + 1;
                }
                else
                {
                    // 如果段未完成，获取已下载的大小
                    if (File.Exists(segment.TempFile))
                    {
                        var fileInfo = new FileInfo(segment.TempFile);
                        segment.CurrentPosition = segmentInfo.StartPosition + fileInfo.Length;
                        task.DownloadedSize += fileInfo.Length;
                    }
                    else
                    {
                        segment.CurrentPosition = segmentInfo.StartPosition;
                    }
                }

                task.Segments.Add(segment);
            }
        }

        private async Task<bool> CheckResumeSupport(string url)
        {
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("Range", "bytes=0-0");
            using HttpResponseMessage response = await client.GetAsync(url);
            return response.Headers.AcceptRanges.Contains("bytes");
        }

        private async Task DownloadSegmentAsync(DownloadTask task, DownloadSegment segment)
        {
            await _downloadSemaphore.WaitAsync();

            try
            {
                while (!segment.IsCompleted && !task.IsPaused)
                {
                    using (var request = new HttpClient())
                    {
                        request.DefaultRequestHeaders.Add("Range", $"bytes={segment.CurrentPosition}-{segment.EndPosition}");
                        using var response = await request.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead);
                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.LogError($"Segment download failed: {response.ReasonPhrase}");
                            continue;
                        }
                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(segment.TempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                        await stream.CopyToAsync(fileStream);

                        segment.CurrentPosition = segment.EndPosition;
                    }

                    // 更新总进度
                    task.DownloadedSize = task.Segments.Sum(s => s.CurrentPosition - s.StartPosition);
                    task.OnProgressChanged?.Invoke(task.Progress);

                    // 保存分段信息
                    task.SaveSegmentsInfo();
                }

                task.IsCompleted = task.Segments.All(s => s.IsCompleted);
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        private async Task MergeSegmentsAsync(DownloadTask task)
        {
            string tempMergedFile = task.SavePath + ".tmp";

            try
            {
                using (var outputStream = new FileStream(tempMergedFile, FileMode.Create))
                {
                    foreach (var segment in task.Segments.OrderBy(s => s.StartPosition))
                    {
                        using (var inputStream = new FileStream(segment.TempFile, FileMode.Open))
                        {
                            await inputStream.CopyToAsync(outputStream);
                        }
                    }
                }

                // 如果目标文件已存在，先删除
                if (File.Exists(task.SavePath))
                {
                    File.Delete(task.SavePath);
                }

                // 将临时合并文件移动到目标位置
                File.Move(tempMergedFile, task.SavePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to merge segments: {ex.Message}");
                if (File.Exists(tempMergedFile))
                {
                    File.Delete(tempMergedFile);
                }
                throw;
            }
        }

        private string CalculateMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public void PauseDownload(string url)
        {
            if (_tasks.TryGetValue(url, out var task))
            {
                task.IsPaused = true;
            }
        }

        public void ResumeDownload(string url)
        {
            if (_tasks.TryGetValue(url, out var task))
            {
                task.IsPaused = false;
                _ = DownloadFileAsync(url, task.SavePath, task.FileSize, task.MD5, task.OnProgressChanged);
            }
        }

        public void CancelDownload(string url)
        {
            if (_tasks.TryGetValue(url, out var task))
            {
                task.IsPaused = true;
                _tasks.Remove(url);
                if (Directory.Exists(task.TempFolder))
                {
                    Directory.Delete(task.TempFolder, true);
                }
            }
        }
    }
}
