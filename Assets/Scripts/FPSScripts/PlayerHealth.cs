using UnityEngine;
using Unity.Netcode;

public class PlayerHealth : NetworkBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Tooltip("Current health (networked runtime value).")]
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> isDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public System.Action<PlayerHealth> OnDied;
    public System.Action<bool> OnDeadStateChanged;

    public override void OnNetworkSpawn()
    {
        isDead.OnValueChanged += OnDeadChanged;

        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }
    }


    public override void OnNetworkDespawn()
    {
        isDead.OnValueChanged -= OnDeadChanged;
    }

    public void ResetHealth()
    {
        if (IsServer)
        {
            currentHealth.Value = maxHealth;
            isDead.Value = false;
        }
    }

    public void TakeDamage(float amount, object source = null)
    {
        if (!IsServer)
        {
            Debug.LogWarning($"[PlayerHealth] TakeDamage called on client for {name}. Damage must be applied on server.");
            return;
        }

        ApplyDamageInternal(amount, source);
    }

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(float amount, string source)
    {
        ApplyDamageInternal(amount, source);
    }

    private void ApplyDamageInternal(float amount, object source = null)
    {
        if (amount <= 0f)
        {
            Debug.LogWarning($"[PlayerHealth] Ignored non-positive damage on {name}. amount={amount}");
            return;
        }

        if (currentHealth.Value <= 0f)
        {
            Debug.LogWarning($"[PlayerHealth] Ignored damage on {name} because health is already <= 0.");
            return;
        }

        if (isDead.Value)
        {
            Debug.LogWarning($"[PlayerHealth] Ignored damage on {name} because isDead is already true.");
            return;
        }

        float oldHealth = currentHealth.Value;
        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - amount);

        Debug.Log(
            $"[PlayerHealth] {name} took {amount:F1} damage. " +
            $"oldHealth={oldHealth:F1} newHealth={currentHealth.Value:F1} source={source}"
        );

        if (currentHealth.Value <= 0f)
        {
            isDead.Value = true;
            OnDied?.Invoke(this);

            Debug.Log($"[PlayerHealth] {name} died. Source={source}");
        }
    }

    private void OnDeadChanged(bool previousValue, bool newValue)
    {
        OnDeadStateChanged?.Invoke(newValue);
    }
}