using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class LanBootstrap : MonoBehaviour
{
    public UnityTransport transport;
    public string ip = "127.0.0.1";
    public ushort port = 7777;
    [SerializeField] private GameObject gameplayHUD;

    [Header("UI")]
    public GameObject uiRoot; 

    [Header("Map Generator")]
    public FpsMapGenerator generator; 

    private void Awake()
    {
        if (transport == null)
            transport = FindFirstObjectByType<UnityTransport>();

        if (generator == null)
            generator = FindFirstObjectByType<FpsMapGenerator>();
    }

    public void StartHost()
    {
        if (transport == null)
        {
            Debug.LogError("[LanBootstrap] UnityTransport not found.");
            return;
        }

        transport.SetConnectionData(ip, port);

        bool ok = NetworkManager.Singleton.StartHost();
        Debug.Log($"[LanBootstrap] StartHost()={ok}");

        if (uiRoot != null) uiRoot.SetActive(false);

    }


    public void StartClient()
    {
        if (transport == null)
        {
            Debug.LogError("[LanBootstrap] UnityTransport not found.");
            return;
        }

        transport.SetConnectionData(ip, port);

        bool ok = NetworkManager.Singleton.StartClient();
        Debug.Log($"[LanBootstrap] StartClient()={ok}");

        if (uiRoot != null) uiRoot.SetActive(false);
    }
}