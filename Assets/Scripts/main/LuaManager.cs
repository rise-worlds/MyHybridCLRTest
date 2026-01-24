using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XLua;

namespace RiseClient
{
    public interface ILuaManager
    {
        void StartLuaEnv();
    }

    [LuaCallCSharp]
    public class LuaManager : MonoBehaviour
    {
        private static LuaManager _instance;
        public static LuaManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject obj = new GameObject("LuaManager");
                    _instance = obj.AddComponent<LuaManager>();
                    DontDestroyOnLoad(obj);
                }
                return _instance;
            }
        }

        private ILuaManager _luaMangage;
        [BlackList]
        public void Init(GameObject luaManager)
        {
            if (_luaMangage == null)
            {
                _luaMangage = luaManager?.GetComponent<ILuaManager>();
            }
        }


        [BlackList]
        public void LuaEnvStartAsync()
        {
            _luaMangage.StartLuaEnv();
        }

        [BlackList]
        public void Destroy()
        {
            if (_luaMangage != null)
            {
                Destroy(_luaMangage as MonoBehaviour);
                _luaMangage = null;
            }
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }
    }
}