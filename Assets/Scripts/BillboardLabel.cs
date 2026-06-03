using UnityEngine;

/// <summary>
/// Makes the attached transform always face the main camera (billboard effect).
/// </summary>
public class BillboardLabel : MonoBehaviour
{
    Transform cam;

    void Start()
    {
        cam = Camera.main?.transform;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
    }
}
