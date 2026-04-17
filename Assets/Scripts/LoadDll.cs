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
using Cysharp.Threading.Tasks;
using UniFramework.Event;
using System.Threading.Tasks;
using FairyGUI;

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

    // FairyGUI相关
    private GComponent _loadingView;               //加载界面
    private GProgressBar _progressBar;             //进度条
    private GTextField _statusText;                //状态文本
    private GComponent _hotupView;                 //更新提示界面
    private GComponent _restartView;               //重启提示界面

    void Awake()
    {
        Application.targetFrameRate = 60;   //设置帧率
        Application.runInBackground = true; //设置后台运行
        DontDestroyOnLoad(gameObject);      //确保该对象不会在场景切换时销毁

        // 初始化FairyGUI
        FairyGUI.Stage.inst.DisableSound();
    }

    public async UniTask Start()
    {
        Debug.Log($"资源系统运行模式：{PlayMode}");
        // 初始化事件系统
        UniEvent.Initalize();
        
        // 再初始化YooAssets
        await InitYooAssets();
    }

    async UniTask InitYooAssets()
    {
        // 1.初始化资源系统
        UpdateUI("初始化资源系统...", 0.1f);
        YooAssets.Initialize();

        // 2.初始化资源包
        UpdateUI("初始化资源包...", 0.2f);
        await InitPackage();
        
        // 先加载FairyGUI界面
        LoadFairyGUI();
        
        // 3.获取资源版本
        UpdateUI("获取资源版本...", 0.3f);
        await UpdatePackageVersion();

        // 4.获取文件清单
        UpdateUI("获取文件清单...", 0.4f);
        await UpdateManifest();

        // 5.创建资源下载器
        UpdateUI("检查更新...", 0.5f);
        if (CreateDownloader())
        {
            // 5.开始下载资源文件
            await BeginDownload();
        }
        else
        {
            UpdateUI("无需更新", 1);
        }

        YooAssets.SetDefaultPackage(_package);
        //6.清理未使用的缓存文件
        UpdateUI("清理缓存...", 0.9f);
        ClearFiles();
    }

    #region 初始化资源包
    private async UniTask InitPackage()
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
            var createParameters = new EditorSimulateModeParameters
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot)
            };
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
            var cacheFSParam = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
            cacheFSParam.AddParameter(FileSystemParametersDefine.DISABLE_ONDEMAND_DOWNLOAD, true);
            HostPlayModeParameters createParameters = new HostPlayModeParameters
            {
                //创建内置文件系统参数
                BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                // BuildinFileSystemParameters = null,
                //创建缓存系统参数
                CacheFileSystemParameters = cacheFSParam
            };
            //执行异步初始化
            initializationOperation = _package.InitializeAsync(createParameters);
        }

        await initializationOperation;

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
    private async UniTask UpdatePackageVersion()
    {
        // 发起异步版本请求
        RequestPackageVersionOperation operation = _package.RequestPackageVersionAsync();
        await operation;

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
    private async UniTask UpdateManifest()
    {
        _operationManifest = _package.UpdatePackageManifestAsync(appVersion);
        await _operationManifest;

        // 处理文件清单结果
        if (_operationManifest.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning(_operationManifest.Error);
        }
        else
        {
            Debug.Log("更新资源清单成功-------------------");
        }
    }
    #endregion

    #region 创建资源下载器
    bool CreateDownloader()
    {
        _downloader = _package.CreateResourceDownloader(downloadingMaxNum, filedTryAgain);
        if (_downloader.TotalDownloadCount == 0)
        {
            Debug.Log("没有需要更新的文件");
            return false;
        }
        else
        {
            // 发现新更新文件后，显示更新提示
            int count = _downloader.TotalDownloadCount;
            long bytes = _downloader.TotalDownloadBytes;
            Debug.Log($"需要更新{count}个文件, 大小是{bytes / 1024 / 1024}MB");
            
            // 显示更新提示界面
            ShowHotupView(count, bytes);
        }
        return true;
    }
    #endregion

    #region 显示更新提示界面
    private void ShowHotupView(int count, long bytes)
    {
        // 创建更新提示界面
        _hotupView = UIPackage.CreateObject("Launch", "GameStartHotupView").asCom;
        GRoot.inst.AddChild(_hotupView);
        
        // 设置更新信息
        GTextField updateInfo = _hotupView.GetChild("updateInfo").asTextField;
        if (updateInfo != null)
        {
            updateInfo.text = $"发现{count}个更新文件，大小{bytes / 1024 / 1024}MB，是否更新？";
        }
        
        // 添加确认按钮点击事件
        GButton confirmBtn = _hotupView.GetChild("confirmBtn").asButton;
        if (confirmBtn != null)
        {
            confirmBtn.onClick.Add(() => {
                // 隐藏更新提示界面
                _hotupView.visible = false;
                // 继续下载
            });
        }
        
        // 添加取消按钮点击事件
        GButton cancelBtn = _hotupView.GetChild("cancelBtn").asButton;
        if (cancelBtn != null)
        {
            cancelBtn.onClick.Add(() => {
                // 隐藏更新提示界面
                _hotupView.visible = false;
                // 取消更新
                _downloader = null;
            });
        }
    }
    #endregion

    #region 加载FairyGUI界面
    private void LoadFairyGUI()
    {
        // 加载UI包
        UIPackage.AddPackage("MyAsset/UI/Launch");
        
        // 创建加载界面
        _loadingView = UIPackage.CreateObject("Launch", "GameStartLoadingView").asCom;
        GRoot.inst.AddChild(_loadingView);
        
        // 获取进度条和状态文本
        _progressBar = _loadingView.GetChild("progressBar").asProgress;
        _statusText = _loadingView.GetChild("statusText").asTextField;
        
        // 初始状态
        UpdateUI("初始化资源系统...", 0);
    }
    #endregion

    #region 更新UI显示
    private void UpdateUI(string status, float progress)
    {
        if (_statusText != null)
        {
            _statusText.text = status;
        }
        if (_progressBar != null)
        {
            _progressBar.value = progress;
        }
    }
    #endregion

    #region 开始下载资源文件
    private async UniTask BeginDownload()
    {
        _downloader.DownloadErrorCallback = DownloadErrorCallback;// 单个文件下载失败
        _downloader.DownloadUpdateCallback = DownloadUpdateCallback;// 下载进度更新
        _downloader.BeginDownload();//开始下载
        await _downloader;

        // 检测下载结果
        if (_downloader.Status != EOperationStatus.Succeed)
        {
            Debug.LogWarning(_operationManifest.Error);
            UpdateUI("下载失败: " + _operationManifest.Error, 0);
        }
        else
        {
            Debug.Log("下载成功-------------------");
            UpdateUI("下载成功", 1);
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
    public void DownloadUpdateCallback(DownloadUpdateData updateData)
    {
        int totalDownloadCount = updateData.TotalDownloadCount;
        int currentDownloadCount = updateData.CurrentDownloadCount;
        long totalDownloadSizeBytes = updateData.TotalDownloadBytes;
        long currentDownloadSizeBytes = updateData.CurrentDownloadBytes;
        float progress = (float)currentDownloadCount / totalDownloadCount;
        
        Debug.Log($"下载进度: {currentDownloadCount}/{totalDownloadCount}, " +
                  $"{currentDownloadSizeBytes / 1024}KB/{totalDownloadSizeBytes / 1024}KB");
        
        UpdateUI($"下载中... {currentDownloadCount}/{totalDownloadCount}", progress);
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
        using var _ = UpdateDone();
    }
    #endregion

    #region 热更新结束回调
    private async Task UpdateDone()
    {
        Debug.Log("热更新结束");
        UpdateUI("热更新完成", 1);

        // 延迟一下，让用户看到完成信息
        await UniTask.Delay(1000);

        // 隐藏加载界面
        if (_loadingView != null)
        {
            _loadingView.visible = false;
        }

        // 显示重启提示界面
        ShowRestartView();
    }
    #endregion

    #region 显示重启提示界面
    private void ShowRestartView()
    {
        // 创建重启提示界面
        _restartView = UIPackage.CreateObject("Launch", "GameStartHotupRestartView").asCom;
        GRoot.inst.AddChild(_restartView);
        
        // 添加重启按钮点击事件
        GButton restartBtn = _restartView.GetChild("restartBtn").asButton;
        if (restartBtn != null)
        {
            restartBtn.onClick.Add(async () => {
                // 隐藏重启提示界面
                _restartView.visible = false;
                // 跳转场景
                Debug.Log("跳转场景");
                await StartGame();
            });
        }
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

    private static List<string> HotUpdateAssemblyFiles { get; } = new() { "Assembly-CSharp", "Obfuz.Runtime", "HotUpdate", };
    async Task StartGame()
    {
#if !UNITY_EDITOR
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
            Debug.Log($"LoadMetadataForAOTAssembly:{hotUpdateDllName}. ");
        }
        Debug.Log("运行热更代码");
#endif
        await Run_InstantiateComponentByAsset();
    }

    async UniTask Run_InstantiateComponentByAsset()
    {
        // 通过实例化assetbundle中的资源，还原资源上的热更新脚本
        var handle = _package.LoadAssetAsync<GameObject>("Cube");
        await handle;
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
