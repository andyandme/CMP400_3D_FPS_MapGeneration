using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("References")]
    public FpsMapGenerator generator;
    public Transform player1;
    public Transform player2;



    [Header("Options")]
    public bool spawnSecondPlayer = false;
    public float yOffset = 4.5f;        // lift above ground
    public bool zeroVelocityOnSpawn = true;


    private bool spawnedP1 = false;
    private bool spawnedP2 = false;



    private void Awake()
    {
        if (generator == null)
            generator = FindFirstObjectByType<FpsMapGenerator>();

        if (generator != null)
            Debug.Log($"[PlayerSpawner] Using generator: {generator.name} (instanceID={generator.GetInstanceID()})");
        else
            Debug.LogWarning("[PlayerSpawner] No FpsMapGenerator found in scene.");
    }

    private void OnEnable()
    {
        if (generator != null)
            generator.OnMapRegenerated += HandleMapRegenerated;
    }

    private void OnDisable()
    {
        if (generator != null)
            generator.OnMapRegenerated -= HandleMapRegenerated;
    }

    private void Start()
    {
        // In case the map is already generated before this runs
        HandleMapRegenerated();
    }

    private void HandleMapRegenerated()
    {

        spawnedP1 = false;
        spawnedP2 = false;

        Debug.Log("[PlayerSpawner] HandleMapRegenerated called.");
        TrySpawnNow();
    }


    private void Update()
    {
        // If Start order/timing is weird, this guarantees we eventually spawn.
        if (!spawnedP1 || (spawnSecondPlayer && !spawnedP2))
            TrySpawnNow();
    }


    private void TrySpawnNow()
    {
        if (generator == null)
            return;

        if (!spawnedP1 && player1 != null)
            spawnedP1 = SpawnPlayerAtIndex(player1, 1);

        if (spawnSecondPlayer && !spawnedP2 && player2 != null)
            spawnedP2 = SpawnPlayerAtIndex(player2, 2);
    }



    private bool SpawnPlayerAtIndex(Transform player, int index)
    {
        if (!generator.TryGetSpawnWorldPosition(index, out Vector3 pos))
            return false;

        pos.y += yOffset;

       
        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = pos;

            if (zeroVelocityOnSpawn)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        else
        {
            player.position = pos;
        }

        return true;
    }


}