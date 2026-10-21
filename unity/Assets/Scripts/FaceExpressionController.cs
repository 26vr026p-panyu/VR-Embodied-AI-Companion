using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FaceExpressionController : MonoBehaviour
{
    [Header("Face")]
    public SkinnedMeshRenderer faceRenderer;
    public bool IsReady => faceMesh != null && targetWeights != null;
    // Observation only: the selected semantic target, not instantaneous facial weights.
    public string CurrentSemanticExpression { get; private set; } = "neutral";

    [Header("Transition")]
    [Tooltip("普通表情从0到100大约需要多少秒")]
    public float transitionDuration = 0.35f;

    [Tooltip("嘴型变化速度，一般比表情快")]
    public float mouthTransitionDuration = 0.08f;


    // 当前希望每个 BlendShape 最终达到的数值
    private float[] targetWeights;

    private Mesh faceMesh;


    // =====================================================
    // 嘴型 BlendShape
    // 表情切换时不会清掉这些
    // =====================================================

    private readonly HashSet<string> mouthShapes =
        new HashSet<string>()
        {
            "aa",
            "ih",
            "ou",
            "ee",
            "oh"
        };


    // =====================================================
    // Start
    // =====================================================

    private void Start()
    {
        if (faceRenderer == null)
        {
            faceRenderer =
                GetComponent<SkinnedMeshRenderer>();
        }


        if (faceRenderer == null)
        {
            Debug.LogError(
                "[Face] SkinnedMeshRenderer not found."
            );

            enabled = false;

            return;
        }


        faceMesh =
            faceRenderer.sharedMesh;


        if (faceMesh == null)
        {
            Debug.LogError(
                "[Face] Mesh not found."
            );

            enabled = false;

            return;
        }


        targetWeights =
            new float[
                faceMesh.blendShapeCount
            ];


        // 读取模型当前数值
        for (
            int i = 0;
            i < targetWeights.Length;
            i++
        )
        {
            targetWeights[i] =
                faceRenderer
                    .GetBlendShapeWeight(i);
        }


        Debug.Log(
            "[Face] Expression Controller ready. "
            + "BlendShapes: "
            + faceMesh.blendShapeCount
        );
    }


    // =====================================================
    // Update
    // =====================================================

    private void Update()
    {
        if (
            faceRenderer == null
            || faceMesh == null
        )
        {
            return;
        }


        // ---------------------------------------------
        // 所有BlendShape逐渐靠近目标值
        // ---------------------------------------------

        for (
            int i = 0;
            i < targetWeights.Length;
            i++
        )
        {
            string shapeName =
                faceMesh.GetBlendShapeName(i);


            float duration =
                mouthShapes.Contains(shapeName)
                ? mouthTransitionDuration
                : transitionDuration;


            duration =
                Mathf.Max(
                    0.01f,
                    duration
                );


            float speed =
                100f / duration;


            float current =
                faceRenderer
                    .GetBlendShapeWeight(i);


            float next =
                Mathf.MoveTowards(
                    current,
                    targetWeights[i],
                    speed * Time.deltaTime
                );


            faceRenderer
                .SetBlendShapeWeight(
                    i,
                    next
                );
        }



    }


    // =====================================================
    // 设置表情
    // =====================================================

    public void SetExpression(
        string shapeName,
        float weight = 100f
    )
    {
        if (faceMesh == null)
            return;


        int index =
            faceMesh
                .GetBlendShapeIndex(
                    shapeName
                );


        if (index < 0)
        {
            Debug.LogWarning(
                "[Face] BlendShape not found: "
                + shapeName
            );

            return;
        }



        // ---------------------------------------------
        // 先让旧表情逐渐回到0
        // 但不要影响嘴型
        // ---------------------------------------------

        for (
            int i = 0;
            i < faceMesh.blendShapeCount;
            i++
        )
        {
            string currentName =
                faceMesh
                    .GetBlendShapeName(i);


            if (
                mouthShapes.Contains(
                    currentName
                )
            )
            {
                continue;
            }


            // blink单独控制
            if (
                currentName == "blink"
            )
            {
                continue;
            }


            targetWeights[i] =
                0f;
        }


        if (!mouthShapes.Contains(shapeName) && shapeName != "blink")
            CurrentSemanticExpression = weight <= 0 ? "neutral" : SemanticName(shapeName);

        targetWeights[index] =
            Mathf.Clamp(
                weight,
                0f,
                100f
            );


        Debug.Log(
            "[Face] Expression -> "
            + shapeName
        );
    }


    // =====================================================
    // 回到普通表情
    // =====================================================

    public void SetNeutral()
    {
        if (faceMesh == null)
            return;


        for (
            int i = 0;
            i < faceMesh.blendShapeCount;
            i++
        )
        {
            string name =
                faceMesh
                    .GetBlendShapeName(i);


            if (
                mouthShapes.Contains(name)
                || name == "blink"
            )
            {
                continue;
            }


            targetWeights[i] =
                0f;
        }


        CurrentSemanticExpression = "neutral";
        Debug.Log(
            "[Face] Expression -> neutral"
        );
    }


    // =====================================================
    // 嘴型
    // =====================================================

    static string SemanticName(string name)
    {
        switch(name) { case "happy": case "sad": case "surprised": case "angry": case "relaxed": case "concerned": case "shy": return name; default: return "unknown"; }
    }

    public void SetMouth(
        string mouthName,
        float weight = 100f
    )
    {
        if (faceMesh == null)
            return;


        // 其他基础嘴型回到0
        foreach (
            string shape
            in mouthShapes
        )
        {
            int index =
                faceMesh
                    .GetBlendShapeIndex(
                        shape
                    );


            if (index >= 0)
            {
                targetWeights[index] =
                    0f;
            }
        }


        int targetIndex =
            faceMesh
                .GetBlendShapeIndex(
                    mouthName
                );


        if (targetIndex < 0)
        {
            Debug.LogWarning(
                "[Face] Mouth BlendShape not found: "
                + mouthName
            );

            return;
        }


        targetWeights[targetIndex] =
            Mathf.Clamp(
                weight,
                0f,
                100f
            );
    }


    public void CloseMouth()
    {
        if (!IsReady) return;
        foreach (
            string shape
            in mouthShapes
        )
        {
            int index =
                faceMesh
                    .GetBlendShapeIndex(
                        shape
                    );


            if (index >= 0)
            {
                targetWeights[index] =
                    0f;
            }
        }
    }


    // =====================================================
    // Blink
    // =====================================================

    public void Blink()
    {
        StartCoroutine(
            BlinkCoroutine()
        );
    }


    private IEnumerator BlinkCoroutine()
    {
        int blinkIndex =
            faceMesh
                .GetBlendShapeIndex(
                    "blink"
                );


        if (blinkIndex < 0)
        {
            yield break;
        }


        // 闭眼
        targetWeights[blinkIndex] =
            100f;


        yield return new WaitForSeconds(
            0.12f
        );


        // 睁眼
        targetWeights[blinkIndex] =
            0f;
    }
}