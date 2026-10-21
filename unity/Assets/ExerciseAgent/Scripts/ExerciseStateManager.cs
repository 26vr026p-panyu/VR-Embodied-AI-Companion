using UnityEngine;

public class ExerciseStateManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("拖入场景中挂有 FreeWalkingSpeedGain 的对象")]
    public FreeWalkingSpeedGain speedGainController;

    [Header("Exercise State")]

    [Tooltip("已经运动了多少秒")]
    public float elapsedTime = 0f;

    [Tooltip("当前步频 steps/min")]
    public float stepRate = 60f;

    [Tooltip("目标步频 steps/min")]
    public float targetStepRate = 60f;

    [Tooltip("主观运动强度 RPE")]
    [Range(6f, 20f)]
    public float rpe = 10f;

    [Tooltip("当前 VR 视觉速度倍率")]
    public float visualGain = 1.0f;

    private void Update()
    {
        // 记录运动时间
        elapsedTime += Time.deltaTime;

        // 从原来的速度欺瞒脚本中读取当前 Gain
        if (speedGainController != null)
        {
            visualGain = speedGainController.currentGain;
        }
    }
}