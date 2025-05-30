
// 平台判断辅助类
using UnityEngine;
namespace RiseClient
{
    public static class Platform
    {
        public static bool isEditor
        {
#if UNITY_EDITOR
            get { return true; }
#else
            get { return false; }
#endif
        }

        public static bool isRelease
        {
#if UNITY_EDITOR
            get { return false; }
#else
            get { return true; }
#endif
        }


        public static bool isStandalone
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
            get { return true; }
#else
            get { return false; }
#endif
        }

        public static bool isMobile
        {
#if UNITY_ANDROID || UNITY_IPHONE
            get { return true; }
#else
            get { return false; }
#endif
        }

        public static bool isWindows =>
#if UNITY_STANDALONE_WIN
            true;
#else
            false;
#endif

        public static bool isOSX =>
#if UNITY_STANDALONE_OSX
            true;
#else
            false;
#endif

        public static bool isLinux =>
#if UNITY_STANDALONE_LINUX
            true;
#else
            false;
#endif

        public static bool isIphone =>
#if UNITY_IOS
            true;
#else
            false;
#endif

        public static bool isAndroid =>
#if UNITY_ANDROID
            true;
#else
            false;
#endif

        public static string platformName
        {
            get
            {
                if (Platform.isWindows)
                {
                    return "windows";
                }
                else if (Platform.isOSX)
                {
                    return "osx";
                }
                else if (Platform.isLinux)
                {
                    return "linux";
                }
                else if (Platform.isIphone)
                {
                    return "ios";
                }
                else if (Platform.isAndroid)
                {
                    return "android";
                }
                return "other";
            }
        }

        public static string AssetsBundleResPath
        {
            get
            {
                return Application.dataPath + "/AssetsBundleRes";
            }
        }

        public static string persistentAssetPath
        {
            get { return string.Concat(Application.persistentDataPath, "/"); }
        }


        public static string streamingAssetPath
        {
            get { return string.Concat(Application.streamingAssetsPath, "/"); }
        }
        public static string cachingAssetPath
        {
            get { return string.Concat(Application.temporaryCachePath, "/"); }
        }
        public static string assetBundleBuildPath
        {
            get
            {
                return Application.dataPath + "/../AssetBundles" + "/" + Platform.platformName + "/";
            }
        }
        public static string assetBundlePath
        {
            get
            {
                if (Platform.isEditor || Platform.isStandalone)
                {
                    return Platform.streamingAssetPath;
                }
                else
                {
                    return Platform.persistentAssetPath;
                }
            }
        }
        public static string tmpPath
        {
            get
            {
                if (Platform.isEditor)
                {
                    return Application.dataPath + "/../Temp/";
                }
                else if (Platform.isStandalone)
                {
                    return Application.dataPath + "/Temp/";
                }
                else
                {
                    return Platform.persistentAssetPath;
                }
            }
        }
    }
}