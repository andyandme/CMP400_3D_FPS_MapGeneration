using Unity.Netcode;
using UnityEngine;

public class NetShutdownOnQuit : MonoBehaviour
{
    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
    }

#if UNITY_EDITOR
    private void OnDisable()
    {
  
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }
#endif
}