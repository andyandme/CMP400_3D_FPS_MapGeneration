using Unity.Netcode;
using UnityEngine;

public class GameplayHUDGate : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject gameplayHUD;
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject connectToClientPanel;
    [SerializeField] private GameObject connectToHostPanel;
    [SerializeField] private GameObject hostModePanel;
    [SerializeField] private GameObject waitingForClientPanel;
    [SerializeField] private GameObject waitingForHostPanel;

    private bool gameplayShown;

    private void Awake()
    {
        if (gameplayHUD != null)
            gameplayHUD.SetActive(false);
    }

    private void Update()
    {
        if (gameplayShown)
            return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
            return;

        int connectedCount = nm.ConnectedClientsList != null ? nm.ConnectedClientsList.Count : 0;
        bool localPlayerReady = nm.LocalClient != null && nm.LocalClient.PlayerObject != null;

        if (connectedCount < 2)
            return;

        if (!localPlayerReady)
            return;

        if (!NetworkMapSync.IsGameplayReady())
            return;

        ShowGameplayOnly();
        gameplayShown = true;

        Debug.Log("[GameplayHUDGate] Gameplay HUD enabled after network + map sync readiness.");
    }

    public void ResetGate()
    {
        gameplayShown = false;

        if (gameplayHUD != null)
            gameplayHUD.SetActive(false);
    }

    private void ShowGameplayOnly()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (connectToClientPanel != null) connectToClientPanel.SetActive(false);
        if (connectToHostPanel != null) connectToHostPanel.SetActive(false);
        if (hostModePanel != null) hostModePanel.SetActive(false);
        if (waitingForClientPanel != null) waitingForClientPanel.SetActive(false);
        if (waitingForHostPanel != null) waitingForHostPanel.SetActive(false);
        if (gameplayHUD != null) gameplayHUD.SetActive(true);
    }
}