using System;
using UnityEngine;
using SharedCommons;

namespace Customizer
{
    [Serializable]
    public class Containers
    {
        public string BonePath;
        public string BundlePath;
        public string AssetName;
        public Vector3 PositionOffset;
        public Vector3 RotationOffset;
        public Vector3 Scale = Vector3.one;

        [NonSerialized]
        public GameObject Instance;
        [NonSerialized]
        public Transform TargetBone;
    }

    [Serializable]
    public class OutfitData
    {
        public string Name;
        public System.Collections.Generic.List<Containers> Attachments = new System.Collections.Generic.List<Containers>();
    }


    [Serializable]
    public class CustomizationData
    {
        public string SkinFile { get; set; }
        public Color EyeColor { get; set; }
        public bool HasCustomEyes { get; set; }
        public string VoiceFile { get; set; }
        public string OutfitName { get; set; }
        public float? Size { get; set; }
        public string Injury { get; set; }

        public bool HasCustomSkin => !string.IsNullOrEmpty(SkinFile);
        public bool HasCustomVoice => !string.IsNullOrEmpty(VoiceFile);
        public bool HasOutfit => !string.IsNullOrEmpty(OutfitName);
        public bool HasSize => Size.HasValue;
        public bool HasInjury => !string.IsNullOrEmpty(Injury);
    }
}
