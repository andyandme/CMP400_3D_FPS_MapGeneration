using UnityEngine;

public class MoveCamera : MonoBehaviour 
{

    public Transform cameraPosition;

    private void Update()
    {
        if (!NetworkMapSync.IsGameplayReady())
            return;

        transform.position = cameraPosition.position;
    }

}
