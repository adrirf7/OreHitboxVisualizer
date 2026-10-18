using System;
using HarmonyLib;
using UnityEngine;

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

    /// <summary>Al apuntar a un depósito, añade bajo su nombre cuántos nodos tiene y cuántos colisionan.</summary>
    [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.GetHoverText))]
    internal static class MineRock5_GetHoverText_Patch
    {
        private static void Postfix(MineRock5 __instance, ref string __result)
        {
            if (!OreHitboxVisualizerPlugin.Enabled.Value || !OreHitboxVisualizerPlugin.ShowNodeCount.Value || __instance.m_hitAreas == null)
            {
                return;
            }
            try
            {
                HitboxManager.GetNodeCounts(__instance, out int total, out int colliding);
                __result += "\n" + NodeCountText(total, colliding, total - colliding);
            }
            catch (Exception e)
            {
                OreHitboxVisualizerPlugin.Log.LogError("Error on MineRock5 hover text: " + e);
            }
        }

        private static string NodeCountText(int total, int colliding, int free)
        {
            bool spanish = OreHitboxVisualizerPlugin.IsSpanish();
            bool support = OreHitboxVisualizerPlugin.Mode.Value == HitboxMode.Support;
            string nodes = spanish ? "Nodos" : "Nodes";
            string redLabel = support ? (spanish ? "sostenidos" : "supported") : (spanish ? "colisionando" : "colliding");
            string greenLabel = support ? (spanish ? "sueltos" : "loose") : (spanish ? "libres" : "free");
            string red = ColorUtility.ToHtmlStringRGB(OreHitboxVisualizerPlugin.CollidingColor.Value);
            string green = ColorUtility.ToHtmlStringRGB(OreHitboxVisualizerPlugin.FreeColor.Value);
            return $"<size=80%>{nodes}: {total}  ·  <color=#{red}>{colliding} {redLabel}</color>  ·  <color=#{green}>{free} {greenLabel}</color></size>";
        }
    }
}
