using System.Collections.Generic;
using UnityEngine;

public enum SessionMapType
{
    ManualMap1,
    ManualMap2,
    ProceduralFixedSeed,
    ProceduralRandomSeed
}

[System.Serializable]
public struct SessionMapEntry
{
    public SessionMapType mapType;
    public int seed;

    public SessionMapEntry(SessionMapType mapType, int seed = 0)
    {
        this.mapType = mapType;
        this.seed = seed;
    }
}

public class HostSessionConfig : MonoBehaviour
{
    public static HostSessionConfig Instance;

    [Header("Participant Testing Seeds")]
    [SerializeField] private int participantSeed1 = 11111;
    [SerializeField] private int participantSeed2 = 22222;
    [SerializeField] private int participantSeed3 = 33333;

    private readonly List<SessionMapEntry> participantTestingOrder = new List<SessionMapEntry>();

    public bool HasActiveConfig { get; private set; }
    public int CurrentMapIndex { get; private set; }

    public SessionMapEntry CurrentMap { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ConfigureParticipantTesting()
    {
        participantTestingOrder.Clear();

        participantTestingOrder.Add(new SessionMapEntry(SessionMapType.ManualMap1));
        participantTestingOrder.Add(new SessionMapEntry(SessionMapType.ManualMap2));
        participantTestingOrder.Add(new SessionMapEntry(SessionMapType.ProceduralFixedSeed, participantSeed1));
        participantTestingOrder.Add(new SessionMapEntry(SessionMapType.ProceduralFixedSeed, participantSeed2));
        participantTestingOrder.Add(new SessionMapEntry(SessionMapType.ProceduralFixedSeed, participantSeed3));

        Shuffle(participantTestingOrder);

        CurrentMapIndex = 0;
        CurrentMap = participantTestingOrder[0];
        HasActiveConfig = true;

        Debug.Log($"[HostSessionConfig] Participant Testing configured. First map={CurrentMap.mapType}, seed={CurrentMap.seed}");
    }

    public void ConfigureRandomMap()
    {
        CurrentMap = new SessionMapEntry(SessionMapType.ProceduralRandomSeed);
        CurrentMapIndex = 0;
        HasActiveConfig = true;

        Debug.Log("[HostSessionConfig] Random Map configured.");
    }

    public void ConfigureSeedSelection(int seed)
    {
        CurrentMap = new SessionMapEntry(SessionMapType.ProceduralFixedSeed, seed);
        CurrentMapIndex = 0;
        HasActiveConfig = true;

        Debug.Log($"[HostSessionConfig] Seed Selection configured. Seed={seed}");
    }

    public bool MoveToNextParticipantTestingMap()
    {
        if (!HasActiveConfig)
            return false;

        if (participantTestingOrder.Count == 0)
            return false;

        CurrentMapIndex++;

        if (CurrentMapIndex >= participantTestingOrder.Count)
            return false;

        CurrentMap = participantTestingOrder[CurrentMapIndex];
        Debug.Log($"[HostSessionConfig] Moved to participant map index {CurrentMapIndex}. Type={CurrentMap.mapType}, seed={CurrentMap.seed}");
        return true;
    }

    private void Shuffle(List<SessionMapEntry> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}