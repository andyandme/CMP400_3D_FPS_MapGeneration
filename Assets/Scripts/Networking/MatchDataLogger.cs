using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public class MatchDataLogger : MonoBehaviour
{
    [Header("Engagement Detection")]
    [SerializeField] private float lineOfSightCheckInterval = 0.1f;
    [SerializeField] private float eyeHeight = 2f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    private PlayerHealth player1;
    private PlayerHealth player2;

    private bool matchActive;
    private bool roundActive;
    private bool engagementRecordedThisRound;

    private float roundStartTime;
    private float nextLineOfSightCheckTime;
    private float firstEngagementTimeSecondsThisRound = -1f;

    private readonly List<float> roundLengths = new List<float>();
    private readonly List<float> firstEngagementTimes = new List<float>();

    private string filePath;

    private void Awake()
    {
#if UNITY_EDITOR
        string logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "MatchLogs");
#else
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MatchLogs");
#endif

        if (!Directory.Exists(logDirectory))
            Directory.CreateDirectory(logDirectory);

        filePath = Path.Combine(logDirectory, "fps_match_log.txt");

        Debug.Log($"[MatchDataLogger] Host log file path: {filePath}");
    }

    private void Update()
    {
        if (!IsServerHost())
            return;

        if (!matchActive || !roundActive)
            return;

        if (engagementRecordedThisRound)
            return;

        if (Time.time < nextLineOfSightCheckTime)
            return;

        nextLineOfSightCheckTime = Time.time + lineOfSightCheckInterval;

        if (player1 == null || player2 == null)
            return;

        bool p1SeesP2 = HasLineOfSight(player1.transform, player2.transform);
        bool p2SeesP1 = HasLineOfSight(player2.transform, player1.transform);

        if (!p1SeesP2 && !p2SeesP1)
            return;

        engagementRecordedThisRound = true;
        firstEngagementTimeSecondsThisRound = Time.time - roundStartTime;

        Debug.Log(
            $"[MatchDataLogger] First engagement recorded for round at {firstEngagementTimeSecondsThisRound:0.000}s " +
            $"(p1SeesP2={p1SeesP2}, p2SeesP1={p2SeesP1})"
        );
    }

    public void BeginMatch(PlayerHealth matchPlayer1, PlayerHealth matchPlayer2)
    {
        if (!IsServerHost())
            return;

        player1 = matchPlayer1;
        player2 = matchPlayer2;

        roundLengths.Clear();
        firstEngagementTimes.Clear();

        matchActive = true;

        Debug.Log(
            $"[MatchDataLogger] BeginMatch " +
            $"player1={(player1 != null ? player1.name : "NULL")} owner={(player1 != null ? player1.OwnerClientId.ToString() : "NULL")} " +
            $"player2={(player2 != null ? player2.name : "NULL")} owner={(player2 != null ? player2.OwnerClientId.ToString() : "NULL")}"
        );

        BeginRound();
    }

    public void BeginRound()
    {
        if (!IsServerHost())
            return;

        if (!matchActive)
            return;

        roundActive = true;
        engagementRecordedThisRound = false;
        firstEngagementTimeSecondsThisRound = -1f;
        roundStartTime = Time.time;
        nextLineOfSightCheckTime = Time.time;

        Debug.Log("[MatchDataLogger] BeginRound");
    }

    public void CompleteRound(PlayerHealth winner, PlayerHealth loser)
    {
        if (!IsServerHost())
            return;

        if (!matchActive || !roundActive)
        {
            Debug.LogWarning("[MatchDataLogger] CompleteRound called while no active round was being tracked.");
            return;
        }

        float roundLengthSeconds = Time.time - roundStartTime;
        roundLengths.Add(roundLengthSeconds);
        firstEngagementTimes.Add(firstEngagementTimeSecondsThisRound);

        roundActive = false;

        Debug.Log(
            $"[MatchDataLogger] CompleteRound length={roundLengthSeconds:0.000}s " +
            $"firstEngagement={(engagementRecordedThisRound ? firstEngagementTimeSecondsThisRound.ToString("0.000") : "NOT_RECORDED")} " +
            $"winner={(winner != null ? winner.OwnerClientId.ToString() : "Unknown")} " +
            $"loser={(loser != null ? loser.OwnerClientId.ToString() : "Unknown")}"
        );
    }

    public void CompleteMatch(PlayerHealth winner, PlayerHealth loser)
    {
        if (!IsServerHost())
            return;

        if (!matchActive)
        {
            Debug.LogWarning("[MatchDataLogger] CompleteMatch called while no active match was being tracked.");
            return;
        }

        SessionMapType? mapType = null;
        int mapSeed = 0;

        if (HostSessionConfig.Instance != null && HostSessionConfig.Instance.HasActiveConfig)
        {
            mapType = HostSessionConfig.Instance.CurrentMap.mapType;
            mapSeed = HostSessionConfig.Instance.CurrentMap.seed;
        }

        ulong serverClientId = NetworkManager.ServerClientId;

        string winnerSide = "Unknown";
        string loserSide = "Unknown";

        if (winner != null)
            winnerSide = (winner.OwnerClientId == serverClientId) ? "Host (Player 1)" : "Client (Player 2)";

        if (loser != null)
            loserSide = (loser.OwnerClientId == serverClientId) ? "Host (Player 1)" : "Client (Player 2)";

        float averageRoundLength = 0f;
        if (roundLengths.Count > 0)
        {
            float total = 0f;
            for (int i = 0; i < roundLengths.Count; i++)
                total += roundLengths[i];

            averageRoundLength = total / roundLengths.Count;
        }

        float averageFirstEngagement = 0f;
        int validEngagementCount = 0;

        for (int i = 0; i < firstEngagementTimes.Count; i++)
        {
            if (firstEngagementTimes[i] >= 0f)
            {
                averageFirstEngagement += firstEngagementTimes[i];
                validEngagementCount++;
            }
        }

        if (validEngagementCount > 0)
            averageFirstEngagement /= validEngagementCount;
        else
            averageFirstEngagement = -1f;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("----- MATCH RESULT -----");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"MapType: {(mapType.HasValue ? mapType.Value.ToString() : "Unknown")}");
        sb.AppendLine($"MapSeed: {mapSeed}");

        for (int i = 0; i < roundLengths.Count; i++)
        {
            sb.AppendLine($"Round{i + 1}LengthSeconds: {roundLengths[i]:0.000}");

            if (i < firstEngagementTimes.Count && firstEngagementTimes[i] >= 0f)
                sb.AppendLine($"Round{i + 1}FirstEngagementSeconds: {firstEngagementTimes[i]:0.000}");
            else
                sb.AppendLine($"Round{i + 1}FirstEngagementSeconds: NOT_RECORDED");
        }

        sb.AppendLine($"AverageRoundLengthSeconds: {averageRoundLength:0.000}");
        sb.AppendLine($"AverageFirstEngagementSeconds: {(averageFirstEngagement >= 0f ? averageFirstEngagement.ToString("0.000") : "NOT_RECORDED")}");
        sb.AppendLine($"WinnerSide: {winnerSide}");
        sb.AppendLine($"WinnerName: {(winner != null ? winner.name : "Unknown")}");
        sb.AppendLine($"WinnerOwnerClientId: {(winner != null ? winner.OwnerClientId.ToString() : "Unknown")}");
        sb.AppendLine($"LoserSide: {loserSide}");
        sb.AppendLine($"LoserName: {(loser != null ? loser.name : "Unknown")}");
        sb.AppendLine($"LoserOwnerClientId: {(loser != null ? loser.OwnerClientId.ToString() : "Unknown")}");
        sb.AppendLine();

        File.AppendAllText(filePath, sb.ToString());

        Debug.Log($"[MatchDataLogger] Match result appended to: {filePath}");

        matchActive = false;
        roundActive = false;
    }

    private bool IsServerHost()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    private bool HasLineOfSight(Transform fromRoot, Transform toRoot)
    {
        Vector3 origin = GetVisionPoint(fromRoot);
        Vector3 target = GetVisionPoint(toRoot);

        Vector3 delta = target - origin;
        float distance = delta.magnitude;

        if (distance <= 0.001f)
            return true;

        if (Physics.Raycast(
                origin,
                delta.normalized,
                out RaycastHit hit,
                distance,
                lineOfSightMask,
                QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == toRoot || hit.transform.IsChildOf(toRoot))
                return true;

            return false;
        }

        return true;
    }

    private Vector3 GetVisionPoint(Transform root)
    {
        CapsuleCollider capsule = root.GetComponentInChildren<CapsuleCollider>(true);
        if (capsule != null)
            return capsule.bounds.center + Vector3.up * (capsule.bounds.extents.y * 0.35f);

        return root.position + Vector3.up * eyeHeight;
    }
}