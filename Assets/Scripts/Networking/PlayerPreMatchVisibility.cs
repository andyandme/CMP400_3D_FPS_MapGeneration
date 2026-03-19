using Unity.Netcode;
using UnityEngine;

public class PlayerPreMatchVisibility : NetworkBehaviour
{
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Collider[] collidersToToggle;

    private bool lastReadyState;

    private void Awake()
    {
        ApplyReadyState(NetworkMapSync.IsGameplayReady());
    }

    private void Update()
    {
        bool ready = NetworkMapSync.IsGameplayReady();

        if (ready == lastReadyState)
            return;

        ApplyReadyState(ready);
    }

    private void ApplyReadyState(bool ready)
    {
        lastReadyState = ready;

        if (visualRoot != null)
            visualRoot.SetActive(ready);

        if (collidersToToggle != null)
        {
            for (int i = 0; i < collidersToToggle.Length; i++)
            {
                if (collidersToToggle[i] != null)
                    collidersToToggle[i].enabled = ready;
            }
        }
    }
}