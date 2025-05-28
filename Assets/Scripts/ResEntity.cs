
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace RiseClient
{
    public enum LoadState
    {
        Null, // 未开始
        Loading, // 加载中
        Ok, // 加载完成
        Fail // 加载失败
    }

    public class ResEntity
    {
        public string abUrl { get; private set; } = "";
        public LoadState loadState { get; private set; } = LoadState.Null;

        public bool isLoaded
        {
            get { return loadState == LoadState.Ok; }
        }

        private ObservableObject<AssetBundle> _assetBundle = new ObservableObject<AssetBundle>(null);
        private Dictionary<string, ObservableObject<object>> _resMap = new Dictionary<string, ObservableObject<object>>();
        private Dictionary<string, ObservableObject<object[]>> _allResMap = new Dictionary<string, ObservableObject<object[]>>();

        public ResEntity(string abUrl)
        {
            this.abUrl = abUrl;
            var src = ResMgr.GetAssetSrc(this.abUrl);
            if (src == AssetSrc.Resource)
            {
                this.loadState = LoadState.Ok;
            }
        }

        public bool HasResObj<T>(string resUrl) where T : class
        {
            if (this.loadState != LoadState.Ok)
            {
                return false;
            }
            System.Type type = typeof(T);
            string resKey = string.Format("{0}_{1}", resUrl, type.ToString());
            return this._resMap.ContainsKey(resKey);
        }

        public T GetResObj<T>(string resUrl) where T : class
        {
            ObservableObject<object> res;
            System.Type type = typeof(T);
            string resKey = string.Format("{0}_{1}", resUrl, type.ToString());
            if (this._resMap.TryGetValue(resKey, out res))
            {
                return res.value as T;
            }
            return null;
        }

        public async Task<T> LoadByAssetBundle<T>(string resUrl, BuildABType abtype) where T : UnityEngine.Object
        {
            resUrl = resUrl.ToLower();
            System.Type type = typeof(T);
            string resKey = string.Format("{0}_{1}", resUrl, type.ToString().ToLower());
            ObservableObject<object> resObj;
            if (!this._resMap.TryGetValue(resKey, out resObj))
            {
                resObj = new ObservableObject<object>(null);
                this._resMap.Add(resKey, resObj);
            }
            if (resObj.value != null)
            {
                return resObj.value as T;
            }
            AssetBundle ab = await this.LoadAssetBundle(abtype);
            if (null == ab)
            {
                //Tracker.Warn($"加载ab{this.abUrl}失败,请确认AB路径和打包ab平台是否正确");
                return null;
            }

            // Fix mobile shader running in editor mode.
            var lastName = $"{Utils.GetPathWithoutExtension(abUrl)}/{resUrl}";
            T obj = ab.LoadAsset<T>("assets/" + lastName); // 仅支持ab包有目录
            //if (Platform.isEditor && !Platform.isStandalone && typeof(T) == typeof(GameObject))
            //{
            //    this.ReloadAllShader(obj as GameObject);
            //}
            //if (obj == null)
            //{
            //    Debug.Log($"AB包加载资源为空 lastName:{lastName}");
            //}

            resObj.value = obj;
            return obj;
        }

        /// <summary>
        /// 加载所有内部资源
        /// </summary>
        /// <param name="resUrl"></param>
        /// <param name="type"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T[] LoadAllByAssetInterior<T>(string resUrl) where T : class
        {
            if (string.IsNullOrEmpty(resUrl))
            {
                resUrl = this.abUrl;
            }
            ObservableObject<object[]> res;
            System.Type type = typeof(T);
            string resKey = string.Format("{0}_{1}", resUrl, type.ToString());
            if (!this._allResMap.TryGetValue(resKey, out res))
            {
                res = new ObservableObject<object[]>(null);
                this._allResMap.Add(resKey, res);
            }
            if (res.value != null)
            {
                return res.value as T[];
            }
            resUrl = resUrl.Replace("Assets/", "");

            UnityEngine.Object[] objArr;
#if UNITY_EDITOR
            objArr = AssetDatabase.LoadAllAssetsAtPath("Assets/" + resUrl);
            //if (!Platform.isStandalone && type == typeof(GameObject))
            //{
            //    foreach (var obj in objArr)
            //    {
            //        this.ReloadAllShader(obj as GameObject);
            //    }
            //}
#else
            resUrl = resUrl.Replace("Resources/", "");
            resUrl = Utils.GetPathWithoutExtension(resUrl);
            objArr = Resources.LoadAll(resUrl);
#endif

            // 类型过滤
            var tmpArr = new List<T>();
            foreach (var obj in objArr)
            {
                var objType = obj.GetType();
                if (objType == type || objType.IsSubclassOf(type)) tmpArr.Add(obj as T);
            }
            var retArr = tmpArr.ToArray();
            res.value = retArr;
            return retArr;
        }

        public T LoadByAssetInterior<T>(string resUrl) where T : class
        {
            if (string.IsNullOrEmpty(resUrl))
            {
                resUrl = this.abUrl;
            }
            System.Type type = typeof(T);
            string resKey = string.Format("{0}_{1}", resUrl, type.ToString());
            ObservableObject<object> res;
            if (!this._resMap.TryGetValue(resKey, out res))
            {
                res = new ObservableObject<object>(null);
                this._resMap.Add(resKey, res);
            }
            if (res.value != null)
            {
                return res.value as T;
            }
            resUrl = resUrl.Replace("Assets/", "");

#if UNITY_EDITOR
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath("Assets/" + resUrl, type);
            // if (!Platform.isStandalone && type == typeof(GameObject))
            // {
            //     this.ReloadAllShader(obj as GameObject);
            // }
            res.value = obj;
#else
            resUrl = resUrl.Replace("Resources/", "");
            resUrl = Utils.GetPathWithoutExtension(resUrl);
            res.value = Resources.Load(resUrl, typeof(T));
#endif
            return res.value as T;
        }

        private async Task<AssetBundle> LoadAssetBundle(BuildABType abtype)
        {
            AssetBundle ab = null;
            if (this.isLoaded)
            {
                ab = this._assetBundle.value;
                if (ab != null)
                {
                    return ab;
                }
                this.loadState = LoadState.Fail;
            }
            if (this.loadState == LoadState.Loading)
            {
                var tcs = new TaskCompletionSource<AssetBundle>();
                this._assetBundle.Update((data) =>
                {
                    tcs.SetResult(data);
                });
                ab = await tcs.Task;
            }
            else
            {
                this.loadState = LoadState.Loading;
                string fullPath = GetFullPath(abtype, this.abUrl);
                var abList = AssetBundle.GetAllLoadedAssetBundles();
                foreach (var item in abList)
                {
                    if (item.name.Equals(this.abUrl))
                    {
                        ab = item;
                        break;
                    }
                }
                if (ab == null)
                {
                    ab = await LoadFromFileAsync(fullPath);
                }
                this._assetBundle.value = ab;
                this.loadState = ab != null ? LoadState.Ok : LoadState.Fail;
            }
            return ab;
        }

        public async Task<T> Load<T>(string resUrl, BuildABType type) where T : UnityEngine.Object
        {
            AssetSrc src = ResMgr.GetAssetSrc(this.abUrl);
            T obj = null;
            if (src == AssetSrc.AssetBundle)
            {
                obj = await this.LoadByAssetBundle<T>(resUrl, type);
            }
            else
            {
                obj = this.LoadByAssetInterior<T>(resUrl);
            }
            if (obj != null)
            {
                //Tracker.Info("加载资源Ok:" + resUrl);
            }
            else
            {
                //Tracker.Error("加载资源失败:" + resUrl);
            }
            return obj;
        }

        public string GetFullPath(BuildABType abtype, string path)
        {
            string fullPath = string.Empty;
            //判断沙盒目录是否有对应AB包，有的话从沙盒目录加载，如果没有则从StreamingAsset加载
            string streamingAssetABPath = $"{Application.streamingAssetsPath}/{abtype}/{path.ToLower()}";
            string persistentAssetABPath = $"{Application.persistentDataPath}/{abtype}/{path.ToLower()}";
            fullPath = File.Exists(persistentAssetABPath) ? "file:///" + persistentAssetABPath : streamingAssetABPath;
            return fullPath;
        }

        public async Task<AssetBundle> LoadFromFileAsync(string path)
        {
            var request = AssetBundle.LoadFromFileAsync(path);
            while (!request.isDone)
            {
                await Task.Yield();
            }
            return request.assetBundle;
            // return await AssetBundle.LoadFromFileAsync(path);
        }
        public AssetBundle LoadFromFileSync(string path)
        {
            return AssetBundle.LoadFromFile(path);
        }
    }
}