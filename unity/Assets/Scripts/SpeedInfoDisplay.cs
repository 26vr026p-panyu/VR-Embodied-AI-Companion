using UnityEngine;
using TMPro;

public class SpeedInfoDisplay : MonoBehaviour
{
    [Header("References")]
    public FreeWalkingSpeedGain speedGain;
    public TextMeshPro speedText;

    void Update()
    {
        if (speedGain == null || speedText == null)
        {
            return;
        }

        string modeName = speedGain.enableGain ? "Deception" : "Normal";

        speedText.text =
            "Speed Deception Demo\n" +
            "Mode: " + modeName + "\n" +
            "Gain: " + speedGain.currentGain.ToString("F2") + "\n" +
            "Real Speed:\n" + speedGain.realSpeed.ToString("F2") + " m/s\n" +
            "VR Speed:\n" + speedGain.virtualSpeed.ToString("F2") + " m/s";
    }
}