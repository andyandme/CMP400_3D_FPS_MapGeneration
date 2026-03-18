using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class HostMapSelectionUI : MonoBehaviour
{
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject connectToClientPanel;
    [SerializeField] private GameObject connectToHostPanel;
    [SerializeField] private GameObject hostModePanel;
    [SerializeField] private GameObject waitingForClientPanel;
    [SerializeField] private GameObject waitingForHostPanel;
    [SerializeField] private GameObject gameplayHUD;

    [SerializeField] private LanBootstrap lanBootstrap;
    [SerializeField] private TMP_InputField seedInputField;

    [SerializeField] private TMP_InputField hostPortInputField;
    [SerializeField] private TMP_Text hostConnectionInfoText;

    [SerializeField] private TMP_InputField clientHostIpInputField;
    [SerializeField] private TMP_InputField clientPortInputField;


    private bool hostStartedFromConnectPanel;

    //[Header("Panels")]
    //[SerializeField] private GameObject mainMenuPanel;
    //[SerializeField] private GameObject hostModePanel;
    //[SerializeField] private GameObject waitingForClientPanel;
    //[SerializeField] private GameObject waitingForHostPanel;
    //[SerializeField] private GameObject gameplayHUD;

    //[Header("Refs")]
    //[SerializeField] private LanBootstrap lanBootstrap;
    //[SerializeField] private TMP_InputField seedInputField;

    public static HostMapSelectionUI Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        ShowOnly(mainMenuPanel);

        if (hostPortInputField != null && string.IsNullOrWhiteSpace(hostPortInputField.text))
            hostPortInputField.text = "7777";

        if (clientPortInputField != null && string.IsNullOrWhiteSpace(clientPortInputField.text))
            clientPortInputField.text = "7777";

        RefreshHostConnectionInfo();
    }


    private void Start()
    {
        StartCoroutine(WaitForNetworkManagerAndHookCallbacks());
    }

    private System.Collections.IEnumerator WaitForNetworkManagerAndHookCallbacks()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;

        if (Instance == this)
            Instance = null;
    }

    public void ShowHostModeAfterMatchReturn()
    {
        ShowOnly(hostModePanel);
    }

    public void ShowWaitingForHostAfterMatchReturn()
    {
        ShowOnly(waitingForHostPanel);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsHost)
            return;

        if (!hostStartedFromConnectPanel)
            return;

        int connectedCount = NetworkManager.Singleton.ConnectedClientsList != null
            ? NetworkManager.Singleton.ConnectedClientsList.Count
            : 0;

        Debug.Log($"[HostMapSelectionUI] OnClientConnected clientId={clientId} connectedCount={connectedCount}");

        if (connectedCount >= 2)
        {
            ShowOnly(hostModePanel);
            Debug.Log("[HostMapSelectionUI] Client connected to host. Showing Host Mode panel.");
        }
    }


    public void OnHostPressed()
    {
        ShowOnly(connectToClientPanel);
        RefreshHostConnectionInfo();
        Debug.Log("[HostMapSelectionUI] Host pressed. Showing ConnectToClient panel.");
    }

    public void OnClientPressed()
    {
        ShowOnly(connectToHostPanel);
        Debug.Log("[HostMapSelectionUI] Client pressed. Showing ConnectToHost panel.");
    }

    public void OnConnectToClientContinuePressed()
    {
        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return;
        }

        if (!TryApplyHostPortFromInput())
        {
            Debug.LogWarning("[HostMapSelectionUI] Host continue cancelled because port input was invalid.");
            return;
        }

        RefreshHostConnectionInfo();

        hostStartedFromConnectPanel = true;
        lanBootstrap.StartHost();

        ShowOnly(waitingForClientPanel);

        Debug.Log("[HostMapSelectionUI] Host started. Waiting for client.");
    }

    public void OnConnectToHostContinuePressed()
    {
        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return;
        }

        if (!TryApplyClientHostIpFromInput())
        {
            Debug.LogWarning("[HostMapSelectionUI] Client continue cancelled because host IP input was invalid.");
            return;
        }

        if (!TryApplyClientPortFromInput())
        {
            Debug.LogWarning("[HostMapSelectionUI] Client continue cancelled because port input was invalid.");
            return;
        }

        lanBootstrap.StartClient();

        ShowOnly(waitingForHostPanel);

        Debug.Log("[HostMapSelectionUI] Client started. Waiting for host.");
    }


    private bool TryApplyHostPortFromInput()
    {
        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return false;
        }

        if (hostPortInputField == null)
        {
            Debug.LogError("[HostMapSelectionUI] Host port input field is not assigned.");
            return false;
        }

        string raw = hostPortInputField.text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogWarning("[HostMapSelectionUI] Host port input is empty.");
            return false;
        }

        if (!ushort.TryParse(raw, out ushort parsedPort))
        {
            Debug.LogWarning($"[HostMapSelectionUI] Invalid host port entered: '{raw}'");
            return false;
        }

        if (parsedPort == 0)
        {
            Debug.LogWarning("[HostMapSelectionUI] Port 0 is not valid for manual entry.");
            return false;
        }

        lanBootstrap.SetPort(parsedPort);
        return true;
    }

    private bool TryApplyClientPortFromInput()
    {
        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return false;
        }

        if (clientPortInputField == null)
        {
            Debug.LogError("[HostMapSelectionUI] Client port input field is not assigned.");
            return false;
        }

        string raw = clientPortInputField.text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogWarning("[HostMapSelectionUI] Client port input is empty.");
            return false;
        }

        if (!ushort.TryParse(raw, out ushort parsedPort))
        {
            Debug.LogWarning($"[HostMapSelectionUI] Invalid client port entered: '{raw}'");
            return false;
        }

        if (parsedPort == 0)
        {
            Debug.LogWarning("[HostMapSelectionUI] Port 0 is not valid for manual entry.");
            return false;
        }

        lanBootstrap.SetPort(parsedPort);
        return true;
    }


    private bool TryApplyClientHostIpFromInput()
    {
        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return false;
        }

        if (clientHostIpInputField == null)
        {
            Debug.LogError("[HostMapSelectionUI] Client host IP input field is not assigned.");
            return false;
        }

        string raw = clientHostIpInputField.text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            Debug.LogWarning("[HostMapSelectionUI] Client host IP input is empty.");
            return false;
        }

        lanBootstrap.SetIp(raw);
        return true;
    }

    private void RefreshHostConnectionInfo()
    {
        if (hostConnectionInfoText == null || lanBootstrap == null)
            return;

        string localIp = lanBootstrap.GetLocalIPv4();

        string portText = "7777";
        if (hostPortInputField != null && !string.IsNullOrWhiteSpace(hostPortInputField.text))
            portText = hostPortInputField.text.Trim();

        hostConnectionInfoText.text = $"Your IP: {localIp}\nPort: {portText}";
    }

    public void OnParticipantTestingPressed()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogError("[HostMapSelectionUI] No HostSessionConfig instance found.");
            return;
        }

        HostSessionConfig.Instance.ConfigureParticipantTesting();

        NetworkMapSync sync = FindFirstObjectByType<NetworkMapSync>();
        if (sync == null)
        {
            Debug.LogError("[HostMapSelectionUI] No NetworkMapSync instance found.");
            return;
        }

        sync.PublishCurrentHostConfig();

        ShowOnly(waitingForClientPanel);

        Debug.Log("[HostMapSelectionUI] Participant Testing selected and published.");
    }
    public void OnRandomMapPressed()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogError("[HostMapSelectionUI] No HostSessionConfig instance found.");
            return;
        }

        HostSessionConfig.Instance.ConfigureRandomMap();

        NetworkMapSync sync = FindFirstObjectByType<NetworkMapSync>();
        if (sync == null)
        {
            Debug.LogError("[HostMapSelectionUI] No NetworkMapSync instance found.");
            return;
        }

        sync.PublishCurrentHostConfig();

        ShowOnly(waitingForClientPanel);

        Debug.Log("[HostMapSelectionUI] Random Map selected and published.");
    }

    public void OnSeedSelectionPressed()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogError("[HostMapSelectionUI] No HostSessionConfig instance found.");
            return;
        }

        if (seedInputField == null)
        {
            Debug.LogError("[HostMapSelectionUI] Seed input field is not assigned.");
            return;
        }

        if (!int.TryParse(seedInputField.text, out int parsedSeed))
        {
            Debug.LogWarning("[HostMapSelectionUI] Invalid seed entered.");
            return;
        }

        HostSessionConfig.Instance.ConfigureSeedSelection(parsedSeed);

        NetworkMapSync sync = FindFirstObjectByType<NetworkMapSync>();
        if (sync == null)
        {
            Debug.LogError("[HostMapSelectionUI] No NetworkMapSync instance found.");
            return;
        }

        sync.PublishCurrentHostConfig();

        ShowOnly(waitingForClientPanel);

        Debug.Log($"[HostMapSelectionUI] Seed Selection configured and published. Seed={parsedSeed}");
    }



    public void OnBackPressed()
    {
        ShowOnly(mainMenuPanel);
    }

    private void BeginHostWaitingFlow()
    {
        ShowOnly(waitingForClientPanel);

        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return;
        }

        lanBootstrap.StartHost();
        Debug.Log("[HostMapSelectionUI] Host configured mode. Waiting for client.");
    }

    private void ShowOnly(GameObject panelToShow)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(panelToShow == mainMenuPanel);
        if (connectToClientPanel != null) connectToClientPanel.SetActive(panelToShow == connectToClientPanel);
        if (connectToHostPanel != null) connectToHostPanel.SetActive(panelToShow == connectToHostPanel);
        if (hostModePanel != null) hostModePanel.SetActive(panelToShow == hostModePanel);
        if (waitingForClientPanel != null) waitingForClientPanel.SetActive(panelToShow == waitingForClientPanel);
        if (waitingForHostPanel != null) waitingForHostPanel.SetActive(panelToShow == waitingForHostPanel);
        if (gameplayHUD != null) gameplayHUD.SetActive(panelToShow == gameplayHUD);

        RefreshHostConnectionInfo();

        Debug.Log(
            $"[HostMapSelectionUI] ShowOnly target={(panelToShow != null ? panelToShow.name : "NULL")} | " +
            $"MainMenu={(mainMenuPanel != null && mainMenuPanel.activeSelf)} | " +
            $"ConnectToClient={(connectToClientPanel != null && connectToClientPanel.activeSelf)} | " +
            $"ConnectToHost={(connectToHostPanel != null && connectToHostPanel.activeSelf)} | " +
            $"HostMode={(hostModePanel != null && hostModePanel.activeSelf)} | " +
            $"WaitClient={(waitingForClientPanel != null && waitingForClientPanel.activeSelf)} | " +
            $"WaitHost={(waitingForHostPanel != null && waitingForHostPanel.activeSelf)} | " +
            $"HUD={(gameplayHUD != null && gameplayHUD.activeSelf)}"
        );
    }
}