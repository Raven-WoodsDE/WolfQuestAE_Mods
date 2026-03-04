using System;
using HarmonyLib;
using UnityEngine;
using WolfQuestEp3;
using SharedCommons;

namespace PackSwitcher
{
	[HarmonyPatch(typeof(HealthUpdater))]
	public static class Patch
	{
		[HarmonyPatch("TakeDamage")]
		[HarmonyPrefix]
		public static void Prefix_TakeDamage(ref int damage, Animal attacker)
		{
			damage = Mathf.Max(20, damage);
		}
	}
}
