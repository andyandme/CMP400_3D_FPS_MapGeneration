using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class WeaponSystem : MonoBehaviour
{
    [Header("Bullet Parts")]
    public int projectileChildIndex = 0;
    public int casingChildIndex = 1;

    [Header("Casing Ejection")]
    public float casingEjectForce = 3f;

    [Header("Refs")]
    public GameObject bullet;
    public Transform firePoint;
    public Transform aimTransform;
    public Transform casingPoint;
    public GunVisuals gunVisuals;

    [Header("Settings")]
    public float bulletSpeed = 200f;
    public float firingSpeed = 0.2f;
    //public float maxBullet = 10f;
    bool queuedShot = false;

    [Header("Offsets (relative to PlayerCam)")]
    public Vector3 positionOffset;

    [Header("Bullet Visual Offset")]
    public Vector3 bulletRotationOffset;

    private bool weaponCanFire = true;
    private NetworkObject ownerNetworkObject;

    private void Awake()
    {
        ownerNetworkObject = GetComponentInParent<NetworkObject>();
    }

    void Update()
    {
        var ownerNO = GetComponentInParent<Unity.Netcode.NetworkObject>();
        if (ownerNO != null && !ownerNO.IsOwner)
            return;

        if (!NetworkMapSync.IsGameplayReady())
            return;

        if (RoundManager.Instance != null && RoundManager.Instance.MatchOver)
            return;

        if (!IsMatchReady())
            return;

        var health = GetComponentInParent<PlayerHealth>();
        if (health != null && health.isDead.Value)
            return;

        if (aimTransform != null)
        {
            transform.position = aimTransform.position + aimTransform.TransformDirection(positionOffset);
            transform.rotation = aimTransform.rotation;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (weaponCanFire)
            {
                Shoot();
            }
            else
            {
                queuedShot = true;
            }
        }
    }

    private bool IsMatchReady()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.ConnectedClientsList == null)
            return false;

        return nm.ConnectedClientsList.Count >= 2;
    }

    private void Shoot()
    {
        weaponCanFire = false;

        Quaternion spawnRot = firePoint.rotation * Quaternion.Euler(bulletRotationOffset);
        GameObject bulletClone = Instantiate(bullet, firePoint.position, spawnRot);

        if (bulletClone.transform.childCount <= Mathf.Max(projectileChildIndex, casingChildIndex))
        {
            Debug.LogError($"[WeaponSystem] Bullet prefab '{bulletClone.name}' does not have enough children. childCount={bulletClone.transform.childCount}, projectileChildIndex={projectileChildIndex}, casingChildIndex={casingChildIndex}");
            Destroy(bulletClone);
            weaponCanFire = true;
            return;
        }

        Transform projectileTf = bulletClone.transform.GetChild(projectileChildIndex);
        Transform casingTf = bulletClone.transform.GetChild(casingChildIndex);

        projectileTf.SetParent(null, true);
        casingTf.SetParent(null, true);

        if (casingPoint != null)
        {
            casingTf.position = casingPoint.position;
            casingTf.rotation = casingPoint.rotation;
        }
        else
        {
            Debug.LogWarning("[WeaponSystem] casingPoint is null.");
        }

        Destroy(bulletClone);

        var relay = GetComponentInParent<NetFireRelay>();
        if (relay != null)
            relay.OwnerFired();
        else if (gunVisuals != null)
            gunVisuals.PlayShot();

        Rigidbody projRb = projectileTf.GetComponent<Rigidbody>();
        if (projRb != null)
        {
            projRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projRb.linearVelocity = firePoint.forward * bulletSpeed;
        }
        else
        {
            Debug.LogWarning("[WeaponSystem] Projectile has no Rigidbody.");
        }

        ProjectileDamage pd = projectileTf.GetComponent<ProjectileDamage>();
        if (pd != null)
        {
            pd.ownerRoot = ownerNetworkObject != null ? ownerNetworkObject.transform : transform.root;
            pd.referenceSpeed = bulletSpeed;

            Debug.Log($"[WeaponSystem] Assigned ownerRoot = {(pd.ownerRoot != null ? pd.ownerRoot.name : "NULL")}");
        }
        else
        {
            Debug.LogWarning("[WeaponSystem] Projectile has no ProjectileDamage component.");
        }

        Rigidbody casingRb = casingTf.GetComponent<Rigidbody>();
        if (casingRb != null)
        {
            casingRb.linearVelocity = Vector3.zero;
            casingRb.AddForce((casingPoint != null ? casingPoint.right : transform.right) * casingEjectForce, ForceMode.Impulse);
        }
        else
        {
            Debug.LogWarning("[WeaponSystem] Casing has no Rigidbody.");
        }

        Destroy(projectileTf.gameObject, 10f);
        Destroy(casingTf.gameObject, 10f);

        StartCoroutine(CooldownTimer());
    }

    IEnumerator CooldownTimer()
    {
        yield return new WaitForSeconds(firingSpeed);

        weaponCanFire = true;

        if (queuedShot)
        {
            queuedShot = false;
            Shoot();
        }
    }
}
