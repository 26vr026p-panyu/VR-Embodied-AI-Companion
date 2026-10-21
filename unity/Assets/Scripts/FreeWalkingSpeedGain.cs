using UnityEngine;
using UnityEngine.InputSystem;

public class FreeWalkingSpeedGain : MonoBehaviour
{
    [Header("VR References")]
    public Transform xrOrigin;
    public Transform headCamera;

    [Header("Speed Gain Settings")]
    public bool enableGain = false;

    [Tooltip("速度欺瞒倍率")]
    public float translationGain = 1.15f;

    [Tooltip("忽略非常小的头部抖动")]
    public float movementThreshold = 0.001f;

    [Header("Debug Info")]
    public float realSpeed = 0f;
    public float virtualSpeed = 0f;
    public float currentGain = 1f;

    private Vector3 previousHeadLocalPosition;

    void Start()
    {
        if (headCamera != null)
        {
            previousHeadLocalPosition = headCamera.localPosition;
        }

        currentGain = enableGain ? translationGain : 1.0f;
    }

    void LateUpdate()
    {
        HandleToggleInput();

        // 无论是否移动，都实时更新当前 Gain
        currentGain = enableGain ? translationGain : 1.0f;

        if (xrOrigin == null || headCamera == null)
            return;

        Vector3 currentHeadLocalPosition = headCamera.localPosition;

        Vector3 realLocalDelta =
            currentHeadLocalPosition - previousHeadLocalPosition;

        // 只处理水平移动
        realLocalDelta.y = 0f;

        float distance = realLocalDelta.magnitude;

        if (distance > movementThreshold)
        {
            Vector3 realWorldDelta =
                xrOrigin.TransformVector(realLocalDelta);

            // 真实速度
            if (Time.deltaTime > 0f)
            {
                realSpeed =
                    realWorldDelta.magnitude / Time.deltaTime;

                virtualSpeed =
                    realSpeed * currentGain;
            }

            // 只补偿额外的 Gain 部分
            if (enableGain)
            {
                Vector3 extraWorldMove =
                    realWorldDelta * (translationGain - 1.0f);

                xrOrigin.position += extraWorldMove;
            }
        }
        else
        {
            realSpeed = 0f;
            virtualSpeed = 0f;
        }

        previousHeadLocalPosition =
            headCamera.localPosition;
    }

    private void HandleToggleInput()
    {
        if (AgentInputActions.Pressed("ToggleVisualGain")) ToggleGain();

    }

    public void ToggleGain()
    {
        enableGain = !enableGain;


        Debug.Log(
            enableGain
                ? $"Deception Mode ON | Gain = {translationGain:F2}"
                : "Normal Mode | Gain = 1.00"
        );
    }
}