using Unity.Netcode;
using UnityEngine;

public class GameplayHUDGate : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject gameplayHUD;
    [SerializeField] private GameObject mainMenuPanel;
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
        if (nm == null)
            return;

        if (!nm.IsListening)
            return;

        int connectedCount = nm.ConnectedClientsList != null ? nm.ConnectedClientsList.Count : 0;
        bool localPlayerReady = nm.LocalClient != null && nm.LocalClient.PlayerObject != null;

        Debug.Log($"[GameplayHUDGate] IsListening={nm.IsListening} ConnectedCount={connectedCount} LocalPlayerReady={localPlayerReady}");

        if (connectedCount < 2)
            return;

        if (!localPlayerReady)
            return;

        ShowGameplayOnly();
        gameplayShown = true;

        Debug.Log("[GameplayHUDGate] Both players connected and local player exists. Gameplay HUD enabled.");
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
        if (hostModePanel != null) hostModePanel.SetActive(false);
        if (waitingForClientPanel != null) waitingForClientPanel.SetActive(false);
        if (waitingForHostPanel != null) waitingForHostPanel.SetActive(false);
        if (gameplayHUD != null) gameplayHUD.SetActive(true);
    }
}