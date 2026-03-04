using System;
using UnityEngine;
using SharedCommons;

namespace Mapeditor
{
	[Serializable]
	public class Containers
	{
		public static Containers CreateNew()
		{
			return new Containers
			{
				UniqueId = Guid.NewGuid().ToString("N").Substring(0, 8),
				Scale = Vector3.one
			};
		}

		public string UniqueId;
		public string BundlePath;
		public string AssetName;
		public Vector3 Position;
		public Vector3 Rotation;
		public Vector3 Scale = Vector3.one;
		public int Layer;
		public float ObstacleRadius = 2f;
		public bool IsDeletedSceneObject;
		public string SceneObjectName;
		public string SceneObjectPath;
		[NonSerialized]
		public GameObject Instance;
	}
}
