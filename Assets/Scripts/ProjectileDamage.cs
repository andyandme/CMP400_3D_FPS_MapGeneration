// ProjectileDamage.cs
using UnityEngine;

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
 
        if (ownerRoot != null && col.transform.IsChildOf(ownerRoot))
            return;

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


        IDamageable dmgable = col.collider.GetComponentInParent<IDamageable>();
        if (dmgable != null)
        {
            dmgable.TakeDamage(dmg, source: ownerRoot != null ? ownerRoot.name : "Unknown");
        }

        hasBounced = true;
    }
}