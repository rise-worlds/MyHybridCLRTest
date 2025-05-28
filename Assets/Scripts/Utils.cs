using System.Collections.Generic;

namespace RiseClient
{
    public class Utils
    {
        public static T2 GetDictByKey<T1, T2>(Dictionary<T1, T2> map, T1 key, T2 def = default(T2))
        {
            T2 ret = default(T2);
            if (map != null && !map.TryGetValue(key, out ret))
            {
                return def;
            }
            return ret;
        }

        public static bool HasExtension(string srcPath, string ext)
        {
            var offset = srcPath.LastIndexOf(".");
            if (offset == -1)
            {
                return false;
            }
            var srcExt = srcPath.Substring(offset, srcPath.Length - offset);
            return srcExt.Equals(ext);
        }
        
        
        public static string GetPathWithoutExtension(string srcPath)
        {
            if (srcPath.LastIndexOf(".") != -1)
            {
                return srcPath.Substring(0, srcPath.LastIndexOf("."));
            }
            return srcPath;
        }

    }
}