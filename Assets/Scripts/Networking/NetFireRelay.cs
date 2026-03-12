using Unity.Netcode;
using UnityEngine;

public class NetFireRelay : NetworkBehaviour
{
    public GunVisuals gunVisuals;
    public Transform firePoint;
    public GameObject bulletVisualPrefab;
    public float visualSpeed = 200f;

    public void OwnerFired()
    {
        if (!IsOwner) return;

        gunVisuals?.PlayShot();

        FireServerRpc(firePoint.position, firePoint.forward);
    }

    [ServerRpc(RequireOwnership = true)]
    private void FireServerRpc(Vector3 origin, Vector3 forward)
    {
        FireClientRpc(origin, forward);
    }

    [ClientRpc]
    private void FireClientRpc(Vector3 origin, Vector3 forward)
    {

        if (IsOwner) return;

        gunVisuals?.PlayShot();

        if (bulletVisualPrefab != null)
        {
            var go = Instantiate(bulletVisualPrefab, origin, Quaternion.LookRotation(forward));
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.linearVelocity = forward * visualSpeed;
            Destroy(go, 3f);
        }
    }
}