using RiseClient;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace RiseClient
{
    public class LuaInstance : MonoBehaviour, ILuaManager
    {
        private LuaEnv luaEnv;
        // Start is called before the first frame update
        void Start()
        {
            luaEnv = new LuaEnv();
            luaEnv.AddLoader(LuaLoader);
        }

        // Update is called once per frame
        void Update()
        {
            if (luaEnv != null)
            {
                luaEnv.Tick();

                if (Time.frameCount % 100 == 0)
                {
                    luaEnv.FullGc();
                }
            }
        }

        private void OnDestroy()
        {
            luaEnv?.Dispose();
            luaEnv = null;
        }

        private byte[] LuaLoader(ref string filepath)
        {
            string path = filepath + ".lua.txt";
            return System.IO.File.ReadAllBytes(path);
        }

        public void StartLuaEnv()
        {
            TextAsset res = Resources.Load<TextAsset>("lua/main.lua.txt");
            if (res == null)
            {
                Debug.LogError("Load lua/main.lua.txt failed");
                return;
            }
            luaEnv.DoString(res.text);
        }
    }
}