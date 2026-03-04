using UnityEngine;
using WolfQuestEp3;
using System.Collections;

namespace AnyAnimalAdoption
{
    public class CustomFollowAI : MonoBehaviour
    {
        public Animal target;
        
        // Animators
        private Animator animator;
        private Animation legacyAnim;

        // Clip names
        private string walkClip = "";
        private string runClip = "";
        private string standClip = "";

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            legacyAnim = GetComponentInChildren<Animation>();

            // Ground upon spawn
            if (Physics.Raycast(transform.position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, 1 << Plugin.cfgGroundLayer.Value))
            {
                transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
            }

            // Find valid clip names in legacy animators
            if (legacyAnim != null)
            {
                foreach (AnimationState state in legacyAnim)
                {
                    string name = state.name.ToLower();
                    if (name.Contains("walk") && walkClip == "") walkClip = state.name;
                    if ((name.Contains("run") || name.Contains("trot")) && runClip == "") runClip = state.name;
                    if ((name.Contains("stand") || name.Contains("idle")) && standClip == "") standClip = state.name;
                }
            }
        }

        private void PlayAnimation(bool moving, float speed)
        {
            if (animator != null)
            {
                float speedVal = moving ? speed : 0f;
                // Typical WQ animator parameters
                animator.SetFloat("Speed", speedVal);
                animator.SetFloat("SpeedMagnitude", speedVal);
                return; // Prioritize modern animator
            }

            if (legacyAnim != null)
            {
                string targetClip = standClip;
                if (moving)
                {
                    targetClip = (speed > 4f && runClip != "") ? runClip : walkClip;
                    if (targetClip == "") targetClip = walkClip; // fallback
                }

                if (targetClip != "" && !legacyAnim.IsPlaying(targetClip))
                {
                    legacyAnim.CrossFade(targetClip, 0.25f);
                }
            }
        }

        private void Update()
        {
            if (target == null) return;

            float dist = Vector3.Distance(transform.position, target.Position);

            if (dist > Plugin.cfgFollowDistance.Value)
            {
                Vector3 direction = (target.Position - transform.position).normalized;
                direction.y = 0; // Planar
                
                if (direction != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
                }

                transform.position += direction * Plugin.cfgFollowSpeed.Value * Time.deltaTime;

                // Continuous grounding
                if (Physics.Raycast(transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, 1 << Plugin.cfgGroundLayer.Value))
                {
                    transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
                }

                PlayAnimation(true, Plugin.cfgFollowSpeed.Value);
            }
            else
            {
                PlayAnimation(false, 0f);
            }
        }

        private void LateUpdate()
        {
            // Lock rotation upright. Legacy animators often apply a 90-degree pitch to the root bone.
            Vector3 euler = transform.localEulerAngles;
            transform.localEulerAngles = new Vector3(0f, euler.y, 0f);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 300, 300, 150));
            GUI.color = Color.white;
            GUILayout.Label($"[AnyAnimalAdoption] Ground Layer: {Plugin.cfgGroundLayer.Value}");
            GUILayout.Label($"Layer Name: {LayerMask.LayerToName(Plugin.cfgGroundLayer.Value)}");
            
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Layer --"))
            {
                int val = Plugin.cfgGroundLayer.Value - 1;
                if (val < 0) val = 31;
                Plugin.cfgGroundLayer.Value = val;
            }
            if (GUILayout.Button("Layer ++"))
            {
                int val = Plugin.cfgGroundLayer.Value + 1;
                if (val > 31) val = 0;
                Plugin.cfgGroundLayer.Value = val;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
