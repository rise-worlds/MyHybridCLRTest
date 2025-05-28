


using System.Collections.Generic;
using System.Threading.Tasks;

namespace RiseClient
{
    public enum BuildABType
    {
        Code,       //代码
        Res,        //资源
    }

    public enum AssetSrc
    {
        Resource, // 内置目录
        AssetBundle, // ab热更新
        Raw, // 原始数据
    }

    public sealed class ResMgr
    {
        public static string AB_EXT = ".ab";
        public static string AB_SPLIT = "|";
        public static string AB_SOLO_SPLIT = ";";

        private static Dictionary<string, ResEntity> _assetMap = new Dictionary<string, ResEntity>();
        private static Dictionary<string, AssetSrc> _srcMap = new Dictionary<string, AssetSrc>();
        private static Dictionary<string, int> _packageRefs = new Dictionary<string, int>();
        public static AssetSrc GetAssetSrc(string abUrl)
        {
            return Utils.GetDictByKey(ResMgr._srcMap, abUrl, AssetSrc.Resource);
        }
        public static ResEntity GetResEntity(string abUrl)
        {
            ResEntity ret = null;
            ResMgr._assetMap.TryGetValue(abUrl, out ret);
            if (ret == null)
            {
                ret = new ResEntity(abUrl);
                ResMgr._assetMap.Add(abUrl, ret);
            }
            return ret;
        }
        private static string FormateAbUrl(string abUrl)
        {
            abUrl = abUrl.ToLower();
            if (!Utils.HasExtension(abUrl, ResMgr.AB_EXT))
            {
                abUrl += ResMgr.AB_EXT;
            }
            return abUrl;
        }

        public static async Task<T> LoadResEntity<T>(string path, BuildABType type = BuildABType.Code) where T : UnityEngine.Object
        {
            path = path.Replace(ResMgr.AB_SPLIT, "/").Replace(ResMgr.AB_SOLO_SPLIT, "/");
            var pathArr = path.Split(ResMgr.AB_SPLIT.ToCharArray());
            var abUrl = pathArr.Length > 1 ? ResMgr.FormateAbUrl(pathArr[0]) : pathArr[0].ToLower();
            var resUrl = pathArr.Length > 1 ? pathArr[1] : pathArr[0];
            if (!ResMgr._srcMap.TryGetValue(abUrl, out AssetSrc assetSrc))
            {
                ResMgr._srcMap[abUrl] = pathArr.Length > 1 ? AssetSrc.AssetBundle : AssetSrc.Resource;
            }
            var resEntity = ResMgr.GetResEntity(abUrl);
            if (resEntity.HasResObj<T>(resUrl))
            {
                return resEntity.GetResObj<T>(resUrl);
            }
            return await resEntity.Load<T>(resUrl, type);
        }
    }
}