using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;


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
    public float maxBullet = 10f;

    [Header("Offsets (relative to PlayerCam)")]
    public Vector3 positionOffset; 
   

    [Header("Bullet Visual Offset")]
 
    public Vector3 bulletRotationOffset;

    bool weaponCanFire = true;

    void Update()
    {

        if (aimTransform != null)
        {

            transform.position = aimTransform.position + aimTransform.TransformDirection(positionOffset); 


            transform.rotation = aimTransform.rotation; 
        }

        transform.position = aimTransform.position;
        transform.rotation = aimTransform.rotation;
        if (Input.GetMouseButton(0) && maxBullet > 0 && weaponCanFire)
        {
            Shoot();
        }
    }

    void Shoot()
    {

        Debug.Log("Shoot() called");
        weaponCanFire = false;

        Quaternion spawnRot = firePoint.rotation * Quaternion.Euler(bulletRotationOffset);
        GameObject bulletClone = Instantiate(bullet, firePoint.position, spawnRot);
        


        Transform projectileTf = bulletClone.transform.GetChild(projectileChildIndex);
        Transform casingTf = bulletClone.transform.GetChild(casingChildIndex);

     
        projectileTf.SetParent(null);
        casingTf.SetParent(null);

        casingTf.position = casingPoint.position;
        casingTf.rotation = casingPoint.rotation;

        bulletClone.SetActive(true);

        Destroy(bulletClone);

        Rigidbody projRb = projectileTf.GetComponent<Rigidbody>();
        Rigidbody casingRb = casingTf.GetComponent<Rigidbody>();

        if (gunVisuals != null)
            gunVisuals.PlayShot();


        // fire projectile
        projRb.linearVelocity = firePoint.forward * bulletSpeed;

       
        if (casingRb != null)
        {
            casingRb.linearVelocity = Vector3.zero;
            casingRb.AddForce(casingPoint.right * casingEjectForce, ForceMode.Impulse);
        }

        Destroy(projectileTf.gameObject, 10f);
        Destroy(casingTf.gameObject, 10f);

        maxBullet--;

        if (maxBullet <= 0 && gunVisuals != null)
            gunVisuals.SetEmptySlideBack();


        StartCoroutine(CooldownTimer());

    }

    IEnumerator CooldownTimer()
    {
        yield return new WaitForSeconds(firingSpeed);
        weaponCanFire = true;
    }

}
