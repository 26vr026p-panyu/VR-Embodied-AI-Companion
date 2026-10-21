using UnityEngine;

public class ConversationStateManager : MonoBehaviour
{
    public enum ConversationState
    {
        LISTENING,
        USER_SPEAKING,
        WAITING_FOR_MORE,
        PROCESSING,
        AI_SPEAKING
    }

    public event System.Action<ConversationState, ConversationState> OnStateChanged;

    [Header("Current State")]
    public ConversationState currentState =
        ConversationState.LISTENING;


    private void Start()
    {
        SetState(ConversationState.LISTENING);
    }


    public void SetState(ConversationState newState)
    {
        if (currentState == newState)
            return;

        Debug.Log(
            "[Conversation State] "
            + currentState
            + " -> "
            + newState
        );

        var previous = currentState;
        currentState = newState;
        OnStateChanged?.Invoke(previous, newState);
    }


    public bool IsListening()
    {
        return currentState ==
            ConversationState.LISTENING;
    }


    public bool IsUserSpeaking()
    {
        return currentState ==
            ConversationState.USER_SPEAKING;
    }


    public bool IsProcessing()
    {
        return currentState ==
            ConversationState.PROCESSING;
    }


    public bool IsAISpeaking()
    {
        return currentState ==
            ConversationState.AI_SPEAKING;
    }
}