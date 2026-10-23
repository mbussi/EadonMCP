using UnityEngine;

namespace UnityMcpBridge
{
    /// <summary>
    /// Placed on a cutscene container by create_cutscene_sequence. Records the settings
    /// the sequence was built from, so update_cutscene_sequence can recompute a change
    /// the same way as at creation instead of guessing from the current scene:
    ///
    ///   DialogueCamera     — the player and NPC, the camera side and the look offset
    ///                        (the look target is the NPC's position plus the offset)
    ///   AreaReveal /
    ///   ArrivalFlythrough  — the ease-in and ease-out fractions of the duration
    ///
    /// It lives in the runtime assembly (UnityMcpBridge.Runtime) so the component stays
    /// valid in saved scenes. It does nothing at runtime.
    /// </summary>
    [AddComponentMenu("Eadon/MCP Bridge/Cutscene Sequence Settings")]
    [DisallowMultipleComponent]
    public class CutsceneSequenceSettings : MonoBehaviour
    {
        [Tooltip("The template the sequence was created from, e.g. DialogueCamera.")]
        public string template;

        [Header("DialogueCamera")]
        [Tooltip("The player the dialogue camera frames.")]
        public Transform player;

        [Tooltip("The NPC the camera looks at.")]
        public Transform npc;

        [Tooltip("The camera side the sequence resolved to: left or right.")]
        public string cameraSide;

        [Tooltip("Offset from the NPC's position to the point the camera looks at.")]
        public Vector3 lookOffset = new Vector3(0f, 1.6f, 0f);

        [Header("AreaReveal / ArrivalFlythrough")]
        [Tooltip("Ease-in as a fraction of the duration (0–1).")]
        [Range(0f, 1f)] public float easeIn;

        [Tooltip("Ease-out as a fraction of the duration (0–1).")]
        [Range(0f, 1f)] public float easeOut;
    }
}
