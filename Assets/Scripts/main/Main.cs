using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RiseClient
{
    public class Main : MonoBehaviour
    {
        public Text text;
        private void Start()
        {
            Run();
            text.text = "Hello, my worlds!";
        }

        public static void Run()
        {
            Debug.Log("Hello, my worlds!");
        }
    }
}