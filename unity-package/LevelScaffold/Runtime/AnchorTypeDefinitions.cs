namespace LevelScaffold
{
    public enum ScaffoldAnchorType
    {
        /// <summary>Zone entry point. Drawn as a green forward-facing arrow.</summary>
        Entry,

        /// <summary>Zone exit point. Drawn as a green backward-facing arrow.</summary>
        Exit,

        /// <summary>Explicitly named position from the design document. Drawn as a white sphere.</summary>
        Named,

        /// <summary>Indexed step on a staircase. Drawn as a small grey dot.</summary>
        Step,

        /// <summary>Wall surface reference for relative placement. Drawn as a blue tick mark.</summary>
        WallRef,

        /// <summary>Connection point to an adjacent zone. Drawn as a cyan diamond.</summary>
        Connection,
    }

    public enum GameplayMarkerType
    {
        /// <summary>Enemy spawn location. Holds health, damage, detection, and behaviour config.</summary>
        EnemySpawn,

        /// <summary>Trap or hazard. Holds trigger conditions, alert radius, and fixability config.</summary>
        Trap,

        /// <summary>Loot container or item drop. Holds container type and loot table config.</summary>
        LootContainer,

        /// <summary>Interactive object — lever, valve, door, dialogue trigger, etc.</summary>
        Interaction,

        /// <summary>Ambient environmental dressing hint. Used by the zone-dressing step at bake time.</summary>
        AmbientDressing,

        /// <summary>Freeform custom marker. SemanticTags and ConfigJson are fully author-defined.</summary>
        Custom,
    }

    public enum BakeState
    {
        /// <summary>Marker has not been through the resolve step. No prefab candidate chosen.</summary>
        Pending,

        /// <summary>A prefab candidate has been chosen but not yet instantiated.</summary>
        Resolved,

        /// <summary>Prefab has been instantiated at this marker. The marker is fulfilled.</summary>
        Baked,
    }
}
