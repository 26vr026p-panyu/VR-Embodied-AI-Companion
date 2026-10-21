using UnityEngine;
using TMPro;
using WindsongIntegration;
namespace CompanionAvatar {
 [DefaultExecutionOrder(20020)] public sealed class PresentationResearchHUD:MonoBehaviour {
  public Camera view;public DesktopXRDebugController desktop;public FreeWalkingSpeedGain speedGain;public ConversationStateManager conversation;
  public TMP_Text speedValue,gainValue,stateValue,sourceLabel;public RectTransform panel;
  Vector3 previous;bool sampled;float speed;
  void OnEnable(){sampled=false;speed=0;}
  void LateUpdate(){if(!view)return;bool debug=desktop&&desktop.IsDesktopActive;bool tracked=!debug&&speedGain&&speedGain.isActiveAndEnabled&&UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).isValid;
   var position=view.transform.position;float distance=Vector3.ProjectOnPlane(position-previous,Vector3.up).magnitude;previous=position;
   float target=debug&&sampled&&distance<1?distance/Mathf.Max(Time.deltaTime,.001f):0;sampled=true;speed=Mathf.Lerp(speed,target,1-Mathf.Exp(-Time.deltaTime/.2f));
   if(speedValue)speedValue.text=debug?speed.ToString("F2")+" <size=55%>m/s</size>":tracked?speedGain.realSpeed.ToString("F2")+" <size=55%>m/s</size>":"—";
   if(gainValue)gainValue.text=tracked?speedGain.currentGain.ToString("F2")+"<size=55%>×</size>":"—";
   if(sourceLabel)sourceLabel.text=debug?"DESKTOP MOTION · GAIN NOT APPLIED":tracked?"HMD MOTION":"MOTION SOURCE UNAVAILABLE";
   if(stateValue){var state=conversation?conversation.currentState:ConversationStateManager.ConversationState.LISTENING;stateValue.text=state==ConversationStateManager.ConversationState.AI_SPEAKING?"SPEAKING":state==ConversationStateManager.ConversationState.PROCESSING?"THINKING":"LISTENING";}
   // World-space stereo canvas, fixed angular size in the lower-left edge, never the road centre.
   if(panel){float d=1.6f;var a=view.ViewportToWorldPoint(new Vector3(.025f,.025f,d));var b=view.ViewportToWorldPoint(new Vector3(.325f,.025f,d));panel.position=a;panel.rotation=view.transform.rotation;panel.localScale=Vector3.one*(Vector3.Distance(a,b)/700);}
  }
 }
}
