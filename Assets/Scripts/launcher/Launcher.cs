using HybridCLR;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RiseClient
{
    public class Launcher : MonoBehaviour
    {
#if UNITY_ANDROID
        private string remoteBaseUrl => "https://tools.qipai360.cn/unity3d/ab/android/";
#elif UNITY_IOS
        private string remoteBaseUrl => "https://tools.qipai360.cn/unity3d/ab/ios/";
#else
        private string remoteBaseUrl => "https://tools.qipai360.cn/unity3d/ab/win32/";
#endif
        private string abUrl => $"{remoteBaseUrl}main.dll.bytes.ab";
        private string versionUrl => $"{remoteBaseUrl}version.txt";
        private static string localVersionPath => Path.Combine(Application.persistentDataPath, "version.txt");
        private static string localVersion = "1.0";

        public Text statusText;
        public Text downloadText;
        public Slider totalProgressBar;
        public Slider downloadProgressBar;

        private List<UpdateFileInfo> updateFiles;
        private DownloadProgress downloadProgress = new DownloadProgress();

        private static string GetAssetPath(string assetName)
        {
#if UNITY_STANDALONE_WIN
            string assetPath = Path.Combine(Application.persistentDataPath, assetName);
            if (!File.Exists(assetPath))
            {
                assetPath = Path.Combine(Application.streamingAssetsPath, assetName);
            }
#elif UNITY_ANDROID
            string assetPath = Path.Combine(Application.persistentDataPath, assetName);
            if (!File.Exists(assetPath))
            {
                // Android平台需要使用WWW或UnityWebRequest加载StreamingAssets
                assetPath = "jar:file://" + Application.dataPath + "!/assets/" + assetName;
            }
#elif UNITY_IOS
            string assetPath = Path.Combine(Application.persistentDataPath, assetName);
            if (!File.Exists(assetPath))
            {
                assetPath = Path.Combine(Application.streamingAssetsPath, assetName);
            }
#endif
            return assetPath;
        }

        void Start()
        {
            Debug.Log($"StreamingAssets: {Application.streamingAssetsPath}");
            Debug.Log($"PersistentData:  {Application.persistentDataPath}");

            // 读取本地版本号
            statusText.text = "正在检查本地版本...";
            Debug.Log("获取本地版本号路径: " + localVersionPath);
            if (!File.Exists(localVersionPath))
            {
                statusText.text = "本地版本文件不存在，使用默认版本1.0";
                Debug.LogWarning("本地版本文件不存在，使用默认版本1.0");
            }
            else
            {
                localVersion = File.ReadAllText(localVersionPath).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            }

            downloadText.gameObject.SetActive(false);
            downloadProgressBar.gameObject.SetActive(false);
            StartCoroutine(CheckAndUpdate());
#if !UNITY_EDITOR
            // 在编辑器中直接加载main场景
            string mainScene = "Assets/Scenes/main.unity"; // 替换为你的main场景路径
            if (File.Exists(mainScene))
            {
                SceneManager.LoadScene(Path.GetFileNameWithoutExtension(mainScene));
            }
            else
            {
                Debug.LogError("未找到main场景");
            }
#else
#endif
        }

        private IEnumerator CheckAndUpdate()
        {
            yield return StartCoroutine(LoadMetadataForAOTAssemblies());

            float totalSteps = 3f;
            float currentStep = 0f;

            // 2. 获取服务器版本信息
            statusText.text = "正在获取服务器版本...";
            UnityWebRequest versionReq = UnityWebRequest.Get($"{versionUrl}?t={DateTime.Now.Ticks}");
            yield return versionReq.SendWebRequest();
            currentStep++;
            totalProgressBar.value = currentStep / totalSteps;
            
            if (versionReq.result != UnityWebRequest.Result.Success)
            {
                statusText.text = "获取服务器版本失败";
                Debug.LogError("获取服务器版本失败");
                yield break;
            }
            
            string versionContent = versionReq.downloadHandler.text;

            var result = new List<UpdateFileInfo>();
            var lines = versionContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string remoteVersion = lines.Length > 0 ? lines[0].Trim() : "1.0";
            string remoteBaseUrl = lines.Length > 1 ? lines[1].Trim() : this.remoteBaseUrl; // 默认远程地址
            updateFiles = UpdateFileInfo.ParseVersionFile(remoteBaseUrl, lines);

            // 3. 比较版本号
            Debug.Log($"本地版本: {localVersion}, 远程版本: {remoteVersion}");
            if (localVersion != remoteVersion)
            {
                // 初始化下载状态管理器
                DownloadStateManager.Instance.InitializeForVersion(remoteVersion);

                // 获取待下载的文件列表
                var pendingFiles = DownloadStateManager.Instance.FilterPendingDownloads(updateFiles);
                
                // 4. 下载所有更新文件
                statusText.text = "正在下载资源...";
                downloadText.gameObject.SetActive(true);
                downloadProgressBar.gameObject.SetActive(true);

                downloadProgress.TotalFiles = pendingFiles.Count;
                downloadProgress.CompletedFiles = updateFiles.Count - pendingFiles.Count;
                downloadProgress.CurrentFileProgress = 0;

                // 批量添加到下载队列并等待全部完成
                var downloadTasks = new List<Task<bool>>();
                foreach (var fileInfo in pendingFiles)
                {
                    // 准备所有文件的下载目录
                    string directory = Path.GetDirectoryName(fileInfo.GetFullLocalPath());
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    var task = DownloadQueue.Instance.EnqueueDownloadWithTask(
                        fileInfo,
                        (progress) =>
                        {
                            downloadProgress.CurrentFileName = fileInfo.LocalPath;
                            downloadProgress.CurrentFileProgress = progress;
                            UpdateDownloadUI();
                        });
                    downloadTasks.Add(task);
                }

                // 用TaskCompletionSource桥接等待所有下载完成
                bool[] results = null;
                bool allSuccess = true;
                var tcs = new TaskCompletionSource<bool[]>();
                Task.WhenAll(downloadTasks).ContinueWith(t => {
                    if (t.Exception != null)
                    {
                        Debug.LogError($"下载队列执行出错: {t.Exception.Message}");
                        tcs.SetResult(null);
                    }
                    else
                    {
                        tcs.SetResult(t.Result);
                    }
                });
                while (!tcs.Task.IsCompleted)
                {
                    yield return null;
                }
                results = tcs.Task.Result;

                // 检查每个文件的下载结果
                if (results != null)
                {
                    for (int i = 0; i < pendingFiles.Count; i++)
                    {
                        if (results[i])
                        {
                            pendingFiles[i].IsDownloaded = true;
                            downloadProgress.CompletedFiles++;
                            downloadProgress.CurrentFileProgress = 0;
                            // 标记文件下载完成
                            DownloadStateManager.Instance.MarkFileAsDownloaded(pendingFiles[i].LocalPath);
                        }
                        else
                        {
                            allSuccess = false;
                            Debug.LogError($"下载失败: {pendingFiles[i].LocalPath}");
                        }
                    }
                }

                if (!allSuccess)
                {
                    statusText.text = "下载更新文件失败";
                    Debug.LogError("下载更新文件失败");
                    DownloadQueue.Instance.ClearQueue(); // 清空剩余队列
                    yield break;
                }

                // 所有文件下载成功，更新本地版本文件，并清除下载状态
                File.WriteAllText(localVersionPath, versionContent);
                DownloadStateManager.Instance.ClearState();
                Debug.Log($"版本更新成功，已保存新版本号: {remoteVersion}");
            }
            else
            {
                // 如果版本相同，清除可能存在的未完成下载状态
                DownloadStateManager.Instance.ClearState();
                downloadProgressBar.value = 1f;
                currentStep++;
                totalProgressBar.value = currentStep / totalSteps;
            }

            // 5. 加载热更新DLL
            var mainDllInfo = updateFiles?.FirstOrDefault(f => f.LocalPath.EndsWith("main.dll.bytes.ab"));
            string mainDllPath = mainDllInfo?.GetFullLocalPath() ?? GetAssetPath("main.dll.bytes.ab");

            statusText.text = "正在加载资源...";
            AssetBundle ab = AssetBundle.LoadFromFile(mainDllPath);
            if (ab == null)
            {
                statusText.text = "加载AB包失败";
                Debug.LogError($"从 {mainDllPath} 加载AB包失败");
                yield break;
            }
            totalProgressBar.value = 1f;

            TextAsset _hotUpdateAssData = ab.LoadAsset<TextAsset>("main.dll.bytes");
            Assembly _hotUpdateAss = Assembly.Load(_hotUpdateAssData.bytes);

            // 6. 加载场景
            var sceneInfo = updateFiles?.FirstOrDefault(f => f.LocalPath.EndsWith("main.ab"));
            string scenePath = sceneInfo?.GetFullLocalPath() ?? GetAssetPath("main.ab");

            AssetBundle sceneab = AssetBundle.LoadFromFile(scenePath);
            string[] scenes = sceneab.GetAllScenePaths();
            string mainScene = Array.Find(scenes, s => s.Contains("main"));
            if (!string.IsNullOrEmpty(mainScene))
            {
                statusText.text = "加载场景中...";
                SceneManager.LoadScene(Path.GetFileNameWithoutExtension(mainScene));
            }
            else
            {
                statusText.text = "未找到main场景";
                Debug.LogError($"未从 {scenePath} 找到main场景");
            }
        }

        private static IEnumerator LoadMetadataForAOTAssemblies()
        {
            Debug.Log("Loading AOT assemblies...");

            List<string> aotDllList = new List<string>
            {
                "mscorlib.dll",
                "System.dll",
                "System.Core.dll",
            };
            bool copy = File.Exists(localVersionPath);
            Debug.Log($"AOT DLL copy mode: {(copy ? "Copy" : "Download")}");

            foreach (var aotDllName in aotDllList)
            {
                string path = Path.Combine(Application.persistentDataPath, $"{aotDllName}.bytes");
                if (!copy)
                {
                    // 如果版本文件不存在，直接从StreamingAssets复制AOT DLL
#if UNITY_ANDROID
                    string sourcePath = $"jar:file://{Application.dataPath}!/assets/{aotDllName}.bytes";
#elif UNITY_IOS
                    string sourcePath = $"{Application.streamingAssetsPath}/{aotDllName}.bytes";
#else
                    string sourcePath = $"{Application.streamingAssetsPath}/{aotDllName}.bytes";
#endif
                    Debug.Log($"Copy AOT assembly from {sourcePath} to {path}");
                    var req = UnityWebRequest.Get(sourcePath);
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"load AOT assembly error: {req.error}");
                        continue; // 如果下载失败，跳过这个AOT DLL
                    }
                    FileStream fsDes = File.Create(path);
                    fsDes.Write(req.downloadHandler.data, 0, req.downloadHandler.data.Length);
                    fsDes.Flush();
                    fsDes.Close();
                }
                Debug.Log($"Loading AOT assembly: {aotDllName} from {path}");
                Thread.Sleep(100); // 确保日志输出有时间被处理
                byte[] dllBytes = File.ReadAllBytes(path);
                if (dllBytes == null)
                {
                    Debug.LogError($"AOT assembly not found: {path}");
                    continue;
                }
                LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, HomologousImageMode.SuperSet);
                Debug.Log($"LoadMetadataForAOTAssembly:{aotDllName}. ret:{err}");
            }
            if (!copy)
            {
                Debug.Log("Creating local version file with default version 1.0");
                File.WriteAllText(localVersionPath, localVersion);
            }
        }

        private async Task<bool> DownloadFileWithRetry(UpdateFileInfo fileInfo, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    downloadProgress.CurrentFileName = fileInfo.LocalPath;
                    
                    var downloadTask = DownloadQueue.Instance.EnqueueDownloadWithTask(
                        fileInfo,
                        (progress) =>
                        {
                            downloadProgress.CurrentFileProgress = progress;
                            UpdateDownloadUI();
                        });

                    bool success = await downloadTask;

                    if (success)
                    {
                        fileInfo.IsDownloaded = true;
                        downloadProgress.CompletedFiles++;
                        downloadProgress.CurrentFileProgress = 0;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Download attempt {i + 1} failed for {fileInfo.LocalPath}: {ex.Message}");
                    await Task.Delay(1000 * (i + 1)); // 指数退避
                }
            }
            return false;
        }

        private void UpdateDownloadUI()
        {
            if (downloadProgress.TotalFiles > 0)
            {
                downloadProgressBar.value = downloadProgress.TotalProgress;
                string fileName = Path.GetFileName(downloadProgress.CurrentFileName);
                int remainingFiles = downloadProgress.TotalFiles - downloadProgress.CompletedFiles;
                downloadText.text = $"正在下载: {fileName} ({downloadProgress.CompletedFiles}/{downloadProgress.TotalFiles})\n队列中: {remainingFiles} 个文件";
            }
        }

        string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024 * 1024)
                return $"{(bytes / 1024f / 1024f):F2} MB";
            if (bytes >= 1024)
                return $"{(bytes / 1024f):F2} KB";
            return $"{bytes} B";
        }
    }
}
