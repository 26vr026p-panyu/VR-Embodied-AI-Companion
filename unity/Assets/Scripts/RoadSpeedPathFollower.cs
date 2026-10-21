using UnityEngine;

public class RoadSpeedPathFollower : MonoBehaviour
{
    [Header("References")]
    public Transform xrOrigin;
    public Transform[] waypoints;

    [Header("Speed Settings")]
    public float baseSpeed = 2.0f;
    public float visualGain = 1.0f;

    [Header("Path Settings")]
    public bool loop = true;
    public float arriveDistance = 0.2f;
    public float turnSpeed = 3.0f;
    public float heightOffset = 0f;

    [Header("Debug")]
    public int currentIndex = 0;
    public float currentVisualSpeed;

    void Start()
    {
        if (xrOrigin == null)
        {
            Debug.LogError("XR Origin is not assigned.");
            enabled = false;
            return;
        }

        if (waypoints == null || waypoints.Length < 2)
        {
            Debug.LogError("Please assign at least 2 waypoints.");
            enabled = false;
            return;
        }

        MoveToWaypoint(0);
        currentIndex = 1;
    }

    void Update()
    {
        currentVisualSpeed = baseSpeed * visualGain;

        Transform target = waypoints[currentIndex];

        Vector3 currentPosition = xrOrigin.position;
        Vector3 targetPosition = target.position + Vector3.up * heightOffset;

        Vector3 flatCurrent = new Vector3(currentPosition.x, 0f, currentPosition.z);
        Vector3 flatTarget = new Vector3(targetPosition.x, 0f, targetPosition.z);

        Vector3 direction = flatTarget - flatCurrent;

        if (direction.magnitude < arriveDistance)
        {
            GoToNextWaypoint();
            return;
        }

        Vector3 moveDirection = direction.normalized;
        xrOrigin.position += moveDirection * currentVisualSpeed * Time.deltaTime;

        RotateAlongPath(moveDirection);
    }

    void GoToNextWaypoint()
    {
        currentIndex++;

        if (currentIndex >= waypoints.Length)
        {
            if (loop)
            {
                currentIndex = 0;
            }
            else
            {
                enabled = false;
            }
        }
    }

    void RotateAlongPath(Vector3 moveDirection)
    {
        if (moveDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        xrOrigin.rotation = Quaternion.Slerp(
            xrOrigin.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime
        );
    }

    void MoveToWaypoint(int index)
    {
        Vector3 start = waypoints[index].position + Vector3.up * heightOffset;
        xrOrigin.position = start;
    }
}