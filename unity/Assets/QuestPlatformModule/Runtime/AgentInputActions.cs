using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>One semantic map, no avatar or voice policy. Never enables push-to-talk.</summary>
public static class AgentInputActions
{
    static InputActionMap map;
    static readonly Dictionary<string,InputAction> actions=new Dictionary<string,InputAction>();
    static bool installed;
    static void Add(string name,string key,string xr=null)
    {
        var a=map.AddAction(name,InputActionType.Button);
        if(key!=null)a.AddBinding("<Keyboard>/"+key);
        if(xr!=null)a.AddBinding(xr);
        actions.Add(name,a);
    }
    static void Ensure()
    {
        if(map!=null)return;
        map=new InputActionMap("AgentSemanticInput");
        Add("ToggleVisualGain","t","<OculusTouchController>{RightHand}/primaryButton");
        Add("GestureNod","n");Add("GestureWave","v");
        // Development controller chords avoid collision with normal gain A.
        Add("FollowSide","f5");Add("FollowLead","f6");Add("FollowTrail","f7");
        Add("NearbyStay","f8");Add("NearbyApproach","f9");Add("NearbyRoam","f10");
        Add("MotionIdle","f1");Add("MotionWalk","f2");Add("MotionRun","f3");
        string[] names={"ExpressionHappy","ExpressionSad","ExpressionSurprised","ExpressionAngry","ExpressionRelaxed","ExpressionNeutral","Blink","MouthAa","MouthIh","MouthOu","MouthEe","MouthOh","MouthClose"};
        string[] keys={"1","2","3","4","5","0","b","q","w","e","r","u","y"};
        for(int i=0;i<names.Length;i++)Add(names[i],keys[i]);
        Add("PreviewTrackToggle","space");Add("PreviewGainToggle","g");Add("PreviewPanelToggle","h");Add("PreviewSpeedIncrease","upArrow");Add("PreviewSpeedDecrease","downArrow");
        Add("ReleaseDesktopCursor","escape");
        Add("DesktopFast",null);actions["DesktopFast"].AddBinding("<Keyboard>/leftShift");actions["DesktopFast"].AddBinding("<Keyboard>/rightShift");
        Add("DesktopLookHeld",null);actions["DesktopLookHeld"].AddBinding("<Mouse>/rightButton");
        var move=map.AddAction("DesktopMove",InputActionType.Value);move.AddCompositeBinding("2DVector").With("Up","<Keyboard>/w").With("Down","<Keyboard>/s").With("Left","<Keyboard>/a").With("Right","<Keyboard>/d");actions.Add("DesktopMove",move);
        var look=map.AddAction("DesktopLook",InputActionType.Value,"<Mouse>/delta");actions.Add("DesktopLook",look);
        Chord("GestureNod","<OculusTouchController>{RightHand}/secondaryButton");
        Chord("GestureWave","<OculusTouchController>{RightHand}/triggerPressed");
        Chord("FollowSide","<OculusTouchController>{LeftHand}/primaryButton");
        Chord("FollowLead","<OculusTouchController>{LeftHand}/secondaryButton");
        Chord("FollowTrail","<OculusTouchController>{RightHand}/thumbstickClicked");
        Chord("NearbyStay","<OculusTouchController>{LeftHand}/thumbstickClicked");
        Chord("NearbyApproach","<OculusTouchController>{LeftHand}/triggerPressed");
        map.Enable();
        if(!installed){Application.quitting+=Dispose;installed=true;}
    }
    static void Chord(string name,string button){actions[name].AddCompositeBinding("ButtonWithOneModifier").With("Modifier","<OculusTouchController>{LeftHand}/gripPressed").With("Button",button);}
    public static bool Pressed(string semantic){Ensure();return actions[semantic].WasPressedThisFrame();}
    public static bool Held(string semantic){Ensure();return actions[semantic].IsPressed();}
    public static Vector2 Vector(string semantic){Ensure();return actions[semantic].ReadValue<Vector2>();}
    public static InputActionMap Map { get {Ensure();return map;} }
    public static bool DevelopmentAllowed
    {
        get
        {
            if(!Application.isFocused)return false;
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(selected&&(selected.GetComponent<TMPro.TMP_InputField>()||selected.GetComponent<UnityEngine.UI.InputField>()))return false;
            var ui=UnityEngine.Object.FindAnyObjectByType<CompanionAvatar.PresentationModeController>();
            return ui&&!ui.presentationMode;
        }
    }
    static void Dispose(){map?.Dispose();map=null;actions.Clear();}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset(){Dispose();if(installed)Application.quitting-=Dispose;installed=false;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void Initialize(){Ensure();}
}
