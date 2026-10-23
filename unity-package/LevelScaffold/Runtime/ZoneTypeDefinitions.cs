using System;
using UnityEngine;

namespace LevelScaffold
{
    public enum ScaffoldZoneType
    {
        SpiralStaircase,
        StraightCorridor,
        CircularChamber,
        RectangularRoom,
        CrawlTunnel,
        OpenArea,
        Custom,
    }

    [Serializable]
    public struct ZoneDimensions
    {
        [Tooltip("Width of the space. Used by all types.")]
        public float Width;

        [Tooltip("Length (forward depth) for corridors, rooms, and open areas.")]
        public float Depth;

        [Tooltip("Ceiling height. Used by all types.")]
        public float Height;

        [Tooltip("Diameter of a circular chamber. Takes precedence over Radius when > 0.")]
        public float Diameter;

        [Tooltip("Number of individual steps (SpiralStaircase only).")]
        public int StepCount;

        [Tooltip("Vertical rise per step in metres (SpiralStaircase only).")]
        public float RiserHeight;

        [Tooltip("Number of full rotations the spiral completes, e.g. 2.5 (SpiralStaircase only).")]
        public float Turns;

        [Tooltip("Radius for circular open areas. Also used as Diameter/2 fallback for CircularChamber.")]
        public float Radius;

        /// <summary>Effective radius for chamber types. Prefers Diameter/2; falls back to Radius.</summary>
        public readonly float EffectiveRadius => Diameter > 0f ? Diameter * 0.5f : Radius;

        /// <summary>Total vertical drop of a spiral staircase in metres.</summary>
        public readonly float StaircaseTotalHeight => StepCount * RiserHeight;

        /// <summary>Returns sensible defaults for a given zone type.</summary>
        public static ZoneDimensions DefaultsFor(ScaffoldZoneType type) => type switch
        {
            ScaffoldZoneType.SpiralStaircase  => new ZoneDimensions { Width = 2f,  StepCount = 25, RiserHeight = 0.2f, Turns = 2.5f },
            ScaffoldZoneType.StraightCorridor => new ZoneDimensions { Width = 3f,  Depth = 15f, Height = 3f },
            ScaffoldZoneType.CircularChamber  => new ZoneDimensions { Diameter = 12f, Height = 4f },
            ScaffoldZoneType.RectangularRoom  => new ZoneDimensions { Width = 8f,  Depth = 10f, Height = 3f },
            ScaffoldZoneType.CrawlTunnel      => new ZoneDimensions { Width = 1.2f, Depth = 6f, Height = 1f },
            ScaffoldZoneType.OpenArea         => new ZoneDimensions { Width = 30f, Depth = 30f, Radius = 15f },
            _                                 => new ZoneDimensions { Width = 5f,  Depth = 5f,  Height = 3f },
        };
    }
}
