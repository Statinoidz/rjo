using UnityEngine;

public class CVGyroDeadReckoner
{
    // Tunables
    public float maxGyroSeconds = 4f;
    public float maxGyroDistance = 3f;
    public float maxMetersPerSecond = 1.5f;

    public bool assumeUpright = true;
    public float uprightToleranceDegrees = 20f;

    public float confidenceDecayPerSecond = 0.25f;
    public float minConfidenceBeforeFail = 0.2f;

    // State
    Vector3 accumulatedPosition;
    Quaternion accumulatedRotation;

    float elapsed;
    float confidence = 1f;

    Quaternion lastGyroRotation;

    public bool IsValid => confidence > minConfidenceBeforeFail;

    public void ResetFromVision(Vector3 pos, Quaternion rot)
    {
        accumulatedPosition = pos;
        accumulatedRotation = rot;

        elapsed = 0f;
        confidence = 1f;

        lastGyroRotation = Input.gyro.attitude;
    }

    public bool Update(float deltaTime)
    {
        elapsed += deltaTime;
        confidence -= confidenceDecayPerSecond * deltaTime;

        if (elapsed > maxGyroSeconds)
            return false;

        Quaternion currentGyro = Input.gyro.attitude;
        Quaternion deltaRot = Quaternion.Inverse(lastGyroRotation) * currentGyro;
        lastGyroRotation = currentGyro;

        if (assumeUpright)
        {
            Vector3 euler = deltaRot.eulerAngles;
            euler.x = ClampAngle(euler.x);
            euler.z = ClampAngle(euler.z);
            deltaRot = Quaternion.Euler(euler);
        }

        accumulatedRotation = accumulatedRotation * deltaRot;

        // Estimate forward motion (VERY conservative)
        float distanceStep = maxMetersPerSecond * deltaTime;
        Vector3 step = accumulatedRotation * Vector3.forward * distanceStep;

        if (accumulatedPosition.magnitude + step.magnitude > maxGyroDistance)
            return false;

        accumulatedPosition += step;

        return IsValid;
    }

    public Vector3 GetPosition() => accumulatedPosition;
    public Quaternion GetRotation() => accumulatedRotation;

    float ClampAngle(float a)
    {
        a = Mathf.DeltaAngle(0, a);
        return Mathf.Clamp(a, -uprightToleranceDegrees, uprightToleranceDegrees);
    }
}
