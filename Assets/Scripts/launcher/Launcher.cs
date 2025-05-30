using HybridCLR;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace RiseClient
{
    public class Launcher : MonoBehaviour
    {
#if UNITY_STANDALONE_WIN
        private string abUrl => "https://tools.qipai360.cn/unity3d/ab/win32/main.dll.bytes.ab";
#elif UNITY_ANDROID
        private string abUrl => "https://tools.qipai360.cn/unity3d/ab/android/main.dll.bytes.ab";
#elif UNITY_IOS
        private string abUrl => "https://tools.qipai360.cn/unity3d/ab/ios/main.dll.bytes.ab";
#endif
        private string versionUrl => "https://tools.qipai360.cn/unity3d/ab/version.txt";
        private static string localVersionPath => $"{Application.persistentDataPath}/version.txt";
        private static string localVersion = "1.0";

        public Text statusText;
        public Text downloadText;
        public Slider totalProgressBar;
        public Slider downloadProgressBar;

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
            f (!File.Exists(assetPath))
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
#if !UNITY_EDITOR
            downloadText.gameObject.SetActive(false);
            downloadProgressBar.gameObject.SetActive(false);
            StartCoroutine(CheckAndUpdate());
#else
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
#endif
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
#if UNITY_STANDALONE_WIN
                    string sourcePath = $"{Application.streamingAssetsPath}/{aotDllName}.bytes";
#elif UNITY_ANDROID
                    string sourcePath = $"jar:file://{Application.dataPath}!/assets/{aotDllName}.bytes";
#elif UNITY_IOS
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

        IEnumerator CheckAndUpdate()
        {
            yield return StartCoroutine(LoadMetadataForAOTAssemblies());

            float totalSteps = 3f; // 1.获取版本 2.下载AB 3.加载AB
            float currentStep = 0f;

            // 1. 读取本地版本号
            statusText.text = "正在检查本地版本...";
            Debug.Log("获取本地版本号路径: " + localVersionPath);
            if (!File.Exists(localVersionPath))
            {
                statusText.text = "本地版本文件不存在，使用默认版本1.0";
                Debug.LogWarning("本地版本文件不存在，使用默认版本1.0");
            }
            else
            {
                localVersion = File.ReadAllText(localVersionPath).Trim();
            }

            // 2. 获取服务器版本号
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
            string remoteVersion = versionReq.downloadHandler.text.Trim();
            string abLocalPath = GetAssetPath("main.dll.bytes.ab");

            // 3. 比较版本号
            Debug.Log($"本地版本: {localVersion}, 远程版本: {remoteVersion}");
            if (localVersion != remoteVersion)
            {
                // 4. 下载AB包
                statusText.text = "正在下载资源...";
                UnityWebRequest abReq = UnityWebRequest.Get($"{abUrl}?t={DateTime.Now.Ticks}");
                abReq.SendWebRequest();
                downloadText.gameObject.SetActive(true);
                downloadProgressBar.gameObject.SetActive(true);
                while (!abReq.isDone)
                {
                    downloadProgressBar.value = abReq.downloadProgress;
                    ulong downloaded = abReq.downloadedBytes;
                    ulong total = 0;
                    if (abReq.GetResponseHeaders() != null && abReq.GetResponseHeaders().ContainsKey("Content-Length"))
                    {
                        ulong.TryParse(abReq.GetResponseHeaders()["Content-Length"], out total);
                    }
                    downloadText.text = $"已下载: {FormatBytes(downloaded)} / {FormatBytes(total)}";
                    yield return null;
                }
                downloadProgressBar.value = 1f;
                currentStep++;
                totalProgressBar.value = currentStep / totalSteps;

                if (abReq.result != UnityWebRequest.Result.Success)
                {
                    statusText.text = "下载AB包失败";
                    Debug.LogError("下载AB包失败");
                    yield break;
                }

#if UNITY_STANDALONE_WIN
                abLocalPath = $"{Application.streamingAssetsPath}/main.dll.bytes.ab";
#elif UNITY_ANDROID
                abLocalPath = $"{Application.persistentDataPath}/main.dll.bytes.ab";
#elif UNITY_IOS
                abLocalPath = $"{Application.streamingAssetsPath}/main.dll.bytes.ab";
#endif
                File.WriteAllBytes(abLocalPath, abReq.downloadHandler.data);
                File.WriteAllText(localVersionPath, remoteVersion);
                Debug.Log($"AB包下载并保存成功到: {abLocalPath}");
                Debug.Log($"版本更新成功，已保存新版本号: {remoteVersion}, {localVersionPath}");
            }
            else
            {
                // 如果不需要下载，直接跳过下载进度
                downloadProgressBar.value = 1f;
                currentStep++;
                totalProgressBar.value = currentStep / totalSteps;
            }

            // 5. 加载AB包并切换到main场景
            statusText.text = "正在加载资源...";
            AssetBundle ab = AssetBundle.LoadFromFile(abLocalPath);
            //var abLoad = AssetBundle.LoadFromFileAsync(abLocalPath);
            //while (!abLoad.isDone)
            //{
            //    totalProgressBar.value = (currentStep + abLoad.progress) / totalSteps;
            //}
            //AssetBundle ab = abLoad.assetBundle;
            if (ab == null)
            {
                statusText.text = "加载AB包失败";
                Debug.LogError($"从 {abLocalPath} 加载AB包失败");
                yield break;
            }
            totalProgressBar.value = 1f;

            TextAsset _hotUpdateAssData = ab.LoadAsset<TextAsset>("main.dll.bytes");
            Assembly _hotUpdateAss = Assembly.Load(_hotUpdateAssData.bytes);

            // Type _testHotUpdate = _hotUpdateAss.GetType("RiseClient.TestHotUpdate");
            // MethodInfo _runMethod = _testHotUpdate.GetMethod("Run");
            // _runMethod.Invoke(null, null);

            string sceneabLocalPath = GetAssetPath("main.ab");
            AssetBundle sceneab = AssetBundle.LoadFromFile(sceneabLocalPath);
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
                Debug.LogError($"未从 {sceneabLocalPath} 找到main场景");
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
