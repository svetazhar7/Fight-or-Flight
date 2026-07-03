using UnityEngine;

namespace IslandSystem
{
    /// <summary>Put this on anything that should flatten grass (the player). Registers with the manager.</summary>
    public class GrassInteractor : MonoBehaviour
    {
        [Tooltip("World radius within which grass is pushed down / aside.")]
        [Min(0f)] public float radius = 2.5f;

        void OnEnable() => GrassInteractionManager.Register(this);
        void OnDisable() => GrassInteractionManager.Unregister(this);
    }
}
