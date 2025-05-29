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
            text.text = TestHotUpdate.Run();
        }
   }
}