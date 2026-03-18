using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance;

    [Header("Generated Spawn Names")]
    [SerializeField] private string spawnAName = "SpawnA";
    [SerializeField] private string spawnBName = "SpawnB";

    [Header("Round Settings")]
    [SerializeField] private float nextRoundDelay = 3f;
    [SerializeField] private int winsNeededToWinMatch = 2;

    private Transform spawnA;
    private Transform spawnB;

    private PlayerHealth player1;
    private PlayerHealth player2;

    private bool nextRoundRoutineRunning;

    private NetworkVariable<int> player1RoundWins = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<int> player2RoundWins = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> roundOver = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> matchOver = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> spawnsSwapped = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int Player1RoundWins => player1RoundWins.Value;
    public int Player2RoundWins => player2RoundWins.Value;
    public bool RoundOver => roundOver.Value;
    public bool MatchOver => matchOver.Value;
    public bool SpawnsSwapped => spawnsSwapped.Value;

    private NetworkVariable<ulong> winningClientId = new NetworkVariable<ulong>(
    ulong.MaxValue,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);

    public ulong WinningClientId => winningClientId.Value;

    private void PublishAndStartCurrentConfiguredMatch()
    {
        NetworkMapSync sync = FindFirstObjectByType<NetworkMapSync>();
        if (sync == null)
        {
            Debug.LogError("[RoundManager] No NetworkMapSync found.");
            return;
        }

        ResetMatchStateOnly();

        sync.SetGameplayReadyForAll(false);
        sync.PublishCurrentHostConfig();

        Debug.Log("[RoundManager] Published configured match and waiting for all clients to finish map apply.");
    }

    public void OnMapSyncReadyForMatchStartServer()
    {
        if (!IsServer)
            return;

        ResetBestOf3StateForNewMatchServer();

        if (!ResolvePlayers())
        {
            Debug.LogWarning("[RoundManager] Could not resolve players after map sync.");
            return;
        }

        if (!ResolveSpawns())
        {
            Debug.LogWarning("[RoundManager] Could not resolve spawns after map sync.");
            return;
        }

        ResetPlayersForFreshMatch();

        Debug.Log("[RoundManager] Map sync complete for all players. Fresh BO3 match started.");
    }



    private void ResetPlayersForFreshMatch()
    {
        if (!ResolvePlayers())
        {
            Debug.LogWarning("[RoundManager] Could not resolve players for fresh match.");
            return;
        }

        if (!ResolveSpawns())
        {
            Debug.LogWarning("[RoundManager] Could not resolve spawns for fresh match.");
            return;
        }

        ResetPlayerForRound(player1, spawnA);
        ResetPlayerForRound(player2, spawnB);
    }

    private bool ConfigureNextMapFromFlowMode()
    {
        if (HostSessionConfig.Instance == null)
        {
            Debug.LogWarning("[RoundManager] No HostSessionConfig instance found.");
            return false;
        }

        switch (HostSessionConfig.Instance.CurrentFlowMode)
        {
            case SessionFlowMode.ParticipantTesting:
                return HostSessionConfig.Instance.MoveToNextParticipantTestingMap();

            case SessionFlowMode.RandomMap:
                HostSessionConfig.Instance.ConfigureRandomMap();
                return true;

            case SessionFlowMode.SeedSelection:
                // Keep exact same selected seed/map for "next map" in seed-selection mode.
                return HostSessionConfig.Instance.HasActiveConfig;

            default:
                return false;
        }
    }



    [ClientRpc]
    private void SetGameplayReadyClientRpc(bool ready)
    {
        if (NetworkMapSync.Instance != null)
            NetworkMapSync.Instance.SetLocalGameplayReady(ready);
    }

    [ClientRpc]
    private void ReturnPlayersToMenuStateClientRpc()
    {
        GameplayHUDGate gate = FindFirstObjectByType<GameplayHUDGate>();
        if (gate != null)
            gate.ResetGate();

        if (NetworkMapSync.Instance != null)
            NetworkMapSync.Instance.SetLocalGameplayReady(false);

        if (HostMapSelectionUI.Instance == null)
            return;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
            HostMapSelectionUI.Instance.ShowHostModeAfterMatchReturn();
        else
            HostMapSelectionUI.Instance.ShowWaitingForHostAfterMatchReturn();
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartNextMapServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[RoundManager] Non-host client {sender} tried to start next map.");
            return;
        }

        if (!ConfigureNextMapFromFlowMode())
        {
            Debug.LogWarning("[RoundManager] Could not configure next map from current flow mode.");
            return;
        }

        PublishAndStartCurrentConfiguredMatch();
    }

    [ServerRpc(RequireOwnership = false)]
    public void RematchCurrentMapServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[RoundManager] Non-host client {sender} tried to request rematch.");
            return;
        }

        PublishAndStartCurrentConfiguredMatch();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReturnToHostMenuServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[RoundManager] Non-host client {sender} tried to return to menu.");
            return;
        }

        ResetBestOf3StateForNewMatchServer();

        NetworkMapSync sync = FindFirstObjectByType<NetworkMapSync>();
        if (sync != null)
            sync.SetGameplayReadyForAll(false);

        ReturnPlayersToMenuStateClientRpc();

        Debug.Log("[RoundManager] Returned both players to host/client menu state with BO3 reset.");
    }

    private void ResetMatchStateOnly()
    {
        ResetBestOf3StateForNewMatchServer();
    }


    public void ResetBestOf3StateForNewMatchServer()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[RoundManager] ResetBestOf3StateForNewMatchServer called on non-server.");
            return;
        }

        player1RoundWins.Value = 0;
        player2RoundWins.Value = 0;
        roundOver.Value = false;
        matchOver.Value = false;
        spawnsSwapped.Value = false;
        nextRoundRoutineRunning = false;
        winningClientId.Value = ulong.MaxValue;

        Debug.Log("[RoundManager] Best-of-3 state reset for a fresh match.");
    }

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Debug.Log($"[RoundManager] OnNetworkSpawn IsServer={IsServer} IsHost={IsHost}");

        if (IsServer)
        {
            StartCoroutine(WaitForPlayersAndSpawns());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
            Instance = null;
    }

    private IEnumerator WaitForPlayersAndSpawns()
    {
        while (IsServer && !ResolvePlayers())
            yield return null;

        Debug.Log($"[RoundManager] Players resolved. P1={player1.name} ({player1.OwnerClientId}) P2={player2.name} ({player2.OwnerClientId})");

        while (IsServer && !ResolveSpawns())
            yield return null;

        Debug.Log($"[RoundManager] Spawns resolved. A={spawnA.name} B={spawnB.name}");
    }

    private bool ResolvePlayers()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return false;

        if (NetworkManager.Singleton.ConnectedClientsList == null || NetworkManager.Singleton.ConnectedClientsList.Count < 2)
            return false;

        PlayerHealth first = null;
        PlayerHealth second = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
                continue;

            PlayerHealth ph = client.PlayerObject.GetComponent<PlayerHealth>();
            if (ph == null)
                ph = client.PlayerObject.GetComponentInChildren<PlayerHealth>(true);

            if (ph == null || !ph.IsSpawned)
                continue;

            if (first == null)
                first = ph;
            else if (second == null)
                second = ph;
        }

        if (first == null || second == null)
        {
            Debug.LogWarning("[RoundManager] ResolvePlayers failed. Could not find 2 spawned PlayerHealth components from connected clients.");
            return false;
        }

        if (first.OwnerClientId < second.OwnerClientId)
        {
            player1 = first;
            player2 = second;
        }
        else
        {
            player1 = second;
            player2 = first;
        }

        Debug.Log(
            $"[RoundManager] ResolvePlayers success. " +
            $"player1={player1.name} owner={player1.OwnerClientId} " +
            $"player2={player2.name} owner={player2.OwnerClientId}"
        );

        return true;
    }


    private bool ResolveSpawns()
    {
        if (spawnA != null && spawnB != null)
            return true;

        FpsMapGenerator gen = FindFirstObjectByType<FpsMapGenerator>();
        if (gen == null)
            return false;

        if (spawnA == null)
            spawnA = gen.GeneratedSpawnA;

        if (spawnB == null)
            spawnB = gen.GeneratedSpawnB;

        return spawnA != null && spawnB != null;
    }

    private void ResolvePlayersIfNeeded()
    {
        bool success = ResolvePlayers();

        Debug.Log(
            $"[RoundManager] ResolvePlayersIfNeeded -> success={success} " +
            $"player1={(player1 != null ? player1.name : "NULL")} " +
            $"player2={(player2 != null ? player2.name : "NULL")}"
        );
    }

    private void Update()
    {
        if (!IsServer)
            return;

        if (matchOver.Value || roundOver.Value)
            return;

        if (player1 == null || player2 == null)
        {
            ResolvePlayersIfNeeded();
            return;
        }

        if (spawnA == null || spawnB == null)
        {
            if (!ResolveSpawns())
            {
                Debug.LogWarning("[RoundManager] Update: spawnA/spawnB not ready yet.");
                return;
            }
        }

        Debug.Log(
            $"[RoundManager] Update " +
            $"P1={player1.name} dead={player1.isDead.Value} hp={player1.currentHealth.Value} " +
            $"P2={player2.name} dead={player2.isDead.Value} hp={player2.currentHealth.Value} " +
            $"roundOver={roundOver.Value} matchOver={matchOver.Value}"
        );

        if (player1.isDead.Value)
        {
            Debug.Log("[RoundManager] Detected player1 dead.");
            EndRound(player2, player1);
        }
        else if (player2.isDead.Value)
        {
            Debug.Log("[RoundManager] Detected player2 dead.");
            EndRound(player1, player2);
        }
    }

    private void EndRound(PlayerHealth winner, PlayerHealth loser)
    {
        if (winner == null || loser == null)
        {
            Debug.LogWarning("[RoundManager] EndRound called with null winner or loser.");
            return;
        }

        roundOver.Value = true;

        if (winner == player1)
            player1RoundWins.Value++;
        else if (winner == player2)
            player2RoundWins.Value++;

        Debug.Log($"[RoundManager] Round over. Winner={winner.name} Loser={loser.name} Score P1={player1RoundWins.Value} P2={player2RoundWins.Value}");

        if (player1RoundWins.Value >= winsNeededToWinMatch || player2RoundWins.Value >= winsNeededToWinMatch)
        {
            matchOver.Value = true;
            winningClientId.Value = winner.OwnerClientId;
            Debug.Log($"[RoundManager] Match over. WinningClientId={winningClientId.Value}");
            return;
        }

        if (!nextRoundRoutineRunning)
            StartCoroutine(StartNextRoundAfterDelay());
    }


    private IEnumerator StartNextRoundAfterDelay()
    {
        nextRoundRoutineRunning = true;

        Debug.Log($"[RoundManager] Waiting {nextRoundDelay:0.0} seconds before next round.");

        yield return new WaitForSeconds(nextRoundDelay);

        if (!IsServer)
        {
            Debug.LogWarning("[RoundManager] StartNextRoundAfterDelay aborted: no longer server.");
            nextRoundRoutineRunning = false;
            yield break;
        }

        if (matchOver.Value)
        {
            Debug.Log("[RoundManager] StartNextRoundAfterDelay aborted: match is over.");
            nextRoundRoutineRunning = false;
            yield break;
        }

        if (!ResolvePlayers())
        {
            Debug.LogWarning("[RoundManager] Could not resolve players before next round reset.");
            nextRoundRoutineRunning = false;
            yield break;
        }

        if (!ResolveSpawns())
        {
            Debug.LogWarning("[RoundManager] Could not resolve generated spawns before next round reset.");
            nextRoundRoutineRunning = false;
            yield break;
        }

        spawnsSwapped.Value = !spawnsSwapped.Value;

        Debug.Log(
            $"[RoundManager] Restarting round. " +
            $"spawnsSwapped={spawnsSwapped.Value} " +
            $"spawnForP1={(spawnsSwapped.Value ? spawnB.name : spawnA.name)} " +
            $"spawnForP2={(spawnsSwapped.Value ? spawnA.name : spawnB.name)}"
        );

        ResetPlayerForRound(player1, spawnsSwapped.Value ? spawnB : spawnA);
        ResetPlayerForRound(player2, spawnsSwapped.Value ? spawnA : spawnB);

        roundOver.Value = false;
        nextRoundRoutineRunning = false;

        Debug.Log("[RoundManager] Next round started.");
    }

    private void ResetPlayerForRound(PlayerHealth player, Transform spawnPoint)
    {
        if (player == null)
        {
            Debug.LogWarning("[RoundManager] ResetPlayerForRound: player is null.");
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogWarning($"[RoundManager] ResetPlayerForRound: spawnPoint is null for player '{player.name}'.");
            return;
        }

        Rigidbody rb = player.GetComponentInChildren<Rigidbody>(true);
        Transform moveRoot = (rb != null) ? rb.transform : player.transform;

        NetworkTransform netTx = moveRoot.GetComponent<NetworkTransform>();
        if (netTx == null)
            netTx = player.GetComponent<NetworkTransform>();

        ServerAuthoritativeMovement move = player.GetComponent<ServerAuthoritativeMovement>();
        if (move == null)
            move = player.GetComponentInChildren<ServerAuthoritativeMovement>(true);

        Vector3 targetPos = spawnPoint.position;
        Quaternion targetRot = spawnPoint.rotation;

        Debug.Log(
            $"[RoundManager] ResetPlayerForRound BEGIN " +
            $"player={player.name} owner={player.OwnerClientId} " +
            $"moveRoot={moveRoot.name} hasRB={(rb != null)} hasNetTx={(netTx != null)} " +
            $"targetPos={targetPos} targetRot={targetRot.eulerAngles}"
        );

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = targetPos;
            rb.rotation = targetRot;
        }
        else
        {
            moveRoot.SetPositionAndRotation(targetPos, targetRot);
        }

        if (netTx != null)
        {
            netTx.Teleport(targetPos, targetRot, moveRoot.localScale);
        }

        if (move != null)
            move.ResetForNextRound();

        player.ResetHealth();

        Debug.Log(
            $"[RoundManager] ResetPlayerForRound END " +
            $"player={player.name} hp={player.currentHealth.Value} dead={player.isDead.Value} " +
            $"newPos={moveRoot.position}"
        );
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartNextMatchServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        ulong sender = rpcParams.Receive.SenderClientId;

        if (sender != NetworkManager.ServerClientId)
        {
            Debug.LogWarning($"[RoundManager] Non-host client {sender} tried to start next match.");
            return;
        }

        player1RoundWins.Value = 0;
        player2RoundWins.Value = 0;
        roundOver.Value = false;
        matchOver.Value = false;
        spawnsSwapped.Value = false;
        nextRoundRoutineRunning = false;

        if (!ResolvePlayers())
        {
            Debug.LogWarning("[RoundManager] Could not resolve players when starting next match.");
            return;
        }

        if (!ResolveSpawns())
        {
            Debug.LogWarning("[RoundManager] Could not resolve spawns when starting next match.");
            return;
        }

        ResetPlayerForRound(player1, spawnA);
        ResetPlayerForRound(player2, spawnB);

        Debug.Log("[RoundManager] New match started.");
    }
}