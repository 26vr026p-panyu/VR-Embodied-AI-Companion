using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CompanionAvatar
{
    public enum CompanionContactRegion { Head, LeftHand, RightHand, Torso }
    [Serializable] public sealed class CompanionContactEvent : UnityEvent<Collider> { }

    /// <summary>Per-collider contact notifications; does not drive expression, motion, voice or physics forces.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Collider))]
    public sealed class CompanionContactZone : MonoBehaviour
    {
        public CompanionContactRegion region;
        public Transform characterRoot;
        public LayerMask acceptedLayers = ~0;
        [Tooltip("Require a CompanionContactInteractor on the other collider or its parent. Avoids scenery/self contact spam.")]
        public bool requireInteractor = true;
        public bool logContacts = true;
        public CompanionContactEvent onContactEnter = new CompanionContactEvent();
        public CompanionContactEvent onContactStay = new CompanionContactEvent();
        public CompanionContactEvent onContactExit = new CompanionContactEvent();
        public event Action<CompanionContactZone, Collider> ContactEnter;
        public event Action<CompanionContactZone, Collider> ContactStay;
        public event Action<CompanionContactZone, Collider> ContactExit;
        readonly HashSet<Collider> contacts = new HashSet<Collider>();
        readonly List<Collider> expired = new List<Collider>();
        Collider zoneCollider;
        public int ActiveContactCount => contacts.Count;

        void Awake() { zoneCollider = GetComponent<Collider>(); zoneCollider.isTrigger = true; }
        bool Accepts(Collider other)
        {
            if(other == null || !other.enabled || !other.gameObject.activeInHierarchy) return false;
            if(characterRoot != null && other.transform.IsChildOf(characterRoot)) return false;
            if((acceptedLayers.value & (1 << other.gameObject.layer)) == 0) return false;
            var interactor = other.GetComponentInParent<CompanionContactInteractor>();
            return !requireInteractor || (interactor != null && interactor.isActiveAndEnabled);
        }
        void OnTriggerEnter(Collider other)
        {
            if(!isActiveAndEnabled || !Accepts(other) || !contacts.Add(other)) return;
            if(logContacts) Debug.Log("[CompanionContact] Enter: " + region, this);
            ContactEnter?.Invoke(this,other); onContactEnter.Invoke(other);
        }
        void OnTriggerStay(Collider other)
        {
            if(!isActiveAndEnabled) return;
            if(!Accepts(other)) { Exit(other); return; }
            if(!contacts.Contains(other)) OnTriggerEnter(other);
            if(contacts.Contains(other)) { ContactStay?.Invoke(this,other); onContactStay.Invoke(other); }
        }
        void OnTriggerExit(Collider other) { Exit(other); }
        void Exit(Collider other)
        {
            if(!contacts.Remove(other)) return;
            if(logContacts) Debug.Log("[CompanionContact] Exit: " + region, this);
            ContactExit?.Invoke(this,other); onContactExit.Invoke(other);
        }
        void FixedUpdate()
        {
            if(zoneCollider == null || !zoneCollider.enabled) { ClearContacts(); return; }
            // Unity need not send Exit when the other collider is disabled/destroyed.
            expired.Clear();
            foreach(var c in contacts) if(!Accepts(c)) expired.Add(c);
            foreach(var c in expired) Exit(c);
        }
        void OnDisable() { ClearContacts(); }
        void ClearContacts()
        {
            if(contacts.Count == 0) return;
            // A listener can disable this zone while processing a contact event.
            foreach(var c in new List<Collider>(contacts)) Exit(c);
        }
    }
}
