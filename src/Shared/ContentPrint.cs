using System;

namespace MC.Shared;

// Me = cheap "did game content change?" check (docs/modding/framework.md, "Third-party content"). Mods that build a
// list from game data (which items are towers, which pieces are lights...) keep the print they built from and compare
// it with Now(): other print = content came or went (new world, other mods' items registered late, server-synced
// item editors) = build again. Print = ObjectDB and ZNetScene objects + item, recipe and prefab counts. Five field
// reads, no allocation: fine once per frame. Blind to in-place edits that keep every count (a mod that change an
// item's data, not the item list): read such data late (after every ObjectDB.Awake postfix), not from a print.
internal readonly struct ContentPrint : IEquatable<ContentPrint>
{
    internal readonly ObjectDB Db;
    internal readonly ZNetScene Scene;
    internal readonly int Items;
    internal readonly int Recipes;
    internal readonly int Prefabs;

    private ContentPrint(ObjectDB db, ZNetScene scene, int items, int recipes, int prefabs)
    {
        Db = db;
        Scene = scene;
        Items = items;
        Recipes = recipes;
        Prefabs = prefabs;
    }

    // Print of this moment (no ObjectDB / ZNetScene = -1 counts).
    internal static ContentPrint Now()
    {
        var db = ObjectDB.instance;
        var scene = ZNetScene.instance;
        return new ContentPrint(db, scene,
            db != null && db.m_items != null ? db.m_items.Count : -1,
            db != null && db.m_recipes != null ? db.m_recipes.Count : -1,
            scene != null ? scene.m_namedPrefabs.Count : -1);
    }

    // Unity objects: reference compare only (no == operator: a destroyed ObjectDB is still "other").
    public bool Equals(ContentPrint o) =>
        ReferenceEquals(Db, o.Db) && ReferenceEquals(Scene, o.Scene) && Items == o.Items && Recipes == o.Recipes
        && Prefabs == o.Prefabs;

    public override bool Equals(object obj) => obj is ContentPrint p && Equals(p);

    public override int GetHashCode() => Items ^ (Recipes << 10) ^ (Prefabs << 20);

    public override string ToString() => $"items {Items}, recipes {Recipes}, prefabs {Prefabs}";
}
