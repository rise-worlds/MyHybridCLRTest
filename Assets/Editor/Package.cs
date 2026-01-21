using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using Obfuz.Settings;
using Obfuz4HybridCLR;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [MenuItem("Build/编译打包", false, 0)]
        public static void BuildPlayer()
        {
            // Get filename.
#if UNITY_STANDALONE_WIN
            BuildTarget target = BuildTarget.StandaloneWindows64;
            BuildTargetGroup group = BuildTargetGroup.Standalone;
            string outputPath = $"{SettingsUtil.ProjectDir}/Release-Win64";
            string location = $"{outputPath}/HybridCLRTrial.exe";
#elif UNITY_ANDROID
            BuildTarget target = BuildTarget.Android;
            BuildTargetGroup group = BuildTargetGroup.Android;
            string outputPath = $"{SettingsUtil.ProjectDir}/Release-Android";
            string location = $"{outputPath}/HybridCLRTrial.apk";
#elif UNITY_IOS
            BuildTarget target = BuildTarget.iOS;
            BuildTargetGroup group = BuildTargetGroup.iOS;
            string outputPath = $"{SettingsUtil.ProjectDir}/Release-iOS";
            string location = $"{outputPath}/HybridCLRTrial.app";
#endif
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            var buildOptions = BuildOptions.CompressWithLz4;


            BuildCodeAssetBundle();
            BuildResAssetBundle();
            Debug.Log("====> Build App");
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions()
            {
                scenes = new string[] { "Assets/Scenes/launcher.unity", "Assets/Scenes/main.unity" },
                locationPathName = location,
                options = buildOptions,
                target = target,
                targetGroup = group,
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
            foreach (var dll in SettingsUtil.HotUpdateAssemblyFilesExcludePreserved)
            {
                string name = $"{dll}.bytes";
                Debug.Log(name);
                string srcPath = Path.Combine(Application.streamingAssetsPath, name);
                string dstPath = Path.Combine(assetBundleDirectory, name);
                File.Copy(srcPath, dstPath, true);
                File.Delete(srcPath);

                AssetBundleBuild resBuild = new AssetBundleBuild();
                resBuild.assetBundleName = $"{name}{ResMgr.AB_EXT}";
                resBuild.assetNames = new string[] { dstPath };

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

        [MenuItem("Build/混淆")]
        public static void CompileAndObfuscateAndCopyToStreamingAssets()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            CompileDllCommand.CompileDll(target);

            string obfuscatedHotUpdateDllPath = PrebuildCommandExt.GetObfuscatedHotUpdateAssemblyOutputPath(target);
            ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);

            Directory.CreateDirectory(Application.streamingAssetsPath);

            string hotUpdateDllPath = $"{SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)}";
            List<string> obfuscationRelativeAssemblyNames = ObfuzSettings.Instance.assemblySettings.GetObfuscationRelativeAssemblyNames();

            foreach (string assName in SettingsUtil.HotUpdateAssemblyNamesIncludePreserved)
            {
                string srcDir = obfuscationRelativeAssemblyNames.Contains(assName) ? obfuscatedHotUpdateDllPath : hotUpdateDllPath;
                string srcFile = $"{srcDir}/{assName}.dll";
                string dstFile = $"{Application.streamingAssetsPath}/{assName}.dll.bytes";
                if (File.Exists(srcFile))
                {
                    File.Copy(srcFile, dstFile, true);
                    Debug.Log($"[CompileAndObfuscate] Copy {srcFile} to {dstFile}");
                }
            }
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