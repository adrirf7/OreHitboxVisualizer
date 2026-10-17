using System;
using HarmonyLib;

namespace OreHitboxVisualizer
{
    /// <summary>
    /// MineRock5.Damage se ejecuta en el cliente del jugador que golpea (antes del RPC al dueño),
    /// así que sirve para detectar el primer golpe del jugador local también en multijugador.
    /// </summary>
    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
    internal static class MineRock5_Damage_Patch
    {
        private static void Prefix(MineRock5 __instance, HitData hit)
        {
            try
            {
                HitboxManager.OnRockHit(__instance, hit);
            }
            catch (Exception e)
            {
                OreHitboxVisualizerPlugin.Log.LogError("Error on MineRock5 hit: " + e);
            }
        }
    }
}
