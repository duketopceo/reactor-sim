using System;
using System.Collections.Generic;

namespace ReactorSim
{
    public enum RadMaterial { Concrete, Steel, Lead, Water }

    /// <summary>One layer of shielding: a material and its thickness in metres.</summary>
    public struct Slab
    {
        public readonly RadMaterial Material;
        public readonly double ThicknessM;
        public Slab(RadMaterial material, double thicknessM) { Material = material; ThicknessM = thicknessM; }
    }

    /// <summary>A point in the RouteGraph frame: x east, y front to back, h up from the control-room floor, metres.</summary>
    public struct RadPoint
    {
        public readonly double X, Y, H;
        public RadPoint(double x, double y, double h) { X = x; Y = y; H = h; }
    }

    /// <summary>
    /// Ordered (source area, subject area) pairs mapped to a list of shielding slabs. An empty list is an explicit "no barrier";
    /// a pair that is absent is a register error (Validate, DoseField), never an unshielded zero. Lookup is a linear scan of a
    /// list, so nothing depends on hash iteration order.
    /// </summary>
    public sealed class BarrierTable
    {
        private sealed class Entry
        {
            public string Source, Subject; public Slab[] Slabs;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public int Count { get { return _entries.Count; } }

        public static double HvlM(RadMaterial material)
        {
            switch (material)
            {
                case RadMaterial.Concrete: return Physics.RadConcreteHvlM;
                case RadMaterial.Steel: return Physics.RadSteelHvlM;
                case RadMaterial.Lead: return Physics.RadLeadHvlM;
                case RadMaterial.Water: return Physics.RadWaterHvlM;
                default: return double.NaN;
            }
        }

        /// <summary>2 raised to minus the sum of thickness over HVL for each slab; 1.0 for no slabs.</summary>
        public static double Attenuation(Slab[] slabs)
        {
            if (slabs == null) return 1.0;
            double halvings = 0.0;
            for (int i = 0; i < slabs.Length; i++) halvings += slabs[i].ThicknessM / HvlM(slabs[i].Material);
            return Math.Pow(0.5, halvings);
        }

        private int IndexOf(string source, string subject)
        {
            if (source == null || subject == null) return -1;
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].Source, source, StringComparison.Ordinal) && string.Equals(_entries[i].Subject, subject, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>Adds the pair, or replaces its slabs when it exists.</summary>
        public void Set(string sourceArea, string subjectArea, params Slab[] slabs)
        {
            if (sourceArea == null) throw new ArgumentNullException("sourceArea");
            if (subjectArea == null) throw new ArgumentNullException("subjectArea");
            var copy = slabs == null ? new Slab[0] : (Slab[])slabs.Clone();
            int at = IndexOf(sourceArea, subjectArea);
            if (at >= 0) _entries[at].Slabs = copy;
            else _entries.Add(new Entry { Source = sourceArea, Subject = subjectArea, Slabs = copy });
        }

        public bool Remove(string sourceArea, string subjectArea)
        {
            int at = IndexOf(sourceArea, subjectArea);
            if (at < 0) return false;
            _entries.RemoveAt(at);
            return true;
        }

        public bool TryGet(string sourceArea, string subjectArea, out Slab[] slabs)
        {
            int at = IndexOf(sourceArea, subjectArea);
            slabs = at < 0 ? null : _entries[at].Slabs;
            return at >= 0;
        }

        public BarrierTable Clone()
        {
            var c = new BarrierTable();
            foreach (var e in _entries) c.Set(e.Source, e.Subject, e.Slabs);
            return c;
        }

        /// <summary>Problems in the table itself: unknown areas and non-finite or negative thicknesses. Empty when sound.</summary>
        public List<string> Validate()
        {
            var bad = new List<string>();
            foreach (var e in _entries)
            {
                if (!RadiationAreas.IsKnown(e.Source)) bad.Add("barrier table names unknown source area '" + e.Source + "'");
                if (!RadiationAreas.IsKnown(e.Subject)) bad.Add("barrier table names unknown subject area '" + e.Subject + "'");
                foreach (var s in e.Slabs)
                {
                    if (!Physics.IsFinite(s.ThicknessM) || s.ThicknessM < 0.0)
                        bad.Add("barrier " + e.Source + " -> " + e.Subject + " has a bad thickness " + s.ThicknessM);
                    if (!Physics.IsFinite(HvlM(s.Material)) || HvlM(s.Material) <= 0.0)
                        bad.Add("barrier " + e.Source + " -> " + e.Subject + " has an unknown material");
                }
            }
            return bad;
        }
    }

