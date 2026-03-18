using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkMapSync : NetworkBehaviour
{
    [SerializeField] private FpsMapGenerator mapGenerator;
    public static NetworkMapSync Instance { get; private set; }
    private Coroutine pendingApplyRoutine;

    private readonly NetworkVariable<int> syncedGenerationToken = new(
    0,
    NetworkVariableReadPermission.Everyone,
    NetworkVariableWritePermission.Server
);
    public void SetLocalGameplayReady(bool ready)
    {
        IsMapReadyLocally = ready;
    }

    private readonly NetworkVariable<int> syncedMapType = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<int> syncedSeed = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool _appliedLocally;
    private readonly HashSet<ulong> appliedClientsForCurrentMap = new HashSet<ulong>();
    public bool IsMapReadyLocally { get; private set; }

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<FpsMapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("[NetworkMapSync] No FpsMapGenerator found.");
            return;
        }

        syncedMapType.OnValueChanged += OnSyncedMapChanged;
        syncedSeed.OnValueChanged += OnSyncedSeedChanged;
        syncedGenerationToken.OnValueChanged += OnSyncedGenerationTokenChanged;

        if (IsServer)
        {
            Debug.Log("[NetworkMapSync] Server spawned. Waiting for host config selection.");
        }
        else
        {
            TryApplySyncedConfig();
        }
    }

    private void OnSyncedGenerationTokenChanged(int previousValue, int newValue)
    {
        _appliedLocally = false;
        IsMapReadyLocally = false;

        Debug.Log($"[NetworkMapSync] Generation token changed {previousValue} -> {newValue}");
        ScheduleApplySyncedConfig();
    }

    public static bool IsGameplayReady()
    {
        return Instance != null && Instance.IsMapReadyLocally;
    }

    public override void OnNetworkDespawn()
    {
        syncedMapType.OnValueChanged -= OnSyncedMapChanged;
        syncedSeed.OnValueChanged -= OnSyncedSeedChanged;
        syncedGenerationToken.OnValueChanged -= OnSyncedGenerationTokenChanged;

        if (pendingApplyRoutine != null)
        {
            StopCoroutine(pendingApplyRoutine);
            pendingApplyRoutine = null;
        }

        if (Instance == this)
            Instance = null;
    }


    private void OnSyncedMapChanged(int previousValue, int newValue)
    {
        _appliedLocally = false;
        IsMapReadyLocally = false;

        Debug.Log($"[NetworkMapSync] MapType changed {previousValue} -> {newValue}");
        ScheduleApplySyncedConfig();
    }

    private void OnSyncedSeedChanged(int previousValue, int newValue)
    {
        _appliedLocally = false;
        IsMapReadyLocally = false;

        Debug.Log($"[NetworkMapSync] Seed changed {previousValue} -> {newValue}");
        ScheduleApplySyncedConfig();
    }


    private void ScheduleApplySyncedConfig()
    {
        if (!IsSpawned)
            return;

        if (pendingApplyRoutine != null)
            StopCoroutine(pendingApplyRoutine);

        pendingApplyRoutine = StartCoroutine(ApplySyncedConfigNextFrame());
    }

    private System.Collections.IEnumerator ApplySyncedConfigNextFrame()
    {
        yield return null; // wait one frame so all synced values settle

        pendingApplyRoutine = null;
        TryApplySyncedConfig();
    }

    public void PublishCurrentHostConfig()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[NetworkMapSync] PublishCurrentHostConfig called on non-server.");
            return;
        }

        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<FpsMapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("[NetworkMapSync] No FpsMapGenerator found while publishing config.");
            return;
        }

        HostSessionConfig sessionConfig = FindFirstObjectByType<HostSessionConfig>();
        if (sessionConfig == null || !sessionConfig.HasActiveConfig)
        {
            Debug.LogWarning("[NetworkMapSync] No active HostSessionConfig found on server.");
            return;
        }

        if (RoundManager.Instance != null)
            RoundManager.Instance.ResetBestOf3StateForNewMatchServer();

        SessionMapEntry entry = sessionConfig.CurrentMap;

        _appliedLocally = false;
        IsMapReadyLocally = false;

        appliedClientsForCurrentMap.Clear();

        SetGameplayReadyForAll(false);

        syncedMapType.Value = (int)entry.mapType;
        syncedSeed.Value = entry.seed;
        syncedGenerationToken.Value++;

        Debug.Log($"[NetworkMapSync] Publishing map config. Type={entry.mapType}, Seed={entry.seed}, Token={syncedGenerationToken.Value}");

        mapGenerator.ApplySessionMapConfig(entry);
        mapGenerator.Regenerate();

        _appliedLocally = true;
        IsMapReadyLocally = false;

        appliedClientsForCurrentMap.Add(NetworkManager.ServerClientId);

        TryFinalizeMapLoadOnServer();
    }


    public void SetGameplayReadyForAll(bool ready)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[NetworkMapSync] SetGameplayReadyForAll called on non-server.");
            return;
        }

        IsMapReadyLocally = ready;
        SetGameplayReadyClientRpc(ready);
    }

    [ClientRpc]
    private void SetGameplayReadyClientRpc(bool ready)
    {
        IsMapReadyLocally = ready;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReportMapAppliedServerRpc(int generationToken, ServerRpcParams rpcParams = default)
    {
        if (!IsServer)
            return;

        if (generationToken != syncedGenerationToken.Value)
        {
            Debug.LogWarning(
                $"[NetworkMapSync] Ignoring stale map-applied report from client {rpcParams.Receive.SenderClientId}. " +
                $"ReportedToken={generationToken} CurrentToken={syncedGenerationToken.Value}"
            );
            return;
        }

        ulong sender = rpcParams.Receive.SenderClientId;
        appliedClientsForCurrentMap.Add(sender);

        Debug.Log($"[NetworkMapSync] Client {sender} reported map applied for token {generationToken}.");

        TryFinalizeMapLoadOnServer();
    }

    private void TryFinalizeMapLoadOnServer()
    {
        if (!IsServer || NetworkManager.Singleton == null)
            return;

        if (NetworkManager.Singleton.ConnectedClientsIds == null || NetworkManager.Singleton.ConnectedClientsIds.Count < 2)
            return;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (!appliedClientsForCurrentMap.Contains(clientId))
                return;
        }

        Debug.Log($"[NetworkMapSync] All connected clients applied token {syncedGenerationToken.Value}. Finalising match start.");

        if (RoundManager.Instance != null)
            RoundManager.Instance.OnMapSyncReadyForMatchStartServer();

        SetGameplayReadyForAll(true);
    }


    private void TryApplySyncedConfig()
    {
        if (_appliedLocally)
            return;

        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<FpsMapGenerator>();

        if (mapGenerator == null)
            return;

        if (syncedMapType.Value < 0)
            return;

        SessionMapEntry entry = new SessionMapEntry
        {
            mapType = (SessionMapType)syncedMapType.Value,
            seed = syncedSeed.Value
        };

        Debug.Log($"[NetworkMapSync] TryApplySyncedConfig local IsServer={IsServer} type={entry.mapType} seed={entry.seed} token={syncedGenerationToken.Value}");

        mapGenerator.ApplySessionMapConfig(entry);
        mapGenerator.Regenerate();

        _appliedLocally = true;
        IsMapReadyLocally = false;

        if (!IsServer)
        {
            Debug.Log($"[NetworkMapSync] Client reporting map applied for token {syncedGenerationToken.Value}");
            ReportMapAppliedServerRpc(syncedGenerationToken.Value);
        }
        else
        {
            TryFinalizeMapLoadOnServer();
        }
    }
}