using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Optional keyboard testing; production expression smoothing stays in FaceExpressionController.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(FaceExpressionController))]
public sealed class FaceExpressionDebugController : MonoBehaviour
{
    public FaceExpressionController expressionController;

    private void Awake()
    {
        if (expressionController == null)
            expressionController = GetComponent<FaceExpressionController>();
    }

    private void Update()
    {
        if (expressionController == null || !expressionController.isActiveAndEnabled)
            return;
        // =============================================
        // 下面只是调试按键
        // 后面接AI时可以删掉
        // =============================================

        if (!AgentInputActions.DevelopmentAllowed)
            return;


        if (
            AgentInputActions.Pressed("ExpressionHappy")
        )
        {
            expressionController.SetExpression(
                "happy",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("ExpressionSad")
        )
        {
            expressionController.SetExpression(
                "sad",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("ExpressionSurprised")
        )
        {
            expressionController.SetExpression(
                "surprised",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("ExpressionAngry")
        )
        {
            expressionController.SetExpression(
                "angry",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("ExpressionRelaxed")
        )
        {
            expressionController.SetExpression(
                "relaxed",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("ExpressionNeutral")
        )
        {
            expressionController.SetNeutral();
        }


        // Blink测试
        if (
            AgentInputActions.Pressed("Blink")
        )
        {
            expressionController.Blink();
        }


        // 嘴型测试
        if (
            AgentInputActions.Pressed("MouthAa")
        )
        {
            expressionController.SetMouth(
                "aa",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("MouthIh")
        )
        {
            expressionController.SetMouth(
                "ih",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("MouthOu")
        )
        {
            expressionController.SetMouth(
                "ou",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("MouthEe")
        )
        {
            expressionController.SetMouth(
                "ee",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("MouthOh")
        )
        {
            expressionController.SetMouth(
                "oh",
                100f
            );
        }


        if (
            AgentInputActions.Pressed("MouthClose")
        )
        {
            expressionController.CloseMouth();
        }
    }
}
