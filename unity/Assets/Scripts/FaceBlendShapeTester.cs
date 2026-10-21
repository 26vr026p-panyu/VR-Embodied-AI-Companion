using UnityEngine;
using UnityEngine.InputSystem;

public class FaceBlendShapeTester : MonoBehaviour
{
    [Header("Face Mesh")]
    public SkinnedMeshRenderer faceRenderer;

    private void Start()
    {
        if (faceRenderer == null)
        {
            faceRenderer = GetComponent<SkinnedMeshRenderer>();
        }

        if (faceRenderer == null)
        {
            Debug.LogError(
                "[FaceTest] SkinnedMeshRenderer not found."
            );
            return;
        }

        if (faceRenderer.sharedMesh == null)
        {
            Debug.LogError(
                "[FaceTest] Mesh not found."
            );
            return;
        }

        Debug.Log(
            "[FaceTest] Ready. BlendShape count = "
            + faceRenderer.sharedMesh.blendShapeCount
        );

        // 顺便把模型里的BlendShape名字打印出来
        for (
            int i = 0;
            i < faceRenderer.sharedMesh.blendShapeCount;
            i++
        )
        {
            Debug.Log(
                "[FaceTest] "
                + i
                + " : "
                + faceRenderer.sharedMesh.GetBlendShapeName(i)
            );
        }
    }


    private void Update()
    {
        if (!AgentInputActions.DevelopmentAllowed)
            return;

        // ========================================
        // 表情
        // ========================================

        if (AgentInputActions.Pressed("ExpressionHappy"))
        {
            ResetAllBlendShapes();
            SetShape("blink", 100f);
        }

        if (AgentInputActions.Pressed("ExpressionSad"))
        {
            ResetAllBlendShapes();
            SetShape("happy", 100f);
        }

        if (AgentInputActions.Pressed("ExpressionSurprised"))
        {
            ResetAllBlendShapes();
            SetShape("surprised", 100f);
        }

        if (AgentInputActions.Pressed("ExpressionAngry"))
        {
            ResetAllBlendShapes();
            SetShape("kanashimu", 100f);
        }

        if (AgentInputActions.Pressed("ExpressionRelaxed"))
        {
            ResetAllBlendShapes();
            SetShape("angryeye", 100f);
        }

        if (AgentInputActions.Pressed("ExpressionNeutral"))
        {
            ResetAllBlendShapes();
        }


        // ========================================
        // 嘴型
        // ========================================

        if (AgentInputActions.Pressed("MouthAa"))
        {
            ResetMouthShapes();
            SetShape("aa", 100f);
        }

        if (AgentInputActions.Pressed("MouthIh"))
        {
            ResetMouthShapes();
            SetShape("ih", 100f);
        }

        if (AgentInputActions.Pressed("MouthOu"))
        {
            ResetMouthShapes();
            SetShape("ou", 100f);
        }

        if (AgentInputActions.Pressed("MouthEe"))
        {
            ResetMouthShapes();
            SetShape("ee", 100f);
        }

        if (AgentInputActions.Pressed("MouthOh"))
        {
            ResetMouthShapes();
            SetShape("oh", 100f);
        }

        if (AgentInputActions.Pressed("MouthClose"))
        {
            ResetMouthShapes();
        }
    }


    public void SetShape(
        string shapeName,
        float weight
    )
    {
        if (
            faceRenderer == null
            || faceRenderer.sharedMesh == null
        )
        {
            return;
        }

        int index =
            faceRenderer.sharedMesh
                .GetBlendShapeIndex(
                    shapeName
                );

        if (index < 0)
        {
            Debug.LogWarning(
                "[FaceTest] BlendShape not found: "
                + shapeName
            );

            return;
        }

        faceRenderer.SetBlendShapeWeight(
            index,
            weight
        );

        Debug.Log(
            "[FaceTest] "
            + shapeName
            + " = "
            + weight
        );
    }


    public void ResetAllBlendShapes()
    {
        if (
            faceRenderer == null
            || faceRenderer.sharedMesh == null
        )
        {
            return;
        }

        int count =
            faceRenderer.sharedMesh
                .blendShapeCount;

        for (int i = 0; i < count; i++)
        {
            faceRenderer.SetBlendShapeWeight(
                i,
                0f
            );
        }
    }


    private void ResetMouthShapes()
    {
        SetShape("aa", 0f);
        SetShape("ih", 0f);
        SetShape("ou", 0f);
        SetShape("ee", 0f);
        SetShape("oh", 0f);
    }
}