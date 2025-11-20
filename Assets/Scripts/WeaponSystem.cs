using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class WeaponSystem : MonoBehaviour 
{

    [Header("Refs")]
    public GameObject bullet;          
    public Transform firePoint;        
    public Transform aimTransform;     

    [Header("Settings")]
    public float bulletSpeed = 200f;
    public float firingSpeed = 0.2f;
    public float maxBullet = 10f;

    [Header("Offsets (relative to PlayerCam)")]
    public Vector3 positionOffset = new Vector3(0.3f, -0.25f, 0.6f);
    public Vector3 rotationOffset;

    [Header("Bullet Visual Offset")]
 
    public Vector3 bulletRotationOffset;

    bool weaponCanFire = true;

    void LateUpdate()
    {
       
        if (aimTransform != null)
        {
          
            transform.position = aimTransform.position; //+ aimTransform.TransformDirection(positionOffset)


            transform.rotation = aimTransform.rotation; //* Quaternion.Euler(rotationOffset)
        }

        
        if (Input.GetMouseButton(0) && maxBullet > 0 && weaponCanFire)
        {
            Shoot();
        }
    }

    void Shoot()
    {

        weaponCanFire = false;
        Quaternion spawnRot = firePoint.rotation * Quaternion.Euler(bulletRotationOffset);
        GameObject bulletClone = Instantiate(bullet, firePoint.position, spawnRot);

        Rigidbody rb = bulletClone.GetComponent<Rigidbody>();
        rb.linearVelocity = firePoint.forward * bulletSpeed;
        bulletClone.SetActive(true);
        Destroy(bulletClone, 5f);
        maxBullet--;

        StartCoroutine(CooldownTimer());
    }

    IEnumerator CooldownTimer()
    {
        yield return new WaitForSeconds(firingSpeed);
        weaponCanFire = true;
    }

}
