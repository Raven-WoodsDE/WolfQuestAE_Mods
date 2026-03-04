using System;
using System.Collections.Generic;
using SharedCommons;

namespace Mapeditor
{
	[Serializable]
	public class MapLayoutData
	{
		public static MapLayoutData CreateNew(string name, string sceneAcronym)
		{
			string text = DateTime.UtcNow.ToString("o");
			return new MapLayoutData
			{
				Name = name,
				SceneAcronym = sceneAcronym,
				CreatedAt = text,
				ModifiedAt = text
			};
		}

		public string GetFileExtension()
		{
			return "." + SceneAcronym.ToLowerInvariant() + ".mapl";
		}

		public string Name;
		public string SceneAcronym;
		public string CreatedAt;
		public string ModifiedAt;
		public List<Containers> SpawnedObjects = new List<Containers>();
		public List<DeletedSceneObject> DeletedObjects = new List<DeletedSceneObject>();
	}
}
