using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif
/// <summary>Permission availability only; continuous capture and VAD remain unchanged.</summary>
public static class QuestMicrophonePermission
{
    static bool requested,denied;
    public static bool Available()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if(Permission.HasUserAuthorizedPermission(Permission.Microphone))return true;
        if(requested||denied)return false;
        requested=true;
        var callbacks=new PermissionCallbacks();
        callbacks.PermissionGranted += _=>{requested=false;denied=false;ReliabilityTrace.SetHealth(null,"Microphone",ComponentHealth.Healthy);};
        callbacks.PermissionDenied += _=>{requested=false;denied=true;ReliabilityTrace.SetHealth(null,"Microphone",ComponentHealth.Unavailable);Debug.LogWarning("[Quest] Microphone permission denied; voice unavailable.");};
        Permission.RequestUserPermission(Permission.Microphone,callbacks);
        return false;
#else
        return true;
#endif
    }
    // Explicit retry only; never prompt repeatedly from the frame loop.
    public static void Retry(){requested=false;denied=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){requested=denied=false;}
}
