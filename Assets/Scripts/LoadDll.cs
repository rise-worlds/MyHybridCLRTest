using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HybridCLR;
using UnityEngine;
using YooAsset;
using Obfuz;
using Obfuz.EncryptionVM;

/// <summary>
/// 脚本工作流程：
/// 1.下载资源，用yooAsset资源框架进行下载
///    1.资源文件，ab包
///    2.热更新dll
/// 2.给AOT DLL补充元素据，通过RuntimeApi.LoadMetadataForAOTAssembly
/// 3.通过实例化prefab，运行热更代码
/// </summary>
public class LoadDll : MonoBehaviour
{
    /// <summary>
    /// 资源系统运行模式
    /// </summary>
    public EPlayMode PlayMode = EPlayMode.HostPlayMode;
    string appVersion = "v1.0";                     //版本号
    public string packageName = "DefaultPackage";   //默认包名
    public int downloadingMaxNum = 10;              //最大下载数量
    public int filedTryAgain = 3;                   //失败重试次数

    private ResourcePackage _package = null;        //资源包对象
    private ResourceDownloaderOperation _downloader;//下载器
    private UpdatePackageManifestOperation _operationManifest;//更新清单

    void Start()
    {
        Debug.Log($"资源系统运行模式：{PlayMode}");
        Application.targetFrameRate = 60;   //设置帧率
        Application.runInBackground = true; //设置后台运行
        DontDestroyOnLoad(gameObject);      //确保该对象不会在场景切换时销毁

        StartCoroutine(InitYooAssets());
    }

    IEnumerator InitYooAssets()
    {
        // 1.初始化资源系统
        YooAssets.Initialize();

        // 2.初始化资源包
        yield return StartCoroutine(InitPackage());
        
        // 3.获取资源版本
        yield return StartCoroutine(UpdatePackageVersion());

        // 4.获取文件清单
        yield return StartCoroutine(UpdateManifest());

        // 5.创建资源下载器
        CreateDownloader();

        // 5.开始下载资源文件
        yield return StartCoroutine(BeginDownload());

        //判断是否下载成功
        var assets = new List<string> { "HotUpdate.dll" }.Concat(AOTMetaAssemblyFiles);
        foreach (var asset in assets)
        {
            var handle = _package.LoadAssetAsync<TextAsset>(asset);
            yield return handle;
            var assetObj = handle.AssetObject as TextAsset;
            s_assetDatas[asset] = assetObj;
            Debug.Log($"dll:{asset}   {assetObj == null}");
        }
        YooAssets.SetDefaultPackage(_package);

        //6.清理未使用的缓存文件
        ClearFiles();
    }

    #region 初始化资源包
    private IEnumerator InitPackage()
    {
        // 获取或创建资源包对象
        _package = YooAssets.TryGetPackage(packageName);
        if (_package == null)
            _package = YooAssets.CreatePackage(packageName);

        // 编辑器下的模拟模式
        InitializationOperation initializationOperation = null;
        if (PlayMode == EPlayMode.EditorSimulateMode)
        {
            var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
            var packageRoot = buildResult.PackageRootDirectory;
            var createParameters = new EditorSimulateModeParameters();
            createParameters.EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
            initializationOperation = _package.InitializeAsync(createParameters);
        }
        else if (PlayMode == EPlayMode.OfflinePlayMode)
        {
            // 单机运行模式
            var createParameters = new OfflinePlayModeParameters();
            createParameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            initializationOperation = _package.InitializeAsync(createParameters);
        }
        else if (PlayMode == EPlayMode.HostPlayMode)
        {
            // 联机运行模式
            //创建远端服务实例，用于资源请求
            string defaultHostServer = GetHostServerURL();
            string fallbackHostServer = GetHostServerURL();
            IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);


            // 创建联机模式参数，并设置内置及缓存文件系统参数
            HostPlayModeParameters createParameters = new HostPlayModeParameters
            {
                //创建内置文件系统参数
                //BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                BuildinFileSystemParameters = null,
                //创建缓存系统参数
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices)
            };
            //执行异步初始化
            initializationOperation = _package.InitializeAsync(createParameters);
        }
        else if (PlayMode == EPlayMode.WebPlayMode)
        {
            // WebGL运行模式
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
            var createParameters = new WebPlayModeParameters();
			string defaultHostServer = GetHostServerURL();
            string fallbackHostServer = GetHostServerURL();
            string packageRoot = $"{WeChatWASM.WX.env.USER_DATA_PATH}/__GAME_FILE_CACHE"; //注意：如果有子目录，请修改此处！
            IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
            createParameters.WebServerFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices);
            initializationOperation = _package.InitializeAsync(createParameters);
