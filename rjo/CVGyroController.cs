using UnityEngine;

public class CVGyroController : MonoBehaviour
{
    public bool gyroActive { get; private set; }

    Quaternion lastRotation;
    float activeTime;

    public void EnableGyro()
    {
        gyroActive = true;
        activeTime = 0f;
        lastRotation = Input.gyro.attitude;
        CVTrackingEvents.OnGyroEnabled?.Invoke();
    }

    public void DisableGyro()
    {
        gyroActive = false;
        CVTrackingEvents.OnGyroDisabled?.Invoke();
    }

    public void UpdateGyro(Transform target, CVTrackingSettings settings)
    {
        if (!gyroActive)
            return;

        activeTime += Time.deltaTime;

        Quaternion current = Input.gyro.attitude;
        Quaternion delta = current * Quaternion.Inverse(lastRotation);
        lastRotation = current;

        Vector3 euler = delta.eulerAngles;

        if (settings.assumeCameraVertical)
            euler.z = 0f;

        target.rotation = Quaternion.Slerp(
            target.rotation,
            target.rotation * Quaternion.Euler(euler),
            Time.deltaTime * settings.gyroSmoothing
        );

        if (activeTime > settings.gyroTimeoutSeconds)
            DisableGyro();
    }
}
