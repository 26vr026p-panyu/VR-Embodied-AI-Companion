using UnityEngine;
using TMPro;

public class AgentUIDisplay : MonoBehaviour
{
    [Header("References")]
    public PythonExerciseAgent agent;
    public TMP_Text agentText;

    void Update()
    {
        if (agent == null || agentText == null)
            return;

        agentText.text =
            "AI Agent\n\n" +
            "Action: " + agent.currentAction + "\n\n" +
            agent.currentSpeech;
    }
}