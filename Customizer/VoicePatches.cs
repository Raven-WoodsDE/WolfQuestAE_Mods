using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace Customizer
{
    /// <summary>
    /// Harmony patches for custom howl voices.
    /// Uses WolfDef biography field for voice customization.
    /// </summary>
    public static class VoicePatches
    {
        // Track if we're currently handling a custom howl (prevent re-entry)
        private static bool _isPlayingCustomHowl = false;

        // Cache of wolf voice assignments (WolfDefinition -> voice filename)
        private static Dictionary<WolfDefinition, string> _wolfVoiceCache = new Dictionary<WolfDefinition, string>();

        /// <summary>
        /// Preload voice when wolf cosmetics are applied (same time as skins/eyes).
        /// This ensures the voice is cached before the first howl.
        /// </summary>
        [HarmonyPatch(typeof(WolfCosmetics), "SetCoat")]
        [HarmonyPostfix]
        public static void PreloadVoice_Postfix(WolfDefinition wolfDefinition)
        {
            if (wolfDefinition == null || string.IsNullOrEmpty(wolfDefinition.biography))
                return;

            var data = Plugin.ParseBiography(wolfDefinition.biography);
            if (data.HasCustomVoice)
            {
                // Preload the voice so it's ready for the first howl
                Plugin.Instance.LoadCustomVoice(data.VoiceFile, null);
            }
        }

        [HarmonyPatch(typeof(AnimalSoundLayer), "StartNewSoundEntry")]
        [HarmonyPrefix]
        public static bool StartNewSoundEntry_Prefix(
            AnimalSoundEntry newSoundEntry,
            AnimalSoundLayer __instance,
            Animal ___self,
            AnimalAnimator ___animalAnimator)
        {
            // Skip if no entry or not a howl animation
            if (newSoundEntry == null || string.IsNullOrEmpty(newSoundEntry.animationName))
                return true;

            // Check if this is a howl animation
            if (!IsHowlAnimation(newSoundEntry.animationName))
                return true;

            // Prevent re-entry
            if (_isPlayingCustomHowl)
                return true;

            // Get the wolf definition to check biography
            WolfDefinition wolfDef = ___self.WolfDef;
            if (wolfDef == null)
                return true;

            // Parse biography for voice setting
            var data = Plugin.ParseBiography(wolfDef.biography);
            if (!data.HasCustomVoice)
                return true;

            // Check if we have the voice clip cached
            AudioClip voiceClip = Plugin.GetCachedVoice(data.VoiceFile);

            if (voiceClip == null)
            {
                // Start loading the voice clip for next time
                Plugin.Instance.LoadCustomVoice(data.VoiceFile, null);
                return true; // Use default sound while loading
            }

            try
            {
                _isPlayingCustomHowl = true;

                // Get the wolf's transform for 3D audio positioning
                Transform wolfTransform = null;
                try
                {
                    wolfTransform = ___self?.Physical?.MovingPartTransform;
                }
                catch
                {
                }

                // Start the freeze-and-play coroutine
                if (___animalAnimator != null)
                {
                    Plugin.Instance.StartCoroutine(
                        FreezeAndPlayVoice(___animalAnimator, newSoundEntry.animationName, wolfTransform, voiceClip)
                    );
                }

                // Return false to skip the original sound
                return false;
            }
            catch (Exception ex)
            {
                Globals.Log($"Error in voice patch: {ex.Message}");
                _isPlayingCustomHowl = false;
                return true; // Let original play if we fail
            }
        }

        /// <summary>
        /// Freezes animation at peak (0.9 normalized time), plays custom voice, then unfreezes.
        /// </summary>
        private static IEnumerator FreezeAndPlayVoice(
            AnimalAnimator animator,
            string animationName,
            Transform wolfTransform,
            AudioClip voiceClip)
        {
            // Wait a frame for the animation to start
            yield return null;

            // Try to get the Unity Animator (try different field names)
            Animator unityAnimator = Traverse.Create(animator).Field("animator").GetValue<Animator>();
            if (unityAnimator == null)
                unityAnimator = Traverse.Create(animator).Field("anim").GetValue<Animator>();
            if (unityAnimator == null)
                unityAnimator = Traverse.Create(animator).Field("_animator").GetValue<Animator>();

            // Find which layer the howl animation is on
            int animLayer = 0;
            bool isMainAnim = animator.IsPlayingMainAnimation(animationName);
            bool isHeadAnim = animator.IsPlayingHeadAnimation(animationName);

            if (isHeadAnim)
                animLayer = 1; // Head animations are typically on layer 1

            // Wait until animation reaches ~0.9 normalized time (the peak of the howl)
            const float freezePoint = 0.96f;
            const float maxWaitTime = 5f; // Safety timeout
            float waitedTime = 0f;

            if (unityAnimator != null)
            {
                while (waitedTime < maxWaitTime)
                {
                    AnimatorStateInfo stateInfo = unityAnimator.GetCurrentAnimatorStateInfo(animLayer);

                    // Check if we've reached the freeze point
                    if (stateInfo.normalizedTime >= freezePoint)
                        break;

                    waitedTime += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                // Fallback: if we can't get the animator, just wait a fixed time
                yield return new WaitForSeconds(freezePoint);
            }

            // Store original speed and freeze the animation
            float originalSpeed = isMainAnim ? animator.MainSpeed : animator.HeadSpeed;

            if (isMainAnim)
                animator.MainSpeed = 0f;
            else if (isHeadAnim)
                animator.HeadSpeed = 0f;

            // Play the custom howl sound
            Plugin.PlayCustomHowl(wolfTransform, voiceClip, voiceClip.length);

            // Wait for the voice clip to finish
            yield return new WaitForSeconds(voiceClip.length);

            // Unfreeze - restore original speed
            if (isMainAnim)
                animator.MainSpeed = originalSpeed;
            else if (isHeadAnim)
                animator.HeadSpeed = originalSpeed;

            _isPlayingCustomHowl = false;
        }

        private static bool IsHowlAnimation(string animationName)
        {
            if (string.IsNullOrEmpty(animationName))
                return false;

            // Check for common howl animation name patterns
            string lowerName = animationName.ToLowerInvariant();
            return lowerName.Contains("howl");
        }
    }
}
