using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class ProjectileDamage : MonoBehaviour
{
    [Header("Damage")]
    public float baseDamageAtSpeed = 30f;
    public float referenceSpeed = 200f;
    public float minimumDamage = 2f;

    [Header("Ricochet / Bounce")]
    public float ricochetDamageMultiplier = 0.35f;
    [Range(0f, 1f)] public float ricochetDotThreshold = 0.35f;

    [Header("Lifetime")]
    public float maxLifeSeconds = 10f;

    [Header("Owner")]
    public Transform ownerRoot;

    private Rigidbody rb;
    private bool hasBounced;
    private bool hasAppliedPlayerDamage;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        Destroy(gameObject, maxLifeSeconds);
    }

    private void OnCollisionEnter(Collision col)
    {
        if (hasAppliedPlayerDamage)
            return;

        if (ownerRoot != null && col.transform.IsChildOf(ownerRoot))
            return;

        Debug.Log($"[ProjectileDamage] Hit collider='{col.collider.name}', hitTransform='{col.transform.name}', root='{col.transform.root.name}'");

        float speed = rb.linearVelocity.magnitude;
        float speedFactor = (referenceSpeed <= 0f) ? 1f : Mathf.Clamp01(speed / referenceSpeed);
        float dmg = Mathf.Max(minimumDamage, baseDamageAtSpeed * speedFactor);

        bool glancing = false;
        if (col.contactCount > 0)
        {
            Vector3 v = rb.linearVelocity.normalized;
            Vector3 n = col.contacts[0].normal.normalized;
            float dot = Mathf.Abs(Vector3.Dot(v, n));
            glancing = dot < ricochetDotThreshold;
        }

        if (hasBounced || glancing)
            dmg *= ricochetDamageMultiplier;

        NetworkObject hitNetworkObject = col.collider.GetComponentInParent<NetworkObject>();
        if (hitNetworkObject != null)
        {
            if (ownerRoot != null && hitNetworkObject.transform == ownerRoot)
                return;

            PlayerHealth playerHealth = hitNetworkObject.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                Debug.LogWarning($"[ProjectileDamage] Found NetworkObject '{hitNetworkObject.name}' but no PlayerHealth on root.");
            }
            else
            {
                string sourceName = ownerRoot != null ? ownerRoot.name : "Unknown";

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                {
                    playerHealth.TakeDamage(dmg, sourceName);
                }
                else
                {
                    playerHealth.TakeDamageServerRpc(dmg, sourceName);
                }

                Debug.Log($"[ProjectileDamage] Applied {dmg:F1} damage to '{playerHealth.name}' from '{sourceName}'");

                hasAppliedPlayerDamage = true;
                Destroy(gameObject);
                return;
            }
        }

        hasBounced = true;
    }
}