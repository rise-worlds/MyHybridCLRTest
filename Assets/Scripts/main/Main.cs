using System.Collections;
using Obfuz;
using Obfuz.EncryptionVM;
using UnityEngine;
using UnityEngine.UI;

namespace RiseClient
{
    public class Main : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void SetUpStaticSecretKey()
        {
            Debug.Log("SetUpStaticSecret begin");
            EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
            Debug.Log("SetUpStaticSecret end");
        }

        public Text text;
        private void Start()
        {
            text.text = TestHotUpdate.Run();
            LuaManager.Instance.Init(this.gameObject);
            LuaManager.Instance.StartLuaEnv();
        }

    }
}