using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace RiseClient
{
    public class DownloadQueueItem
    {
        public UpdateFileInfo FileInfo { get; set; }
        public Action<float> OnProgress { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; }
    }

    public class DownloadQueue : MonoBehaviour
    {
        private static DownloadQueue _instance;
        public static DownloadQueue Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("DownloadQueue");
                    _instance = go.AddComponent<DownloadQueue>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private Queue<DownloadQueueItem> downloadQueue = new Queue<DownloadQueueItem>();
        private bool isProcessing = false;
        private DownloadQueueItem currentItem;

        public void EnqueueDownload(UpdateFileInfo fileInfo, Action<float> onProgress)
        {
            var item = new DownloadQueueItem
            {
                FileInfo = fileInfo,
                OnProgress = onProgress,
                CompletionSource = new TaskCompletionSource<bool>()
            };

            downloadQueue.Enqueue(item);
            ProcessQueueAsync();
        }

        public Task<bool> EnqueueDownloadWithTask(UpdateFileInfo fileInfo, Action<float> onProgress)
        {
            var item = new DownloadQueueItem
            {
                FileInfo = fileInfo,
                OnProgress = onProgress,
                CompletionSource = new TaskCompletionSource<bool>()
            };

            downloadQueue.Enqueue(item);
            ProcessQueueAsync();
            return item.CompletionSource.Task;
        }

        private async void ProcessQueueAsync()
        {
            if (isProcessing) return;

            isProcessing = true;
            while (downloadQueue.Count > 0)
            {
                currentItem = downloadQueue.Dequeue();
                Debug.Log($"开始下载: {currentItem.FileInfo.LocalPath}");

                try
                {
                    bool success = await DownloadManager.Instance.DownloadFileAsync(
                        currentItem.FileInfo.RemoteUrl,
                        currentItem.FileInfo.GetFullLocalPath(),
                        currentItem.FileInfo.FileSize,
                        currentItem.FileInfo.MD5,
                        currentItem.OnProgress
                    );

                    currentItem.FileInfo.IsDownloaded = success;
                    currentItem.CompletionSource.SetResult(success);

                    if (!success)
                    {
                        Debug.LogError($"下载失败: {currentItem.FileInfo.LocalPath}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"下载出错: {currentItem.FileInfo.LocalPath}, {ex.Message}");
                    currentItem.CompletionSource.SetResult(false);
                    break;
                }
            }

            isProcessing = false;
            currentItem = null;
        }

        public void ClearQueue()
        {
            foreach (var item in downloadQueue)
            {
                item.CompletionSource.TrySetCanceled();
            }
            downloadQueue.Clear();
        }

        public int GetQueueCount()
        {
            return downloadQueue.Count;
        }

        public DownloadQueueItem GetCurrentDownload()
        {
            return currentItem;
        }
    }
}
