using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RiseClient
{
    public class Launcher : MonoBehaviour
    {
        private string localVersionPath => Path.Combine(Application.persistentDataPath, "version.txt");
        private string abUrl = "https://tools.qipai360.cn/unity3d/ab/main.dll.bytes.ab"; // 替换为你的AB包地址
        private string versionUrl = "https://tools.qipai360.cn/unity3d/ab/version.txt"; // 替换为你的版本号地址
        private string abLocalPath => Path.Combine(Application.persistentDataPath, "main.dll.bytes.ab");

        public Text statusText;
        public Text downloadText;
        public Slider totalProgressBar;
        public Slider downloadProgressBar;

        void Start()
        {
//#if !UNITY_EDITOR
        downloadText.gameObject.SetActive(false);
        downloadProgressBar.gameObject.SetActive(false);
        StartCoroutine(CheckAndUpdate());
//#endif
        //// 在编辑器中直接加载main场景
        //string mainScene = "Assets/Scenes/main.unity"; // 替换为你的main场景路径
        //if (File.Exists(mainScene))
        //{
        //    SceneManager.LoadScene(Path.GetFileNameWithoutExtension(mainScene));
        //}
        //else
        //{
        //    Debug.LogError("未找到main场景");
        //}
//#endif
        }

        IEnumerator CheckAndUpdate()
        {
            float totalSteps = 3f; // 1.获取版本 2.下载AB 3.加载AB
            float currentStep = 0f;

            // 1. 读取本地版本号
            statusText.text = "正在检查本地版本...";
            string localVersion = "1.0";
            if (File.Exists(localVersionPath))
                localVersion = File.ReadAllText(localVersionPath).Trim();

            // 2. 获取服务器版本号
            statusText.text = "正在获取服务器版本...";
            UnityWebRequest versionReq = UnityWebRequest.Get(versionUrl);
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

            // 3. 比较版本号
            if (localVersion != remoteVersion)
            {
                // 4. 下载AB包
                statusText.text = "正在下载资源...";
                UnityWebRequest abReq = UnityWebRequest.Get(abUrl);
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
                File.WriteAllBytes(abLocalPath, abReq.downloadHandler.data);
                File.WriteAllText(localVersionPath, remoteVersion);
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
                Debug.LogError("加载AB包失败");
                yield break;
            }
            totalProgressBar.value = 1f;

            TextAsset _hotUpdateAssData = ab.LoadAsset<TextAsset>("main.dll.bytes");
            Assembly _hotUpdateAss = Assembly.Load(_hotUpdateAssData.bytes);

            string sceneabLocalPath = Path.Combine("Release-Win64/HybridCLRTrial_Data/StreamingAssets", "main.ab");
            AssetBundle sceneab = AssetBundle.LoadFromFile(abLocalPath);
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
                Debug.LogError("未找到main场景");
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
