using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RiseClient
{
    public interface ILuaManager
    {
        // Define interface methods and properties here
    }

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
        public void Init(GameObject luaManager)
        {
            if (_luaMangage == null)
            {
                _luaMangage = luaManager?.GetComponent<ILuaManager>();
            }
        }

        private void Awake()
        {
            // Initialization code here
        }
    }
}