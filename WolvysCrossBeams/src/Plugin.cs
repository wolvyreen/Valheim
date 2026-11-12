// Wolvy's Cross Beams - lets two angled beams of the same type snap into an X in Valheim
// Author: Wolvyreen(With_ClaudeAI)
//
// Why it is needed (read from assembly_valheim.dll):
//   Player.UpdatePlacementGhost finds the snap, then asks
//   Player.IsOverlappingOtherPiece(p, rotation, pieceName, pieces, allowRotatedOverlap).
//   If that says true, the snap is thrown away and the ghost stays loose.
//   Vanilla says true when a piece with the same name already has its centre within 5 cm of the
//   new one (and, only if the piece's m_allowRotatedOverlap is on, is within 10 degrees of it).
//   Beams have that flag off, so a second beam crossing the first at its centre is treated as a
//   duplicate even when it is turned 90 degrees.
//
// The fix: a same-named piece in the same spot only counts as a duplicate when the new
// piece's snap points land on the existing piece's snap points. That catches every real
// duplicate, including symmetric ones (a beam turned 180, a pole turned any amount, a square
// floor turned 90), and lets a crossing piece through because its ends are somewhere else.
// Pieces with m_allowRotatedOverlap on keep vanilla's extra allowance.
//
// Settings: the shared Wolvyreen mod hub window (src/WolvyreenModHub.cs + src/ModHubUi/, copied verbatim from
// Wolvy's Mob Tracker; never edit them here only) and Configuration Manager both show them.
//
// Valheim is reached by name through reflection.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace WolvysCrossBeams
{
    [BepInPlugin(Guid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "wolvy.valheim.crossbeams";
        public const string PluginName = "Wolvy's Cross Beams";
        public const string PluginVersion = "1.0.5";
        public const string PluginAuthor = "Wolvyreen(With_ClaudeAI)";

        // The same limits the game uses
        internal const float SamePlace = 0.05f;    // metres: vanilla's "same spot" distance
        internal const float RotatedAllowance = 10f; // degrees: vanilla's m_allowRotatedOverlap angle

        // Every log line this mod writes starts with this tag (Wolvyreen's rule for all mods, 2026-11-10)
        internal const string LogTag = "[WolvysCrossBeams] ";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private static FieldInfo _ghostField;      // Player.m_placementGhost (GameObject)
        private static Type _pieceType;
        private static MethodInfo _getSnapPoints;   // Piece.GetSnapPoints(List<Transform>)
        private static bool _failedOnce;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Lets an angled beam snap where it crosses another beam of the same kind. " +
                "A real duplicate (a piece whose snap points land on the existing piece's snap points) is still refused, as in vanilla.");

            // A hook ID of our own per loaded copy: ScriptEngine starts the new copy before the old
            // one is destroyed, and the old one's OnDestroy must only remove its own hooks.
            _harmony = new Harmony(Guid + "." + System.Guid.NewGuid().ToString("N").Substring(0, 8));

            // Wolvyreen's mod hub: this mod's tab in the shared "Wolvy's Mods" window (every setting is bound by now)
            try { WolvyreenModHub.Hub.Register(Guid, PluginName, PluginVersion, Config, Logger, _harmony); }
            catch (Exception e) { Log.LogWarning(LogTag + "Mod hub: could not add Wolvy's Cross Beams: " + e.Message); }

            Type playerType = AccessTools.TypeByName("Player");
            _pieceType = AccessTools.TypeByName("Piece");
            MethodInfo target = playerType == null ? null : AccessTools.Method(playerType, "IsOverlappingOtherPiece");
            _ghostField = playerType == null ? null : AccessTools.Field(playerType, "m_placementGhost");
            _getSnapPoints = _pieceType == null ? null : AccessTools.Method(_pieceType, "GetSnapPoints", new[] { typeof(List<Transform>) });

            ParameterInfo[] ps = target?.GetParameters();
            if (target == null || ps.Length != 5 || ps[0].ParameterType != typeof(Vector3) || ps[1].ParameterType != typeof(Quaternion)
                || ps[2].ParameterType != typeof(string) || ps[4].ParameterType != typeof(bool)
                || _ghostField == null || _getSnapPoints == null)
            {
                Log.LogError(LogTag + PluginName + " " + PluginVersion + ": Player.IsOverlappingOtherPiece / Piece.GetSnapPoints not found or changed in this game version; nothing patched (vanilla snapping).");
                return;
            }
            _harmony.Patch(target, prefix: new HarmonyMethod(typeof(Plugin), nameof(OverlapPrefix)));
            Log.LogInfo(LogTag + PluginName + " " + PluginVersion + " loaded: crossed pieces can snap, real duplicates are still refused.");
        }

        private void OnDestroy()
        {
            try { WolvyreenModHub.Hub.Unregister(); } catch (Exception e) { Log?.LogWarning(LogTag + "Mod hub: " + e.Message); }
            _harmony?.UnpatchSelf();
        }

        // Replaces vanilla's check. Returning false skips the original; on any error we let the original run.
        private static bool OverlapPrefix(object __instance, Vector3 p, Quaternion rotation, string pieceName, object pieces,
                                          bool allowRotatedOverlap, ref bool __result)
        {
            if (Enabled == null || !Enabled.Value) return true;
            try
            {
                var ghost = _ghostField.GetValue(__instance) as GameObject;
                Component ghostPiece = ghost == null ? null : ghost.GetComponent(_pieceType);
                if (ghostPiece == null || pieces == null) return true;

                List<Vector3> ghostAtSnap = null; // worked out only when a candidate is found (this runs every frame)
                __result = false;
                foreach (object o in (IEnumerable)pieces)
                {
                    var piece = o as Component;
                    if (piece == null) continue;
                    // The same three tests as vanilla, in the same order
                    if (Vector3.Distance(p, piece.transform.position) >= SamePlace) continue;
                    if (allowRotatedOverlap && Quaternion.Angle(piece.transform.rotation, rotation) > RotatedAllowance) continue;
                    if (!piece.gameObject.name.StartsWith(pieceName, StringComparison.Ordinal)) continue; // = Utils.CustomStartsWith

                    if (ghostAtSnap == null)
                    {
                        // Vanilla snaps by moving the ghost, never turning it (rotation == the ghost's rotation),
                        // so its snap points after the snap are their world positions moved by the same amount.
                        Vector3 move = p - ghost.transform.position;
                        ghostAtSnap = new List<Vector3>();
                        foreach (Transform t in SnapPoints(ghostPiece)) ghostAtSnap.Add(t.position + move);
                    }
                    var existing = new List<Vector3>();
                    foreach (Transform t in SnapPoints(piece)) existing.Add(t.position);
                    if (IsDuplicate(ghostAtSnap, existing)) { __result = true; break; }
                }
                return false;
            }
            catch (Exception e)
            {
                if (!_failedOnce) { _failedOnce = true; Log.LogWarning(LogTag + "Overlap check failed, using vanilla's: " + e); }
                return true;
            }
        }

        private static List<Transform> SnapPoints(Component piece)
        {
            var list = new List<Transform>();
            _getSnapPoints.Invoke(piece, new object[] { list });
            return list;
        }

        /// A same-named piece in the same spot is a duplicate when every snap point of the new piece
        /// (where it would be after the snap) lands on one of the existing piece's snap points.
        /// No snap points to compare: count it as a duplicate, like vanilla.
        internal static bool IsDuplicate(List<Vector3> ghostSnapsAtSnap, List<Vector3> existingWorldSnaps)
        {
            if (ghostSnapsAtSnap.Count == 0 || existingWorldSnaps.Count == 0) return true;
            foreach (Vector3 g in ghostSnapsAtSnap)
            {
                bool found = false;
                foreach (Vector3 e in existingWorldSnaps)
                    if (Vector3.Distance(g, e) < SamePlace) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }
    }
}
