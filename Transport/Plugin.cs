using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;
using System.Reflection;

namespace Transport
{
    [BepInPlugin("com.rw.transport", "Transport", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<KeyCode> _cfgToggleKey;
        private ConfigEntry<float> _cfgStaminaDrain;
        private ConfigEntry<float> _cfgHoverHeight;
        private ConfigEntry<float> _cfgDrainMultiplier;
        private ConfigEntry<float> _cfgCarcassLabelRange;

        private Carcass _targetCarcass;
        private Carcass _viableCarcass;
        private bool _isTransporting;
        private FieldInfo _isGroundedField;

        private void Awake()
        {
            _cfgToggleKey = Config.Bind("General", "ToggleKey", KeyCode.T, "Key to pick up/drop carcass");
            _cfgStaminaDrain = Config.Bind("General", "StaminaDrain", 0.05f,
                "Base stamina drain per second while carrying");
            _cfgHoverHeight = Config.Bind("General", "HoverHeight", 2.0f, "Height above the player the carcass hovers");
            _cfgDrainMultiplier = Config.Bind("General", "DrainMultiplier", 1.0f,
                "Modifier to increase or decrease the drain rate");
            _cfgCarcassLabelRange = Config.Bind("General", "LabelRange", 20.0f,
                "Range at which the carcass tooltip label appears");

            _isGroundedField =
                typeof(PortableObjectPhysical).GetField("isGrounded", BindingFlags.NonPublic | BindingFlags.Instance);

            Globals.Log("[Transport] Plugin Initialised.");
        }

        private void Update()
        {
            if (Globals.MenuIsOpen) return;
            if (Globals.playerInstance == null) return;

            // Find nearest viable carcass for tooltip
            _viableCarcass = FindNearestCarcass(_cfgCarcassLabelRange.Value);

            if (Input.GetKeyDown(_cfgToggleKey.Value))
            {
                if (_isTransporting)
                {
                    StopTransporting();
                }
                else
                {
                    TryStartTransporting();
                }
            }

            if (_isTransporting)
            {
                UpdateTransportingLogic();
            }
        }

        private void LateUpdate()
        {
            if (Globals.MenuIsOpen) return;
            if (Globals.playerInstance == null) return;

            if (_isTransporting && _targetCarcass != null)
            {
                // Force position and rotation in LateUpdate to override internal grounding
                Vector3 targetPos = Globals.playerInstance.Position + Vector3.up * _cfgHoverHeight.Value;
                _targetCarcass.Teleport(targetPos, Globals.playerInstance.Rotation);

                // Disable grounding flag to prevent PortableObjectPhysical from fighting us
                SetGrounded(_targetCarcass, false);
            }
        }

        private void TryStartTransporting()
        {
            _targetCarcass = FindNearestCarcass(5f);
            if (_targetCarcass != null)
            {
                _isTransporting = true;
                SetGrounded(_targetCarcass, false);
                Globals.ShowMessage("Transport", "Carcass picked up!");
            }
            else
            {
                Globals.ShowMessage("Transport", "No carcass nearby.");
            }
        }

        private void StopTransporting()
        {
            if (_targetCarcass != null)
            {
                SetGrounded(_targetCarcass, false); // Let it ground normally
                _targetCarcass.Physical.Ground(false, true);
                Globals.ShowMessage("Transport", "Carcass dropped.");
            }

            _isTransporting = false;
            _targetCarcass = null;
        }

        private void UpdateTransportingLogic()
        {
            if (_targetCarcass == null || Globals.playerInstance == null)
            {
                _isTransporting = false;
                return;
            }

            // Drain stamina
            float currentStamina = Globals.playerInstance.State.Energy;
            currentStamina -= _cfgStaminaDrain.Value * _cfgDrainMultiplier.Value * Time.deltaTime;

            if (currentStamina <= 0)
            {
                currentStamina = 0;
                Globals.ShowMessage("Transport", "Out of stamina! Carcass dropped.");
                StopTransporting();
            }

            Globals.playerInstance.State.Energy = currentStamina;
        }

        private void SetGrounded(Carcass carcass, bool grounded)
        {
            if (carcass == null || _isGroundedField == null) return;
            _isGroundedField.SetValue(carcass.Physical, grounded);
        }

        private Carcass FindNearestCarcass(float maxRange)
        {
            if (Globals.meatManager == null || Globals.playerInstance == null) return null;

            Carcass nearest = null;
            float minDist = maxRange;

            foreach (var meatObj in Globals.meatManager.MeatObjects)
            {
                if (meatObj is Carcass carcass)
                {
                    float dist = Vector3.Distance(carcass.Position, Globals.playerInstance.Position);
                    if (dist < minDist)
                    {
                        nearest = carcass;
                        minDist = dist;
                    }
                }
            }

            return nearest;
        }

        private void OnGUI()
        {
            if (Globals.MenuOpen()) return;

            else if (_viableCarcass != null)
            {
                Vector3 screenPos = Camera.main.WorldToScreenPoint(_viableCarcass.Position + Vector3.up * 1f);
                if (screenPos.z > 0)
                {
                    float x = screenPos.x;
                    float y = Screen.height - screenPos.y;

                    GUI.color = Color.white;
                    GUI.Label(new Rect(x - 50, y - 10, 100, 20), $"[{_cfgToggleKey.Value.ToString()}] Pick up");
                }
            }
        }
    }
}
