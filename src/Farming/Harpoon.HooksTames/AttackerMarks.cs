namespace MC.Farming.HarpoonHooksTamesMod;

// Vanilla Character.RPC_Damage (on game that own creature) mark every player that hit it: ZDO bool
// "<Attackers hash as text><player name>", attacker count, kill modifier. Mark never cleared: creature die later
// from anything = marked player get kill credit. Hook is not attack, so me snapshot the three keys before vanilla
// and put them back after (only keys that changed: real earlier hit keep its mark).
// Me made only for zero-damage harpoon hits on tames (never hot path).
internal sealed class AttackerMarks
{
    private const int KeyCount = 3;

    private readonly int[] _keys = new int[KeyCount];
    private readonly bool[] _had = new bool[KeyCount];
    private readonly int[] _values = new int[KeyCount];

    private AttackerMarks()
    {
    }

    internal static AttackerMarks Take(ZDO zdo, string playerName)
    {
        if (zdo == null)
        {
            return null;
        }

        // Same expression as vanilla (int var + string): key = decimal text of the hash + name, hashed again by ZDO.
        int attackers = ZDOVars.s_attackers;
        string text = attackers + playerName;

        var marks = new AttackerMarks();
        marks.Record(0, zdo, text.GetStableHashCode());
        marks.Record(1, zdo, ZDOVars.s_attackers);
        marks.Record(2, zdo, ZDOVars.s_modifiers);
        return marks;
    }

    // Put back what vanilla changed. True = something put back.
    // Key vanilla added: RemoveInt (no data revision bump, but vanilla Set in same call already bumped it,
    // and nothing sent to peers between, so peers never see the mark).
    internal bool Restore(ZDO zdo)
    {
        if (zdo == null)
        {
            return false;
        }

        var changed = false;
        for (var i = 0; i < KeyCount; i++)
        {
            var has = zdo.GetInt(_keys[i], out var now);
            if (has == _had[i] && (!has || now == _values[i]))
            {
                continue;
            }

            if (_had[i])
            {
                zdo.Set(_keys[i], _values[i]);
            }
            else
            {
                zdo.RemoveInt(_keys[i]);
            }
            changed = true;
        }
        return changed;
    }

    private void Record(int index, ZDO zdo, int key)
    {
        _keys[index] = key;
        _had[index] = zdo.GetInt(key, out _values[index]);
    }
}
