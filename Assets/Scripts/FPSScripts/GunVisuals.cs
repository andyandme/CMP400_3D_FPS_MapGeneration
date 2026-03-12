using UnityEngine;
using System.Collections;

public class GunVisuals : MonoBehaviour
{
    [Header("Refs")]
    public Transform triggerTf;
    public Transform slideTf;
    public Transform recoilPivot;


    [Header("Trigger Move")]
    public Vector3 triggerLocalDown;
    public float triggerSpeed = 30f;

    [Header("Slide Move")]
    public Vector3 slideLocalBack;
    public float slideKickSpeed = 25f;
    public float slideReturnSpeed = 15f;

    [Header("Recoil")]
    public Vector3 recoilEuler;
    public float recoilKickSpeed = 20f;
    public float recoilReturnSpeed = 12f;


    Vector3 triggerStart;
    Vector3 slideStart;

  

    Coroutine recoilRoutine;
    Coroutine slideRoutine;
    Coroutine triggerRoutine;

    void Awake()
    {
        if (!recoilPivot)
        {
            recoilPivot = transform;
        }
        if (triggerTf)
        { 
            triggerStart = triggerTf.localPosition;
        }
        if (slideTf) 
        { 
            slideStart = slideTf.localPosition;
        }
    }

    // called each shot
    public void PlayShot()
    {

        if (triggerRoutine != null)
        { 
        StopCoroutine(triggerRoutine);
        } 
        if (slideRoutine != null)
        { 
        StopCoroutine(slideRoutine);
        } 
        if (recoilRoutine != null)
        {
        StopCoroutine(recoilRoutine);
        }        
        if (triggerTf)
        {
        triggerRoutine = StartCoroutine(TriggerKick());
        }
        if (slideTf)
        { 
            slideRoutine = StartCoroutine(SlideKick(false));
        }

        recoilRoutine = StartCoroutine(RecoilKick());
       

    }

    // When ammo hits zero
    public void SetEmptySlideBack()
    {
        if (!slideTf) return;
        slideTf.localPosition = slideStart + slideLocalBack;
    }


    IEnumerator TriggerKick()
    {
        Vector3 downPos = triggerStart + triggerLocalDown;

        // press
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * triggerSpeed;
            triggerTf.localPosition = Vector3.Lerp(triggerStart, downPos, t);
            yield return null;
        }

        // return
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * triggerSpeed;
            triggerTf.localPosition = Vector3.Lerp(downPos, triggerStart, t);
            yield return null;
        }
    }

    IEnumerator SlideKick(bool stayBack)
    {
        Vector3 backPos = slideStart + slideLocalBack;

        // kick back
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * slideKickSpeed;
            slideTf.localPosition = Vector3.Lerp(slideStart, backPos, t);
            yield return null;
        }

        if (stayBack) yield break;

        // return forward
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * slideReturnSpeed;
            slideTf.localPosition = Vector3.Lerp(backPos, slideStart, t);
            yield return null;
        }
    }

    IEnumerator RecoilKick()
    {
       
        Transform pivot = recoilPivot != null ? recoilPivot : transform;

        Quaternion startRot = pivot.localRotation;
        Quaternion kickRot = startRot * Quaternion.Euler(recoilEuler);

        float t = 0f;
        // kick
        while (t < 1f)
        {
            t += Time.deltaTime * recoilKickSpeed;
            pivot.localRotation = Quaternion.Slerp(startRot, kickRot, t);
            yield return null;
        }

        t = 0f;
        // return
        while (t < 1f)
        {
            t += Time.deltaTime * recoilReturnSpeed;
            pivot.localRotation = Quaternion.Slerp(kickRot, startRot, t);
            yield return null;
        }
    }

}