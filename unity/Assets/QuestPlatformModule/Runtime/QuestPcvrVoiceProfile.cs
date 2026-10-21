using System;
/// <summary>Opt-in settings for verified Quest PCVR capture; uses the existing VAD algorithm.</summary>
[Serializable]
public sealed class QuestPcvrVoiceProfile
{
    public float normalThreshold=.006f;
    public float normalRequiredSeconds=.12f;
    public float normalEvidenceGapSeconds=.08f;
    public float endpointSilenceThreshold=.002f;
    public float onsetEvidenceThreshold=.002f;
    public float onsetEvidenceGapSeconds=.12f;
    // No recorded double-talk/echo gate yet: preserve current barge-in baseline.
    public bool overrideBargeDetection=false;
    public float bargeThreshold=.03f;
    public float bargeRequiredSeconds=.18f;
    public static bool Applies(bool optedIn,bool windowsRuntime,bool trackedHmd,bool desktopEmulation,string microphone)
    {
        return optedIn&&windowsRuntime&&trackedHmd&&!desktopEmulation&&!string.IsNullOrEmpty(microphone)
            &&microphone.IndexOf("Oculus Virtual Audio Device",StringComparison.OrdinalIgnoreCase)>=0;
    }
}

