using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkSpawnDirector : MonoBehaviour
{
    [Header("Refs")]
    public FpsMapGenerator generator;
   

    [Header("Spawn Settings")]
    public float yOffset = 4.5f;
    public float waitTimeoutSeconds = 15f;

    private Coroutine _startRoutine;

    private void Awake()
    {
        if (generator == null)
            generator = FindFirstObjectByType<FpsMapGenerator>();


        StartCoroutine(HookNetworkManager());
    }

    private IEnumerator HookNetworkManager()
    {
        while (NetworkManager.Singleton == null)
            yield return null;

        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private bool TryGetPlayerObject(ulong clientId, out NetworkObject playerObject)
    {
        playerObject = null;

        if (NetworkManager.Singleton == null)
            return false;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            return false;

        if (client == null || client.PlayerObject == null)
            return false;

        playerObject = client.PlayerObject;
        return true;
    }

    private void OnServerStarted()
    {
        Debug.Log("[NetworkSpawnDirector] Server started.");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (_startRoutine != null)
            StopCoroutine(_startRoutine);

        _startRoutine = StartCoroutine(WaitForBothPlayersThenStartMatch());
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[NetworkSpawnDirector] Client connected: {clientId}");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        if (_startRoutine != null)
            StopCoroutine(_startRoutine);

        _startRoutine = StartCoroutine(WaitForBothPlayersThenStartMatch());
    }

    private bool TryGetSpawnMarkers(out Transform spawn1, out Transform spawn2)
    {
        spawn1 = GameObject.Find("SpawnA")?.transform;
        spawn2 = GameObject.Find("SpawnB")?.transform;

        return spawn1 != null && spawn2 != null;
    }

    private System.Collections.IEnumerator WaitForBothPlayersThenStartMatch()
    {
        yield return null;

        DumpConnections("MatchStart");

        while (NetworkManager.Singleton == null ||
               !NetworkManager.Singleton.IsListening ||
               NetworkManager.Singleton.ConnectedClients.Count < 2)
        {
            Debug.Log("[NetworkSpawnDirector] Waiting for 2 connected clients before starting match...");
            yield return new WaitForSeconds(0.25f);
        }

        DumpConnections("MatchStart");
        DumpSpawnMarkers("MatchStart");

        Transform spawn1 = null;
        Transform spawn2 = null;

        while (!TryGetSpawnMarkers(out spawn1, out spawn2))
        {
            Debug.Log("[NetworkSpawnDirector] Waiting for spawn markers...");
            yield return new WaitForSeconds(0.25f);
        }

        Debug.Log(
            $"[NetworkSpawnDirector] Spawn markers ready? " +
            $"s1={(spawn1 != null)} p1={(spawn1 != null ? spawn1.position : Vector3.zero)} " +
            $"s2={(spawn2 != null)} p2={(spawn2 != null ? spawn2.position : Vector3.zero)}"
        );

        while (!TryGetPlayerObject(0, out NetworkObject hostPlayer) ||
               !TryGetPlayerObject(1, out NetworkObject clientPlayer))
        {
            Debug.Log("[NetworkSpawnDirector] Waiting for both player objects to exist...");
            yield return new WaitForSeconds(0.25f);
        }

        Debug.Log("[NetworkSpawnDirector] Both player objects exist. Starting match placement.");

        TeleportClientToSpawn(0, 1);
        TeleportClientToSpawn(1, 2);


        Debug.Log("[NetworkSpawnDirector] MatchStart hostId=0 otherId=1");

        ResetAllPlayersHealth();

        Debug.Log("[NetworkSpawnDirector] Match started: both players placed.");

        _startRoutine = null;
    }



    private void TeleportClientToSpawn(ulong clientId, int spawnIndex)
    {
        Debug.Log($"[NetworkSpawnDirector] TeleportClientToSpawn BEGIN clientId={clientId} spawnIndex={spawnIndex}");

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[NetworkSpawnDirector] TeleportClientToSpawn called but not server.");
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client == null || client.PlayerObject == null)
        {
            Debug.LogWarning($"[NetworkSpawnDirector] Missing PlayerObject for clientId={clientId}");
            return;
        }

        if (generator == null || !generator.TryGetSpawnWorldPosition(spawnIndex, out Vector3 pos))
        {
            Debug.LogWarning($"[NetworkSpawnDirector] Could not get spawn {spawnIndex}.");
            return;
        }

        pos.y += yOffset;

        var rb = client.PlayerObject.GetComponentInChildren<Rigidbody>(true);
        Transform moveRoot = (rb != null) ? rb.transform : client.PlayerObject.transform;

        var netTx = moveRoot.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (netTx == null)
            netTx = client.PlayerObject.GetComponent<Unity.Netcode.Components.NetworkTransform>();

        Debug.Log($"[NetworkSpawnDirector] Teleport target moveRoot='{moveRoot.name}' hasNetTx={(netTx != null)} rb={(rb != null)} currentPos={moveRoot.position} -> newPos={pos}");

  
        if (rb != null)
        {
            rb.position = pos;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        else
        {
            moveRoot.position = pos;
        }

        if (netTx != null)
        {
            netTx.Teleport(pos, moveRoot.rotation, moveRoot.localScale);
        }

        Debug.Log($"[NetworkSpawnDirector] Teleport DONE clientId={clientId} spawnIndex={spawnIndex} posNow={moveRoot.position} rbPos={(rb != null ? rb.position : Vector3.negativeInfinity)}");
    }




    private void ResetAllPlayersHealth()
    {
        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            var po = kv.Value.PlayerObject;
            if (po == null) continue;

            var health = po.GetComponent<PlayerHealth>();
            if (health == null) continue;

            health.ResetHealth();
        }

        Debug.Log("[NetworkSpawnDirector] ResetAllPlayersHealth done.");
    }

    private void DumpConnections(string tag)
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.Log($"[NetworkSpawnDirector] {tag} NM=null");
            return;
        }

        var ids = NetworkManager.Singleton.ConnectedClientsIds;
        Debug.Log($"[NetworkSpawnDirector] {tag} IsServer={NetworkManager.Singleton.IsServer} ConnectedCount={ids.Count} ServerClientId={NetworkManager.ServerClientId}");

        foreach (var id in ids)
        {
            bool hasClient = NetworkManager.Singleton.ConnectedClients.TryGetValue(id, out var c);
            bool hasPO = hasClient && c != null && c.PlayerObject != null;
            string poName = hasPO ? c.PlayerObject.name : "NULL";
            Vector3 poPos = hasPO ? c.PlayerObject.transform.position : Vector3.negativeInfinity;

            Debug.Log($"[NetworkSpawnDirector] {tag}  clientId={id} hasClient={hasClient} hasPlayerObj={hasPO} playerObj={poName} pos={poPos}");
        }
    }

    private void DumpSpawnMarkers(string tag)
    {
        Vector3 p1 = Vector3.negativeInfinity;
        Vector3 p2 = Vector3.negativeInfinity;

        bool s1 = false;
        bool s2 = false;

        if (generator != null)
        {
            s1 = generator.TryGetSpawnWorldPosition(1, out p1);
            s2 = generator.TryGetSpawnWorldPosition(2, out p2);
        }

        Debug.Log($"[NetworkSpawnDirector] {tag} SpawnMarkers s1={s1} p1={p1} s2={s2} p2={p2}");
    }
}