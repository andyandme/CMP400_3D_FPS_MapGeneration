using Unity.Netcode;
using UnityEngine;

public class OwnerOnlyEnable : NetworkBehaviour
{
    [Header("Enable only for the owning client")]
    public Behaviour[] ownerOnlyBehaviours;

    [Header("Owner-only camera/audio (auto)")]
    public bool autoToggleCameras = true;
    public bool autoToggleAudioListeners = true;

    public override void OnNetworkSpawn()
    {
        bool owner = IsOwner;

        if (ownerOnlyBehaviours != null)
        {
            for (int i = 0; i < ownerOnlyBehaviours.Length; i++)
            {
                if (ownerOnlyBehaviours[i] != null)
                    ownerOnlyBehaviours[i].enabled = owner;
            }
        }

        if (autoToggleCameras)
        {
            foreach (var cam in GetComponentsInChildren<Camera>(true))
                cam.enabled = owner;
        }

        if (autoToggleAudioListeners)
        {
            foreach (var al in GetComponentsInChildren<AudioListener>(true))
                al.enabled = owner;
        }
    }
}