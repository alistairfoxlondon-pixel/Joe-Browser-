"""Reads the cmap of a TTF/OTF and reports which codepoints are actually mapped.

Used once to confirm that a Material Icons codepoint really exists in the bundled
font before it is referenced from Icons.cs - a wrong codepoint renders as a blank
box, which is invisible in code review and obvious to a user.
"""
import struct
import sys


def read_cmap(path):
    with open(path, "rb") as f:
        data = f.read()

    num_tables = struct.unpack_from(">H", data, 4)[0]
    cmap_off = None
    for i in range(num_tables):
        off = 12 + i * 16
        tag = data[off:off + 4]
        if tag == b"cmap":
            cmap_off = struct.unpack_from(">I", data, off + 8)[0]
            break
    if cmap_off is None:
        raise SystemExit("no cmap table")

    n_sub = struct.unpack_from(">H", data, cmap_off + 2)[0]
    best = None
    for i in range(n_sub):
        rec = cmap_off + 4 + i * 8
        plat, enc, sub = struct.unpack_from(">HHI", data, rec)
        # prefer a full unicode subtable
        if (plat, enc) in ((3, 10), (0, 4), (0, 6)) or (plat, enc) == (3, 1):
            best = cmap_off + sub
            if (plat, enc) in ((3, 10), (0, 4), (0, 6)):
                break
    if best is None:
        raise SystemExit("no unicode cmap subtable")

    fmt = struct.unpack_from(">H", data, best)[0]
    mapped = set()

    if fmt == 4:
        segx2 = struct.unpack_from(">H", data, best + 6)[0]
        seg = segx2 // 2
        ends = struct.unpack_from(">%dH" % seg, data, best + 14)
        starts = struct.unpack_from(">%dH" % seg, data, best + 16 + segx2)
        deltas = struct.unpack_from(">%dh" % seg, data, best + 16 + segx2 * 2)
        range_off_pos = best + 16 + segx2 * 3
        range_offs = struct.unpack_from(">%dH" % seg, data, range_off_pos)
        for i in range(seg):
            if starts[i] == 0xFFFF:
                continue
            for cp in range(starts[i], ends[i] + 1):
                if range_offs[i] == 0:
                    g = (cp + deltas[i]) & 0xFFFF
                else:
                    gp = range_off_pos + i * 2 + range_offs[i] + (cp - starts[i]) * 2
                    if gp + 2 > len(data):
                        continue
                    g = struct.unpack_from(">H", data, gp)[0]
                    if g:
                        g = (g + deltas[i]) & 0xFFFF
                if g:
                    mapped.add(cp)
    elif fmt == 12:
        n_groups = struct.unpack_from(">I", data, best + 12)[0]
        for i in range(n_groups):
            s, e, gid = struct.unpack_from(">III", data, best + 16 + i * 12)
            for cp in range(s, e + 1):
                mapped.add(cp)
    else:
        raise SystemExit("unsupported cmap format %d" % fmt)

    return mapped


if __name__ == "__main__":
    font = sys.argv[1]
    cps = [int(a, 16) for a in sys.argv[2:]]
    mapped = read_cmap(font)
    print("font maps %d codepoints" % len(mapped))
    for cp in cps:
        print("%s  U+%04X" % ("ok     " if cp in mapped else "MISSING", cp))
