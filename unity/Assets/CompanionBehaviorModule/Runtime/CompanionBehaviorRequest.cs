using System;
using UnityEngine;

namespace CompanionAvatar
{
    public enum BehaviorSource { None, Agent, Debug, Contact, Ambient }
    public enum BehaviorPriority { Ambient, Normal, Important, Safety }
    public enum BehaviorGesture { None, Nod, Wave }
    public enum BehaviorSpatialIntent { None, Lead, Side, Trail, Nearby }
    public enum ExpressionReleasePolicy { ReturnToNeutral, HoldUntilReplaced }

    /// <summary>Local intent, not the backend response schema. Empty channels never change state.</summary>
    [Serializable]
    public sealed class CompanionBehaviorRequest
    {
        public string requestId = Guid.NewGuid().ToString("N");
        public long turnId; // 0 = unbound local request; positive = current agent turn only.
        public BehaviorSource source;
        public BehaviorPriority priority = BehaviorPriority.Normal;
        public BehaviorGesture gesture;
        public string expression; // null / empty / "None" = no change. "Neutral" is explicit.
        [Range(0, 1)] public float intensity = 1;
        [Min(0)] public float expressionHoldDuration = 3;
        public ExpressionReleasePolicy expressionReleasePolicy;
        public BehaviorSpatialIntent spatialIntent;
        [Min(0)] public float ttl = 5;
        public double createdAt = Time.realtimeSinceStartupAsDouble;
        public bool bypassSpatialCooldown; // Honoured only at Important / Safety priority.
    }

    public sealed class BehaviorDecision
    {
        public string request = "Valid", gesture = "None", expression = "None", spatial = "None";
        public bool AnyAccepted => gesture == "Accepted" || expression == "Accepted" || spatial == "Accepted";
        public override string ToString() => $"{request}; gesture={gesture}; expression={expression}; spatial={spatial}";
    }
}
