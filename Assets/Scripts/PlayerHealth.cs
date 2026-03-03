
using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Tooltip("Current health (runtime).")]
    public float currentHealth;

    public System.Action<PlayerHealth> OnDied;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount, object source = null)
    {
        if (amount <= 0f) return;
        if (currentHealth <= 0f) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        if (currentHealth <= 0f)
        {
            OnDied?.Invoke(this);
           
            Debug.Log($"[PlayerHealth] {name} died. Source={source}");
        }
    }
}