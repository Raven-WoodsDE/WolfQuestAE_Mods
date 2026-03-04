using System;
using System.Collections.Generic;
using Unity.Collections;
using HarmonyLib;
using Unity.Mathematics;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace Mapeditor
{
	public static class Patch
	{
		public static void RegisterObstacle(GameObject obj, float radius)
		{
			if (obj != null)
			{
				foreach (MapObstacleEntry mapObstacleEntry in Patch._registeredObstacles)
				{
					if (mapObstacleEntry.GameObject == obj) return;
				}
				_registeredObstacles.Add(new MapObstacleEntry
				{
					GameObject = obj,
					Radius = radius
				});
			}
		}

		public static void UnregisterObstacle(GameObject obj)
		{
			if (obj != null)
			{
				for (int i = _registeredObstacles.Count - 1; i >= 0; i--)
				{
					if (_registeredObstacles[i].GameObject == obj)
					{
						_registeredObstacles.RemoveAt(i);
						break;
					}
				}
			}
		}

		public static void ClearAllObstacles()
		{
			_registeredObstacles.Clear();
		}

		public static void UpdateObstacleRadius(GameObject obj, float newRadius)
		{
			foreach (MapObstacleEntry mapObstacleEntry in _registeredObstacles)
			{
				if (mapObstacleEntry.GameObject == obj)
				{
					mapObstacleEntry.Radius = newRadius;
					break;
				}
			}
		}

		public static int GetObstacleCount()
		{
			return _registeredObstacles.Count;
		}

		private static List<MapObstacleEntry> _registeredObstacles = new List<MapObstacleEntry>();

		[HarmonyPatch(typeof(NavigationCalculator), "CollectExtraPointObstacles")]
		public static class CollectExtraPointObstacles_Patch
		{
			[HarmonyPostfix]
			public static void Postfix(NativeList<PointObstacle> listToUse)
			{
				for (int i = _registeredObstacles.Count - 1; i >= 0; i--)
				{
					MapObstacleEntry mapObstacleEntry = _registeredObstacles[i];
					if (mapObstacleEntry.GameObject == null)
					{
						_registeredObstacles.RemoveAt(i);
					}
					else
					{
						Vector3 position = mapObstacleEntry.GameObject.transform.position;
						float3 position2 = new float3(position.x, 0f, position.z);
						PointObstacle pointObstacle = new PointObstacle(position2, mapObstacleEntry.Radius);
						listToUse.Add(pointObstacle);
					}
				}
			}
		}
	}
}
