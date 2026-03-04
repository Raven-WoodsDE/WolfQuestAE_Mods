using System;
using UnityEngine;
using SharedCommons;

namespace Mapeditor
{
	[Serializable]
	public class DeletedSceneObject
	{
		public int OriginalInstanceId;
		public string ObjectName;
		public string HierarchyPath;
		public Vector3 Position;
		[NonSerialized]
		public bool IsApplied;
		[NonSerialized]
		public GameObject HiddenObject;
	}
}
