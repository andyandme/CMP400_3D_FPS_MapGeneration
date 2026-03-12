using UnityEngine;
using Unity.Netcode;

public class NetworkMapSync : NetworkBehaviour
{
    [SerializeField] private FpsMapGenerator mapGenerator;

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

    public override void OnNetworkSpawn()
    {
        if (mapGenerator == null)
            mapGenerator = FindFirstObjectByType<FpsMapGenerator>();

        if (mapGenerator == null)
        {
            Debug.LogError("[NetworkMapSync] No FpsMapGenerator found.");
            return;
        }

        if (IsServer)
        {
            Debug.Log("[NetworkMapSync] Server generating map...");

            HostSessionConfig sessionConfig = FindFirstObjectByType<HostSessionConfig>();
            Debug.Log($"[NetworkMapSync] HostSessionConfig found. HasActiveConfig={(sessionConfig != null && sessionConfig.HasActiveConfig)}");

            if (sessionConfig != null && sessionConfig.HasActiveConfig)
            {
                SessionMapEntry entry = sessionConfig.CurrentMap;

                mapGenerator.ApplySessionMapConfig(entry);
                Debug.Log($"[NetworkMapSync] Applied session config before regeneration. Type={entry.mapType}, Seed={entry.seed}");

                syncedMapType.Value = (int)entry.mapType;
                syncedSeed.Value = entry.seed;

                mapGenerator.Regenerate();

                Debug.Log($"[NetworkMapSync] Published type={(int)entry.mapType}, seed={entry.seed}");
                _appliedLocally = true;
            }
            else
            {
                Debug.LogWarning("[NetworkMapSync] No active HostSessionConfig found on server.");
            }
        }
        else
        {
            syncedMapType.OnValueChanged += OnSyncedMapChanged;
            syncedSeed.OnValueChanged += OnSyncedSeedChanged;

            TryApplySyncedConfig();
        }
    }

    public override void OnNetworkDespawn()
    {
        syncedMapType.OnValueChanged -= OnSyncedMapChanged;
        syncedSeed.OnValueChanged -= OnSyncedSeedChanged;
    }

    private void OnSyncedMapChanged(int previousValue, int newValue)
    {
        TryApplySyncedConfig();
    }

    private void OnSyncedSeedChanged(int previousValue, int newValue)
    {
        TryApplySyncedConfig();
    }

    private void TryApplySyncedConfig()
    {
        if (IsServer || _appliedLocally)
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

        mapGenerator.ApplySessionMapConfig(entry);
        mapGenerator.Regenerate();

        Debug.Log($"[NetworkMapSync] Client applied synced config. Type={entry.mapType}, Seed={entry.seed}");

        _appliedLocally = true;
    }
}