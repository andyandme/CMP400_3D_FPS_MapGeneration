using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TopHealthBarsUI : MonoBehaviour
{
    [Header("Left Bar (Local Player)")]
    [SerializeField] private GameObject leftRoot;
    [SerializeField] private Image leftFill;
    [SerializeField] private TMP_Text leftNameText;
    [SerializeField] private TMP_Text leftValueText;

    [Header("Right Bar (Opponent)")]
    [SerializeField] private GameObject rightRoot;
    [SerializeField] private Image rightFill;
    [SerializeField] private TMP_Text rightNameText;
    [SerializeField] private TMP_Text rightValueText;

    [Header("Round Win Pips")]
    [SerializeField] private GameObject leftPipEmpty;
    [SerializeField] private GameObject leftPipFilled;
    [SerializeField] private GameObject rightPipEmpty;
    [SerializeField] private GameObject rightPipFilled;

    [Header("Options")]
    [SerializeField] private bool autoFindPlayers = true;
    [SerializeField] private string localLabel = "YOU";
    [SerializeField] private string opponentLabel = "ENEMY";


    [Header("Match Over UI")]
    [SerializeField] private GameObject matchOverPanel;
    [SerializeField] private TMP_Text matchOverTitleText;
    [SerializeField] private TMP_Text matchOverSubtitleText;
    [SerializeField] private GameObject hostPostMatchButtonsRoot;
    [SerializeField] private Button nextMapButton;
    [SerializeField] private Button rematchButton;
    [SerializeField] private Button backToMenuButton;


    private PlayerHealth localPlayerHealth;
    private PlayerHealth opponentPlayerHealth;

    private ulong lastResolvedLocalId = ulong.MaxValue;
    private ulong lastResolvedOpponentId = ulong.MaxValue;

    [SerializeField] private float hostPostMatchButtonDelay = 1.5f;

    private bool previousMatchOver;
    private float matchOverShownTime;


    private void OnNextMapClicked()
    {
        if (RoundManager.Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        RoundManager.Instance.StartNextMapServerRpc();
    }

    private void OnRematchClicked()
    {
        if (RoundManager.Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        RoundManager.Instance.RematchCurrentMapServerRpc();
    }

    private void OnBackToMenuClicked()
    {
        if (RoundManager.Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
            return;

        RoundManager.Instance.ReturnToHostMenuServerRpc();
    }

    private void Start()
    {

        if (nextMapButton != null)
            nextMapButton.onClick.AddListener(OnNextMapClicked);

        if (rematchButton != null)
            rematchButton.onClick.AddListener(OnRematchClicked);

        if (backToMenuButton != null)
            backToMenuButton.onClick.AddListener(OnBackToMenuClicked);

        ConfigureFillImage(leftFill, "LEFT");
        ConfigureFillImage(rightFill, "RIGHT");

        ForceRootsActive();
        ClearBarsIfMissing();
        TryResolvePlayers();
        RefreshAllUI("Start");
    }

    private void ConfigureFillImage(Image img, string tag)
    {
        if (img == null)
            return;

        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillClockwise = false;

        Debug.Log($"[TopHealthBarsUI] Configured {tag} as Filled Horizontal image.");
    }

    private void OnDestroy()
    {

        if (nextMapButton != null)
            nextMapButton.onClick.RemoveListener(OnNextMapClicked);

        if (rematchButton != null)
            rematchButton.onClick.RemoveListener(OnRematchClicked);

        if (backToMenuButton != null)
            backToMenuButton.onClick.RemoveListener(OnBackToMenuClicked);
    }

    private void Update()
    {
        ForceRootsActive();

        if (autoFindPlayers)
        {
            bool needsResolve =
                localPlayerHealth == null ||
                opponentPlayerHealth == null ||
                !localPlayerHealth.IsSpawned ||
                !opponentPlayerHealth.IsSpawned;

            if (needsResolve)
                TryResolvePlayers();
        }

        bool matchOverNow = RoundManager.Instance != null && RoundManager.Instance.MatchOver;

        if (matchOverNow && !previousMatchOver)
        {
            matchOverShownTime = Time.unscaledTime;
        }

        if (!matchOverNow)
        {
            matchOverShownTime = 0f;
        }

        previousMatchOver = matchOverNow;

        RefreshAllUI("Update");
    }

    private void ForceRootsActive()
    {
        if (leftRoot != null && !leftRoot.activeSelf)
        {
            Debug.LogWarning("[TopHealthBarsUI] leftRoot was inactive. Re-enabling it.");
            leftRoot.SetActive(true);
        }

        if (rightRoot != null && !rightRoot.activeSelf)
        {
            Debug.LogWarning("[TopHealthBarsUI] rightRoot was inactive. Re-enabling it.");
            rightRoot.SetActive(true);
        }
    }

    private void TryResolvePlayers()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[TopHealthBarsUI] TryResolvePlayers aborted: NetworkManager.Singleton is null.");
            return;
        }

        if (!NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[TopHealthBarsUI] TryResolvePlayers aborted: NetworkManager is not listening.");
            return;
        }

        if (NetworkManager.Singleton.LocalClient == null)
        {
            Debug.LogWarning("[TopHealthBarsUI] TryResolvePlayers aborted: LocalClient is null.");
            return;
        }

        if (NetworkManager.Singleton.LocalClient.PlayerObject == null)
        {
            Debug.LogWarning("[TopHealthBarsUI] TryResolvePlayers aborted: LocalClient.PlayerObject is null.");
            return;
        }

        localPlayerHealth = null;
        opponentPlayerHealth = null;
        lastResolvedLocalId = ulong.MaxValue;
        lastResolvedOpponentId = ulong.MaxValue;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient.PlayerObject;

        localPlayerHealth = localPlayerObject.GetComponent<PlayerHealth>();
        if (localPlayerHealth == null)
            localPlayerHealth = localPlayerObject.GetComponentInChildren<PlayerHealth>(true);

        if (localPlayerHealth != null)
            lastResolvedLocalId = localPlayerHealth.OwnerClientId;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            if (client.ClientId == localClientId)
                continue;

            PlayerHealth found = client.PlayerObject.GetComponent<PlayerHealth>();
            if (found == null)
                found = client.PlayerObject.GetComponentInChildren<PlayerHealth>(true);

            if (found != null)
            {
                opponentPlayerHealth = found;
                lastResolvedOpponentId = found.OwnerClientId;
                break;
            }
        }

        Debug.Log(
            $"[TopHealthBarsUI] TryResolvePlayers -> " +
            $"local={(localPlayerHealth != null ? localPlayerHealth.name : "NULL")} " +
            $"localOwner={(localPlayerHealth != null ? localPlayerHealth.OwnerClientId.ToString() : "NULL")} " +
            $"opponent={(opponentPlayerHealth != null ? opponentPlayerHealth.name : "NULL")} " +
            $"opponentOwner={(opponentPlayerHealth != null ? opponentPlayerHealth.OwnerClientId.ToString() : "NULL")}"
        );
    }

    private void RefreshAllUI(string sourceTag)
    {
        UpdateBar(leftFill, leftNameText, leftValueText, localPlayerHealth, localLabel, "LEFT", sourceTag);
        UpdateBar(rightFill, rightNameText, rightValueText, opponentPlayerHealth, opponentLabel, "RIGHT", sourceTag);
        UpdatePips();
        UpdatePostMatchPanel();
    }

    private void UpdateBar(
     Image fillImage,
     TMP_Text nameText,
     TMP_Text valueText,
     PlayerHealth health,
     string label,
     string sideTag,
     string sourceTag)
    {
        if (fillImage == null)
        {
            Debug.LogWarning($"[TopHealthBarsUI] {sideTag} fillImage is null.");
            return;
        }

        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillClockwise = false;

        if (health == null)
        {
            fillImage.fillAmount = 0f;

            if (nameText != null)
                nameText.text = label;

            if (valueText != null)
                valueText.text = "--";

            Debug.Log($"[TopHealthBarsUI] {sourceTag} {sideTag} bar has no PlayerHealth bound.");
            return;
        }

        float maxHealth = Mathf.Max(1f, health.maxHealth);
        float current = health.currentHealth.Value;
        float normalized = Mathf.Clamp01(current / maxHealth);

        fillImage.fillAmount = normalized;

        if (nameText != null)
            nameText.text = label;

        if (valueText != null)
            valueText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(maxHealth)}";

        Debug.Log(
            $"[TopHealthBarsUI] {sourceTag} {sideTag} -> " +
            $"player={health.name} owner={health.OwnerClientId} " +
            $"hp={current}/{maxHealth} fill={normalized:0.00} imageType={fillImage.type} " +
            $"fillAmount={fillImage.fillAmount}"
        );
    }

    private void UpdatePips()
    {
        if (RoundManager.Instance == null || localPlayerHealth == null || opponentPlayerHealth == null)
            return;

        bool localIsPlayer1 = localPlayerHealth.OwnerClientId < opponentPlayerHealth.OwnerClientId;

        int localWins = localIsPlayer1 ? RoundManager.Instance.Player1RoundWins : RoundManager.Instance.Player2RoundWins;
        int opponentWins = localIsPlayer1 ? RoundManager.Instance.Player2RoundWins : RoundManager.Instance.Player1RoundWins;

        bool localWonRound = localWins > 0;
        bool opponentWonRound = opponentWins > 0;

        if (leftPipEmpty != null)
            leftPipEmpty.SetActive(!localWonRound);

        if (leftPipFilled != null)
            leftPipFilled.SetActive(localWonRound);

        if (rightPipEmpty != null)
            rightPipEmpty.SetActive(!opponentWonRound);

        if (rightPipFilled != null)
            rightPipFilled.SetActive(opponentWonRound);
    }

    private void UpdatePostMatchPanel()
    {
        if (matchOverPanel == null)
            return;

        bool show = false;

        if (RoundManager.Instance != null &&
            RoundManager.Instance.MatchOver &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            show = true;
        }

        matchOverPanel.SetActive(show);

        if (!show)
        {
            if (hostPostMatchButtonsRoot != null)
                hostPostMatchButtonsRoot.SetActive(false);

            return;
        }

        bool localWon = RoundManager.Instance.WinningClientId == NetworkManager.Singleton.LocalClientId;

        if (matchOverTitleText != null)
            matchOverTitleText.text = localWon ? "YOU WIN" : "YOU LOSE";

        if (matchOverSubtitleText != null)
        {
            if (NetworkManager.Singleton.IsHost)
                matchOverSubtitleText.text = "Choose the next action.";
            else
                matchOverSubtitleText.text = "Waiting for host...";
        }

        if (hostPostMatchButtonsRoot != null)
        {
            bool hostCanClick =
                NetworkManager.Singleton.IsHost &&
                (Time.unscaledTime - matchOverShownTime) >= hostPostMatchButtonDelay;

            hostPostMatchButtonsRoot.SetActive(hostCanClick);
        }
    }



    //private void OnHostNextMatchClicked()
    //{
    //    if (RoundManager.Instance == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost)
    //        return;

    //    RoundManager.Instance.StartNextMatchServerRpc();
    //}

    private void ClearBarsIfMissing()
    {
        if (leftFill != null)
            leftFill.fillAmount = 0f;

        if (rightFill != null)
            rightFill.fillAmount = 0f;

        if (leftNameText != null)
            leftNameText.text = localLabel;

        if (rightNameText != null)
            rightNameText.text = opponentLabel;

        if (leftValueText != null)
            leftValueText.text = "--";

        if (rightValueText != null)
            rightValueText.text = "--";

        if (leftPipEmpty != null)
            leftPipEmpty.SetActive(true);

        if (leftPipFilled != null)
            leftPipFilled.SetActive(false);

        if (rightPipEmpty != null)
            rightPipEmpty.SetActive(true);

        if (rightPipFilled != null)
            rightPipFilled.SetActive(false);

    }
}