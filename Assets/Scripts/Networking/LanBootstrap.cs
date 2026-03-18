using System.Net;
using System.Net.Sockets;
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

    public void SetIp(string newIp)
    {
        if (string.IsNullOrWhiteSpace(newIp))
        {
            Debug.LogWarning("[LanBootstrap] SetIp called with empty value.");
            return;
        }

        ip = newIp.Trim();
        Debug.Log($"[LanBootstrap] IP set to {ip}");
    }

    public void SetPort(ushort newPort)
    {
        port = newPort;
        Debug.Log($"[LanBootstrap] Port set to {port}");
    }

    public string GetLocalIPv4()
    {
        try
        {
            string hostName = Dns.GetHostName();
            IPAddress[] addresses = Dns.GetHostAddresses(hostName);

            for (int i = 0; i < addresses.Length; i++)
            {
                IPAddress addr = addresses[i];

                if (addr.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr))
                {
                    return addr.ToString();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LanBootstrap] Failed to get local IPv4: {ex.Message}");
        }

        return "127.0.0.1";
    }

    public void StartHost()
    {
        if (transport == null)
        {
            Debug.LogError("[LanBootstrap] UnityTransport not found.");
            return;
        }

        transport.SetConnectionData("0.0.0.0", port);

        bool ok = NetworkManager.Singleton.StartHost();
        Debug.Log($"[LanBootstrap] StartHost()={ok} bindIP=0.0.0.0 port={port} localIP={GetLocalIPv4()}");

    }

    public void EnableGameplayHUD()
    {
        if (gameplayHUD != null)
        {
            gameplayHUD.SetActive(true);
            Debug.Log("[LanBootstrap] GameplayHUD enabled.");
        }
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
        Debug.Log($"[LanBootstrap] StartClient()={ok} targetIP={ip} port={port}");

    }
}