using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using XLua;

namespace RiseClient
{
    [LuaCallCSharp]
    public class TestHotUpdate
    {
        public static string Run()
        {
            string message = "Hello, My Test world!";
            Debug.Log(message);
            return message;
        }
    }
}