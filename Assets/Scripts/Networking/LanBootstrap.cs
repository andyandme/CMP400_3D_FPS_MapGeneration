using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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

    private void Start()
    {
        HookNetworkCallbacks();
    }

    private void OnDestroy()
    {
        UnhookNetworkCallbacks();
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
            string fallback = null;

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet)
                    continue;

                IPInterfaceProperties props = nic.GetIPProperties();

                bool hasIpv4Gateway = false;
                foreach (GatewayIPAddressInformation gateway in props.GatewayAddresses)
                {
                    if (gateway?.Address != null &&
                        gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !gateway.Address.Equals(IPAddress.Any))
                    {
                        hasIpv4Gateway = true;
                        break;
                    }
                }

                foreach (UnicastIPAddressInformation uni in props.UnicastAddresses)
                {
                    IPAddress addr = uni.Address;

                    if (addr == null)
                        continue;

                    if (addr.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(addr))
                        continue;

                    string candidate = addr.ToString();

                    // Ignore APIPA addresses like 169.254.x.x
                    if (candidate.StartsWith("169.254."))
                        continue;

                    if (hasIpv4Gateway)
                        return candidate;

                    if (string.IsNullOrEmpty(fallback))
                        fallback = candidate;
                }
            }

            if (!string.IsNullOrEmpty(fallback))
                return fallback;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[LanBootstrap] Failed to get local IPv4 from network interfaces: {ex.Message}");
        }

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
            Debug.LogWarning($"[LanBootstrap] DNS fallback failed while getting local IPv4: {ex.Message}");
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

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LanBootstrap] NetworkManager.Singleton not found.");
            return;
        }

        string localIp = GetLocalIPv4();

        // Host's local client connects via loopback.
        // Server listens on all local interfaces so LAN clients can connect.
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");

        bool ok = NetworkManager.Singleton.StartHost();

        Debug.Log(
            $"[LanBootstrap] StartHost()={ok} " +
            $"hostClientConnectIP=127.0.0.1 listenIP=0.0.0.0 advertisedLocalIP={localIp} port={port}"
        );
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

        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LanBootstrap] NetworkManager.Singleton not found.");
            return;
        }

        transport.SetConnectionData(ip, port);

        bool ok = NetworkManager.Singleton.StartClient();

        Debug.Log($"[LanBootstrap] StartClient()={ok} targetIP={ip} port={port}");
    }

    private void HookNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;

        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure += HandleTransportFailure;
    }

    private void UnhookNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure -= HandleTransportFailure;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        Debug.Log(
            $"[LanBootstrap] OnClientConnected clientId={clientId} " +
            $"localClientId={NetworkManager.Singleton.LocalClientId} " +
            $"isHost={NetworkManager.Singleton.IsHost} isServer={NetworkManager.Singleton.IsServer} isClient={NetworkManager.Singleton.IsClient}"
        );
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        string reason = string.Empty;

        if (NetworkManager.Singleton != null)
            reason = NetworkManager.Singleton.DisconnectReason;

        Debug.LogWarning($"[LanBootstrap] OnClientDisconnected clientId={clientId} reason='{reason}'");
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("[LanBootstrap] Transport failure. Common causes: wrong host IP, firewall, or unreachable host.");
    }
}