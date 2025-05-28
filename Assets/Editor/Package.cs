using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace RiseClient.Editor
{
    /// <summary>
    /// 资源打包工具类
    /// </summary>
    [InitializeOnLoad]
    public class Package
    {
        public static string HybridCLRBuildCacheDir => Application.dataPath + "/HybridCLRBuildCache";

        public static string AssetBundleOutputDir => $"{HybridCLRBuildCacheDir}/AssetBundleOutput";

        public static string AssetBundleSourceDataTempDir => $"{HybridCLRBuildCacheDir}/AssetBundleSourceData";

        [MenuItem("Build/编译", false, 0)]
        public static void BuildPlayer()
        {
            BuildTarget target = BuildTarget.StandaloneWindows64;
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            if (activeTarget != BuildTarget.StandaloneWindows64 && activeTarget != BuildTarget.StandaloneWindows)
            {
                Debug.LogError("请先切到Win平台再打包");
                return;
            }
            // Get filename.
            string outputPath = $"{SettingsUtil.ProjectDir}/Release-Win64";
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var buildOptions = BuildOptions.CompressWithLz4;

            string location = $"{outputPath}/HybridCLRTrial.exe";

            BuildCodeAssetBundle();
            BuildResAssetBundle();
            Debug.Log("====> Build App");
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
            {
                scenes = new string[] { "Assets/Scenes/launcher.unity", "Assets/Scenes/main.unity" },
                locationPathName = location,
                options = buildOptions,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("打包失败");
                return;
            }
        }

        [MenuItem("Build/打代码AB包", false, 1)]
        public static void BuildCodeAssetBundle()
        {
            PrebuildCommand.GenerateAll();
            Debug.Log("====> 复制热更新资源和代码");
            BuildAndCopyABAOTHotUpdateDlls();

            string assetBundleDirectory = "Assets/HotUpdateAssemblies";
            if (!Directory.Exists(assetBundleDirectory))
            {
                Directory.CreateDirectory(assetBundleDirectory);
            }

            List<AssetBundleBuild> buildList = new List<AssetBundleBuild>();
            DirectoryInfo directory = new DirectoryInfo(Application.streamingAssetsPath);
            FileInfo[] files = directory.GetFiles("*.dll.bytes", SearchOption.TopDirectoryOnly);
            foreach (FileInfo file in files)
            {
                string path = "Assets" + file.FullName.Replace('\\', '/').Replace(Application.dataPath, "");
                path = $"{assetBundleDirectory}/{file.Name}";
                Debug.Log(path);
                File.Copy(file.FullName, path, true);
                File.Delete(file.FullName);

                AssetBundleBuild resBuild = new AssetBundleBuild();
                resBuild.assetBundleName = $"{file.Name}{ResMgr.AB_EXT}";
                resBuild.assetNames = new string[] { path };

                buildList.Add(resBuild);
            }
            AssetDatabase.Refresh();
            AssetBundleManifest result = BuildPipeline.BuildAssetBundles(Application.streamingAssetsPath, buildList.ToArray(), BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);
            Directory.Delete(assetBundleDirectory, true);

            Debug.Log("代码AB包已打包到: " + Application.streamingAssetsPath);
        }

        [MenuItem("Build/打资源AB包", false, 2)]
        public static void BuildResAssetBundle()
        {

            AssetBundleBuild[] buildMap = new AssetBundleBuild[1];
            buildMap[0].assetBundleName = "main.ab";
            string[] enemyAssets = new string[1];
            enemyAssets[0] = $"Assets/Scenes/main.unity";
            buildMap[0].assetNames = enemyAssets;
            AssetBundleManifest result = BuildPipeline.BuildAssetBundles(Application.streamingAssetsPath, buildMap, BuildAssetBundleOptions.None, EditorUserBuildSettings.activeBuildTarget);

            AssetDatabase.Refresh();
            Debug.Log("资源AB包已打包到: " + Application.streamingAssetsPath);
        }

        [MenuItem("Build/清除本地数据")]
        public static void ClearStorage()
        {
            PlayerPrefs.DeleteAll();
        }

        public static BuildTarget buildTarget
        {
            get
            {
                if (Platform.isWindows)
                {
                    return BuildTarget.StandaloneWindows64;
                }
                else if (Platform.isOSX)
                {
                    return BuildTarget.StandaloneOSX;
                }
                else if (Platform.isLinux)
                {
                    return BuildTarget.StandaloneLinux64;
                }
                else if (Platform.isIphone)
                {
                    return BuildTarget.iOS;
                }
                else if (Platform.isAndroid)
                {
                    return BuildTarget.Android;
                }
                return BuildTarget.StandaloneWindows64;
            }
        }


        public static string GetAssetBundleOutputDirByTarget(BuildTarget target)
        {
            return $"{AssetBundleOutputDir}/{target}";
        }

        public static string GetAssetBundleTempDirByTarget(BuildTarget target)
        {
            return $"{AssetBundleSourceDataTempDir}/{target}";
        }

        public static string ToRelativeAssetPath(string s)
        {
            return s.Substring(s.IndexOf("Assets/"));
        }

        /// <summary>
        /// 将HotFix.dll和HotUpdatePrefab.prefab打入common包.
        /// 将HotUpdateScene.unity打入scene包.
        /// </summary>
        /// <param name="tempDir"></param>
        /// <param name="outputDir"></param>
        /// <param name="target"></param>
        private static void BuildAssetBundles(string tempDir, string outputDir, BuildTarget target)
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(outputDir);

            List<AssetBundleBuild> abs = new List<AssetBundleBuild>();

            {
                var prefabAssets = new List<string>();
                string testPrefab = $"{Application.dataPath}/Prefabs/Cube.prefab";
                prefabAssets.Add(testPrefab);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                abs.Add(new AssetBundleBuild
                {
                    assetBundleName = "prefabs",
                    assetNames = prefabAssets.Select(s => ToRelativeAssetPath(s)).ToArray(),
                });
            }

            BuildPipeline.BuildAssetBundles(outputDir, abs.ToArray(), BuildAssetBundleOptions.None, target);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        public static void BuildAssetBundleByTarget(BuildTarget target)
        {
            BuildAssetBundles(GetAssetBundleTempDirByTarget(target), GetAssetBundleOutputDirByTarget(target), target);
        }

        // [MenuItem("Build/BuildAssetsAndCopyToStreamingAssets")]
        public static void BuildAndCopyABAOTHotUpdateDlls()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildAssetBundleByTarget(target);
            CompileDllCommand.CompileDll(target);
            CopyABAOTHotUpdateDlls(target);
            AssetDatabase.Refresh();
        }

        public static void CopyABAOTHotUpdateDlls(BuildTarget target)
        {
            CopyAssetBundlesToStreamingAssets(target);
            CopyAOTAssembliesToStreamingAssets();
            CopyHotUpdateAssembliesToStreamingAssets();
        }


        //[MenuItem("HybridCLR/Build/BuildAssetbundle")]
        public static void BuildSceneAssetBundleActiveBuildTargetExcludeAOT()
        {
            BuildAssetBundleByTarget(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CopyAOTAssembliesToStreamingAssets()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            string aotAssembliesSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            string aotAssembliesDstDir = Application.streamingAssetsPath;

            foreach (var dll in SettingsUtil.AOTAssemblyNames)
            {
                string srcDllPath = $"{aotAssembliesSrcDir}/{dll}.dll";
                if (!File.Exists(srcDllPath))
                {
                    Debug.LogError($"ab中添加AOT补充元数据dll:{srcDllPath} 时发生错误,文件不存在。裁剪后的AOT dll在BuildPlayer时才能生成，因此需要你先构建一次游戏App后再打包。");
                    continue;
                }
                string dllBytesPath = $"{aotAssembliesDstDir}/{dll}.dll.bytes";
                File.Copy(srcDllPath, dllBytesPath, true);
                Debug.Log($"[CopyAOTAssembliesToStreamingAssets] copy AOT dll {srcDllPath} -> {dllBytesPath}");
            }
        }

        public static void CopyHotUpdateAssembliesToStreamingAssets()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;

            string hotfixDllSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);
            string hotfixAssembliesDstDir = Application.streamingAssetsPath;
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                string dllPath = $"{hotfixDllSrcDir}/{dll}";
                string dllBytesPath = $"{hotfixAssembliesDstDir}/{dll}.bytes";
                File.Copy(dllPath, dllBytesPath, true);
                Debug.Log($"[CopyHotUpdateAssembliesToStreamingAssets] copy hotfix dll {dllPath} -> {dllBytesPath}");
            }
        }

        public static void CopyAssetBundlesToStreamingAssets(BuildTarget target)
        {
            string streamingAssetPathDst = Application.streamingAssetsPath;
            Directory.CreateDirectory(streamingAssetPathDst);
            string outputDir = GetAssetBundleOutputDirByTarget(target);
            var abs = new string[] { "prefabs" };
            foreach (var ab in abs)
            {
                string srcAb = ToRelativeAssetPath($"{outputDir}/{ab}");
                string dstAb = ToRelativeAssetPath($"{streamingAssetPathDst}/{ab}");
                Debug.Log($"[CopyAssetBundlesToStreamingAssets] copy assetbundle {srcAb} -> {dstAb}");
                AssetDatabase.CopyAsset(srcAb, dstAb);
            }
        }
    }
}
#endif