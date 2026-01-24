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
            luaEnv.DoString(@"
                print('Hello from XLua!');
            ");

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
            luaEnv.Dispose();
            luaEnv = null;
        }
    }
}