    /// <summary>
    /// The areas the dose model knows: the RouteGraph room IDs, space.reactor_hall for the hall above the water, and the Core-only
    /// area.reactor_pool for the pool and core volume below the waterline (KTD6). Core cannot reference RouteGraph, so the IDs and
    /// the numbers copied into Physics.cs are pinned by RadiationAreaRouteTests.
    /// </summary>
    public static class RadiationAreas
    {
        public const string ControlRoomId = "room.control";
        public const string CorridorId = "room.corridor";
        public const string LiftLobbyId = "room.lift_lobby";
        public const string LiftId = "room.lift";
        public const string ReactorDeckId = "room.reactor_deck";
        public const string TurbineHallId = "room.turbine_hall";
        public const string ReactorHallId = "space.reactor_hall";
        public const string ReactorPoolId = "area.reactor_pool";

        private static readonly string[] Ids =
        {
            ControlRoomId, CorridorId, LiftLobbyId, LiftId, ReactorDeckId, TurbineHallId, ReactorHallId, ReactorPoolId
        };

        // One reference point per area, in the same order as Ids. Rooms use a point inside the room at dosimeter height above the
        // floor; the deck uses the hall-end of the vestibule, 1 m past the lift opening.
        private static readonly RadPoint[] RefPoints =
        {
            new RadPoint(11.2, 2.0, Physics.RadReferenceHeightM),                                   // route.door_east
            new RadPoint(20.2, 2.0, Physics.RadReferenceHeightM),                                   // route.corridor_mid
            new RadPoint(18.3, 0.0, Physics.RadReferenceHeightM),                                   // route.lift_lobby
            new RadPoint(18.3, -2.0, Physics.RadReferenceHeightM),                                  // route.lift_lower
            new RadPoint(15.2, -2.0, Physics.RadDeckFloorHM + Physics.RadReferenceHeightM),         // vestibule, at the hall opening
            new RadPoint(29.4, 2.0, Physics.RadReferenceHeightM),                                   // route.turbine_entrance
            new RadPoint(Physics.RadHallCentreXM, Physics.RadHallCentreYM, Physics.RadDeckFloorHM + Physics.RadReferenceHeightM),   // over the pool axis
            new RadPoint(Physics.RadModuleXM, Physics.RadModuleYM + 3.0, Physics.RadCoreHM)         // in the pool, 3 m from the core
        };

        public static IReadOnlyList<string> AllAreaIds { get { return Ids; } }

        public static bool IsKnown(string areaId)
        {
            if (areaId == null) return false;
            for (int i = 0; i < Ids.Length; i++)
                if (string.Equals(Ids[i], areaId, StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool TryGetReferencePoint(string areaId, out RadPoint point)
        {
            if (areaId != null)
                for (int i = 0; i < Ids.Length; i++)
                    if (string.Equals(Ids[i], areaId, StringComparison.Ordinal)) { point = RefPoints[i]; return true; }
            point = default(RadPoint);
            return false;
        }

        /// <summary>The RouteGraph space a Core-only sub-area belongs to; null for an area that is itself a RouteGraph ID.</summary>
        public static string ParentOf(string areaId)
        {
            return string.Equals(areaId, ReactorPoolId, StringComparison.Ordinal) ? ReactorHallId : null;
        }

        private static readonly BarrierTable Barriers = BuildBarriers();

        /// <summary>The shipped barrier table: every source area (pool, hall) against every area. Treat as read-only; use Clone to vary it.</summary>
        public static BarrierTable DefaultBarriers { get { return Barriers; } }

        private static BarrierTable BuildBarriers()
        {
            var t = new BarrierTable();
            var water = new Slab(RadMaterial.Water, Physics.RadPoolWaterSlabM);
            var wing = new Slab(RadMaterial.Concrete, Physics.RadWingConcreteSlabM);
            // Core volume: open to itself, water between it and everything else, plus concrete to the wing rooms.
            t.Set(ReactorPoolId, ReactorPoolId);
            t.Set(ReactorPoolId, ReactorHallId, water);
            t.Set(ReactorPoolId, ReactorDeckId, water);
            t.Set(ReactorPoolId, ControlRoomId, water, wing);
            t.Set(ReactorPoolId, CorridorId, water, wing);
            t.Set(ReactorPoolId, LiftLobbyId, water, wing);
            t.Set(ReactorPoolId, LiftId, water, wing);
            t.Set(ReactorPoolId, TurbineHallId, water, wing);
            // Hall above the water: open to itself and to the deck (the same open space), water down to the pool, concrete to the wing.
            t.Set(ReactorHallId, ReactorHallId);
            t.Set(ReactorHallId, ReactorDeckId);
            t.Set(ReactorHallId, ReactorPoolId, water);
            t.Set(ReactorHallId, ControlRoomId, wing);
            t.Set(ReactorHallId, CorridorId, wing);
            t.Set(ReactorHallId, LiftLobbyId, wing);
            t.Set(ReactorHallId, LiftId, wing);
            t.Set(ReactorHallId, TurbineHallId, wing);
            return t;
        }
    }
}
