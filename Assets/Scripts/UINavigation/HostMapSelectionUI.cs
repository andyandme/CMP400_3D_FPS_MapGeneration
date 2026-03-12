using TMPro;
using UnityEngine;

public class HostMapSelectionUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject hostModePanel;
    [SerializeField] private GameObject waitingForClientPanel;
    [SerializeField] private GameObject waitingForHostPanel;
    [SerializeField] private GameObject gameplayHUD;

    [Header("Refs")]
    [SerializeField] private LanBootstrap lanBootstrap;
    [SerializeField] private TMP_InputField seedInputField;

    private void Awake()
    {
        ShowOnly(mainMenuPanel);
    }

    public void OnHostPressed()
    {
        ShowOnly(hostModePanel);
        Debug.Log("[HostMapSelectionUI] Host pressed. Showing host mode panel.");
    }

    public void OnClientPressed()
    {
        ShowOnly(waitingForHostPanel);

        if (lanBootstrap == null)
        {
            Debug.LogError("[HostMapSelectionUI] LanBootstrap reference is missing.");
            return;
        }

        lanBootstrap.StartClient();
        Debug.Log("[HostMapSelectionUI] Client pressed. Waiting for host.");
    }

    public void OnParticipantTestingPressed()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogError("[HostMapSelectionUI] No HostSessionConfig instance found.");
            return;
        }

        HostSessionConfig.Instance.ConfigureParticipantTesting();
        BeginHostWaitingFlow();
    }

    public void OnRandomMapPressed()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogError("[HostMapSelectionUI] No HostSessionConfig instance found.");
            return;
        }

        HostSessionConfig.Instance.ConfigureRandomMap();
        BeginHostWaitingFlow();
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
        BeginHostWaitingFlow();
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
        if (hostModePanel != null) hostModePanel.SetActive(panelToShow == hostModePanel);
        if (waitingForClientPanel != null) waitingForClientPanel.SetActive(panelToShow == waitingForClientPanel);
        if (waitingForHostPanel != null) waitingForHostPanel.SetActive(panelToShow == waitingForHostPanel);
        if (gameplayHUD != null) gameplayHUD.SetActive(panelToShow == gameplayHUD);

        Debug.Log(
            $"[HostMapSelectionUI] ShowOnly target={(panelToShow != null ? panelToShow.name : "NULL")} | " +
            $"MainMenu={(mainMenuPanel != null && mainMenuPanel.activeSelf)} | " +
            $"HostMode={(hostModePanel != null && hostModePanel.activeSelf)} | " +
            $"WaitClient={(waitingForClientPanel != null && waitingForClientPanel.activeSelf)} | " +
            $"WaitHost={(waitingForHostPanel != null && waitingForHostPanel.activeSelf)} | " +
            $"HUD={(gameplayHUD != null && gameplayHUD.activeSelf)}"
        );
    }
}