namespace MC.UX.CheatedTagRemoverMod;

// What me found in one saved item blob.
internal enum BlobResult
{
    Clean,   // no tag inside (or format that has no tag at all): nothing to do
    Tagged,  // layout 109, read to the last byte, at least one tag
    Unknown, // format me not sure about: never touched, only counted
}

// Me = byte work on saved item records. Pure: no game object, no ObjectDB, no ZDO. Game keep items in two blob kinds
// (ItemDrop.ItemData.Save = one record; Inventory.Save = int version, ushort count, records; ItemDrop.SaveToZDO =
// byte version, one record). Record of layout 109 end with one flags byte, bit 0 = cheated. Me walk blob like the
// game read it, and clear only that bit in a COPY: every other byte stay (custom data of other mods, crafter, unknown
// item prefabs, other bits of the flags byte). Me never go through Inventory.Load + Save: that drop items with
// unknown prefab, clamp stacks and tag again above 10000 damage.
// Only layout 109 get changed. Other layout that can hold a tag, or blob that not end where the walk end = Unknown.
internal static class ItemBlobs
{
    // Version.Item.ChunksNCheats: only layout game write today, only one me change.
    internal const int Layout = (int)global::Version.Item.ChunksNCheats;
    // Version.Item.Smaller: same record without flags byte = no tag possible.
    private const int LayoutNoFlags = (int)global::Version.Item.Smaller;
    // Version.Item.AbandonedDN: has a tag (old inventory layout end each item with a bool). Never seen, never touched.
    private const int LayoutOldTagged = (int)global::Version.Item.AbandonedDN;
    private const byte CheatedBit = 1;

    // Container blob (ZDO key "items"). tagged = records with the bit. cleaned = copy without the bits, only when
    // patch and Tagged.
    internal static BlobResult Inventory(byte[] src, bool patch, out byte[] cleaned, out int tagged)
    {
        cleaned = null;
        tagged = 0;
        if (src == null || src.Length == 0)
        {
            return BlobResult.Clean;
        }
        if (src.Length < 6)
        {
            return BlobResult.Unknown;
        }
        var version = src[0] | (src[1] << 8) | (src[2] << 16) | (src[3] << 24);
        if (version != Layout)
        {
            // 108 and all below 107 hold no tag. 107 and anything newer than me know: hands off.
            return version == LayoutNoFlags || (version >= 0 && version < LayoutOldTagged)
                ? BlobResult.Clean
                : BlobResult.Unknown;
        }
        var count = src[4] | (src[5] << 8);
        var pos = 6;
        byte[] copy = null;
        for (var i = 0; i < count; i++)
        {
            var flags = FlagsIndex(src, pos);
            if (flags < 0)
            {
                tagged = 0;
                return BlobResult.Unknown;
            }
            if ((src[flags] & CheatedBit) != 0)
            {
                tagged++;
                if (patch)
                {
                    copy ??= (byte[])src.Clone();
                    copy[flags] &= unchecked((byte)~CheatedBit);
                }
            }
            pos = flags + 1;
        }
        if (pos != src.Length)
        {
            // Bytes left after last record = not the layout me know.
            tagged = 0;
            return BlobResult.Unknown;
        }
        if (tagged == 0)
        {
            return BlobResult.Clean;
        }
        cleaned = copy;
        return BlobResult.Tagged;
    }

    // One item blob (ZDO key "itemData" or "<slot>_itemData": ground item, fish, item stand, armor stand slot).
    internal static BlobResult Single(byte[] src, bool patch, out byte[] cleaned)
    {
        cleaned = null;
        // Game ignore arrays this short too (ItemDrop.LoadFromZDO).
        if (src == null || src.Length <= 2)
        {
            return BlobResult.Clean;
        }
        int version = src[0];
        if (version != Layout)
        {
            return version == LayoutOldTagged || version > Layout ? BlobResult.Unknown : BlobResult.Clean;
        }
        var last = src.Length - 1;
        if ((src[last] & CheatedBit) == 0)
        {
            return BlobResult.Clean;
        }
        // Bit set: only trust it when the record really end on that byte.
        if (FlagsIndex(src, 1) != last)
        {
            return BlobResult.Unknown;
        }
        if (patch)
        {
            cleaned = (byte[])src.Clone();
            cleaned[last] &= unchecked((byte)~CheatedBit);
        }
        return BlobResult.Tagged;
    }

    // Record start at pos. Return index of its flags byte (record end there), -1 = record run out of the array.
    // Same order as ItemDrop.ItemData.Load.
    private static int FlagsIndex(byte[] b, int pos)
    {
        var len = b.Length;
        // int durability, byte x, byte y, byte world level, byte field flags
        if (pos < 0 || pos + 8 > len)
        {
            return -1;
        }
        var fields = b[pos + 7];
        var p = pos + 8;
        if ((fields & 4) != 0)
        {
            p += 2; // ushort quality
        }
        if ((fields & 8) != 0)
        {
            p += 2; // ushort stack
        }
        if ((fields & 16) != 0)
        {
            p += 4; // int variant
        }
        if ((fields & 32) != 0)
        {
            p += 8; // long crafter id
            p = SkipString(b, p); // crafter name
            if (p < 0)
            {
                return -1;
            }
        }
        if ((fields & 64) != 0)
        {
            p += 4; // int prefab hash
        }
        if ((fields & 128) != 0)
        {
            // Count like ZPackage.ReadNumItems: one byte, or two with high bit on the first.
            if (p >= len)
            {
                return -1;
            }
            int n = b[p++];
            if ((n & 0x80) != 0)
            {
                if (p >= len)
                {
                    return -1;
                }
                n = ((n & 0x7F) << 8) | b[p++];
            }
            for (var i = 0; i < n * 2; i++)
            {
                p = SkipString(b, p); // key, then value
                if (p < 0)
                {
                    return -1;
                }
            }
        }
        return p < len ? p : -1;
    }

    // BinaryWriter string: byte count as 7-bit groups (low first), then the bytes. Return index after it, -1 = bad.
    private static int SkipString(byte[] b, int p)
    {
        var len = b.Length;
        var size = 0;
        var shift = 0;
        while (true)
        {
            if (p < 0 || p >= len || shift > 28)
            {
                return -1;
            }
            int part = b[p++];
            size |= (part & 0x7F) << shift;
            if ((part & 0x80) == 0)
            {
                break;
            }
            shift += 7;
        }
        if (size < 0 || size > len - p)
        {
            return -1;
        }
        return p + size;
    }
}