#else
            var createParameters = new WebPlayModeParameters();
            createParameters.WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
            initializationOperation = _package.InitializeAsync(createParameters);
#endif
        }

        yield return initializationOperation;

        // 如果初始化失败弹出提示界面
        if (initializationOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning($"{initializationOperation.Error}");
        }
        else
        {
            Debug.Log("初始化成功-------------------------");
        }
    }

    private string GetHostServerURL()
    {
        //模拟下载地址，项目名，平台名
#if UNITY_ANDROID
        return "https://tools.qipai360.cn/TestProject/Android";
#elif UNITY_IOS
        return "https://tools.qipai360.cn/TestProject/iOS";
#endif
    }

    /// <summary>
    /// 远端资源地址查询服务类
    /// </summary>
    private class RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }
    }

#endregion

    #region 获取资源版本
    private IEnumerator UpdatePackageVersion()
    {
        // 发起异步版本请求
        RequestPackageVersionOperation operation = _package.RequestPackageVersionAsync();
        yield return operation;

        // 处理版本请求结果
        if (operation.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning(operation.Error);
        }
        else
        {
            Debug.Log($"请求的版本: {operation.PackageVersion}");
            appVersion = operation.PackageVersion;
        }
    }
    #endregion

    #region 获取文件清单
    private IEnumerator UpdateManifest()
    {
        _operationManifest = _package.UpdatePackageManifestAsync(appVersion);
        yield return _operationManifest;

        // 处理文件清单结果
        if (_operationManifest.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning(_operationManifest.Error);
            yield break;
        }
        else
        {
            Debug.Log("更新资源清单成功-------------------");
        }
    }
    #endregion

    #region 创建资源下载器
    void CreateDownloader()
    {
        _downloader = _package.CreateResourceDownloader(downloadingMaxNum, filedTryAgain);
        if (_downloader.TotalDownloadCount == 0)
        {
            Debug.Log("没有需要更新的文件");
            UpdateDone();
        }
        else
        {
            // 发现新更新文件后，挂起流程系统
            // 注意：开发者需要在下载前检测磁盘空间不足
            int count = _downloader.TotalDownloadCount;
            long bytes = _downloader.TotalDownloadBytes;
            Debug.Log($"需要更新{count}个文件, 大小是{bytes / 1024 / 1024}MB");
        }
    }
    #endregion

    #region 开始下载资源文件
    private IEnumerator BeginDownload()
    {
        _downloader.DownloadErrorCallback = DownloadErrorCallback;// 单个文件下载失败
        _downloader.DownloadUpdateCallback = DownloadUpdateCallback;// 下载进度更新
        _downloader.BeginDownload();//开始下载
        yield return _downloader;

        // 检测下载结果
        if (_downloader.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning(_operationManifest.Error);
            yield break;
        }
        else
        {
            Debug.Log("下载成功-------------------");
        }
    }

    // 单个文件下载失败
    public static void DownloadErrorCallback(DownloadErrorData errorData)
    {
        string fileName = errorData.FileName;
        string errorInfo = errorData.ErrorInfo;
        Debug.Log($"下载失败, 文件名: {fileName}, 错误信息: {errorInfo}");
    }

    // 下载进度更新
    public static void DownloadUpdateCallback(DownloadUpdateData updateData)
    {
        int totalDownloadCount = updateData.TotalDownloadCount;
        int currentDownloadCount = updateData.CurrentDownloadCount;
        long totalDownloadSizeBytes = updateData.TotalDownloadBytes;
        long currentDownloadSizeBytes = updateData.CurrentDownloadBytes;
        Debug.Log($"下载进度: {currentDownloadCount}/{totalDownloadCount}, " +
                  $"{currentDownloadSizeBytes / 1024}KB/{totalDownloadSizeBytes / 1024}KB");
    }
    #endregion

    #region 清理未使用的缓存文件
    void ClearFiles()
    {
        var operationClear = _package.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);// 清理未使用的文件
        operationClear.Completed += Operation_Completed;// 添加清理完成回调
    }

    //文件清理完成
    private void Operation_Completed(AsyncOperationBase obj)
    {
        UpdateDone();
    }
    #endregion

    #region 热更新结束回调
    private void UpdateDone()
    {
        Debug.Log("热更新结束");

        //跳转场景
        Debug.Log("跳转场景");

        StartGame();
    }
    #endregion

    #region 补充元数据

    //补充元数据dll的列表
    //通过RuntimeApi.LoadMetadataForAOTAssembly()函数来补充AOT泛型的原始元数据
    private static List<string> AOTMetaAssemblyFiles { get; } = new() { "mscorlib.dll", "System.dll", "System.Core.dll", };
    private static Dictionary<string, TextAsset> s_assetDatas = new Dictionary<string, TextAsset>();
    
    public static byte[] ReadBytesFromStreamingAssets(string dllName)
    {
        if (s_assetDatas.ContainsKey(dllName))
        {
            return s_assetDatas[dllName].bytes;
        }

        return Array.Empty<byte>();
    }

    /// <summary>
    /// 为aot assembly加载原始metadata， 这个代码放aot或者热更新都行。
    /// 一旦加载后，如果AOT泛型函数对应native实现不存在，则自动替换为解释模式执行
    /// </summary>
    private static void LoadMetadataForAOTAssemblies()
    {
        /// 注意，补充元数据是给AOT dll补充元数据，而不是给热更新dll补充元数据。
        /// 热更新dll不缺元数据，不需要补充，如果调用LoadMetadataForAOTAssembly会返回错误
        HomologousImageMode mode = HomologousImageMode.SuperSet;
        foreach (var aotDllName in AOTMetaAssemblyFiles)
        {
            byte[] dllBytes = ReadBytesFromStreamingAssets(aotDllName);
            // 加载assembly对应的dll，会自动为它hook。一旦aot泛型函数的native函数不存在，用解释器版本代码
            LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, mode);
            Debug.Log($"LoadMetadataForAOTAssembly:{aotDllName}. mode:{mode} ret:{err}");
        }
    }

    #endregion

    #region 运行测试

    private static List<string> HotUpdateAssemblyFiles { get; } = new() { "XLua.runtime", "HotUpdate", };
    void StartGame()
    {
        // 加载AOT dll的元数据
        LoadMetadataForAOTAssemblies();
        // 加载热更dll
        foreach (var hotUpdateDllName in HotUpdateAssemblyFiles)
        {
#if !UNITY_EDITOR
            Assembly.Load(ReadBytesFromStreamingAssets($"{hotUpdateDllName}.dll"));
#else
            System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == hotUpdateDllName);
#endif
        }
        Debug.Log("运行热更代码");
        StartCoroutine(Run_InstantiateComponentByAsset());
    }

    IEnumerator Run_InstantiateComponentByAsset()
    {
        // 通过实例化assetbundle中的资源，还原资源上的热更新脚本
        var handle = _package.LoadAssetAsync<GameObject>("Cube");
        yield return handle;
        handle.Completed += Handle_Completed;
    }

    private void Handle_Completed(AssetHandle obj)
    {
        Debug.Log("准备实例化");
        GameObject go = obj.InstantiateSync();
        Debug.Log($"Prefab name is {go.name}");
    }

    #endregion

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void SetUpStaticSecretKey()
    {
        Debug.Log("SetUpStaticSecret begin");
        EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
        Debug.Log("SetUpStaticSecret end");
    }
}
