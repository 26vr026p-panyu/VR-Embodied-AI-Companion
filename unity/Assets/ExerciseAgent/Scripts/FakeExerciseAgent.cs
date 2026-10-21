using UnityEngine;

public class FakeExerciseAgent : MonoBehaviour
{
    [Header("References")]
    public ExerciseStateManager exerciseState;

    [Header("Agent Output")]
    public string currentAction = "KEEP";
    [TextArea]
    public string currentSpeech = "このペースを維持しましょう。";

    void Update()
    {
        if (exerciseState == null)
            return;

        DecideAction();
    }

    private void DecideAction()
    {
        // 1. RPE 太高，优先休息
        if (exerciseState.rpe >= 17f)
        {
            currentAction = "REST";
            currentSpeech = "かなり負荷が高くなっています。少し休憩しましょう。";
            return;
        }

        // 2. 步频比目标低 5 以上，鼓励
        if (exerciseState.stepRate < exerciseState.targetStepRate - 5f)
        {
            currentAction = "ENCOURAGE";
            currentSpeech = "少しペースが落ちています。もう少しだけリズムを上げてみましょう。";
            return;
        }

        // 3. 其他情况保持
        currentAction = "KEEP";
        currentSpeech = "いいペースです。このまま続けましょう。";
    }
}