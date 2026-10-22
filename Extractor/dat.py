"""datc64 reader driven by poe-tool-dev/dat-schema."""
import json
import struct

NULL_ROW = 0xFEFEFEFEFEFEFEFE
MAGIC = b"\xbb" * 8

SIZES = {
    "bool": 1, "i16": 2, "u16": 2, "i32": 4, "u32": 4, "f32": 4, "enumrow": 4,
    "i64": 8, "u64": 8, "string": 8, "row": 8, "foreignrow": 16, "array": 16,
}


def load_schema(path):
    s = json.load(open(path, encoding="utf8"))
    tables = {}
    for t in s["tables"]:
        if t.get("validFor", 1) & 1:  # 1 = PoE1
            tables[t["name"].lower()] = t
    return tables


def col_size(c):
    return 16 if c["array"] else SIZES[c["type"]]


class Dat:
    def __init__(self, path, table):
        b = open(path, "rb").read()
        self.n = struct.unpack_from("<I", b, 0)[0]
        self.var = b.index(MAGIC, 4)
        self.width = (self.var - 4) // self.n if self.n else 0
        self.b = b
        self.cols = table["columns"]
        self.schema_width = sum(col_size(c) for c in self.cols)
        names, offs, o = [], [], 0
        for i, c in enumerate(self.cols):
            names.append(c["name"] or f"_{i}")
            offs.append(o)
            o += col_size(c)
        self.names, self.offs = names, offs

    def _str(self, off):
        p = self.var + off
        end = p
        while self.b[end:end + 4] != b"\0\0\0\0" or (end - p) % 2:
            end += 2
        return self.b[p:end].decode("utf-16-le", "replace")

    def _scalar(self, t, p):
        b = self.b
        if t == "bool": return b[p] != 0
        if t == "i16": return struct.unpack_from("<h", b, p)[0]
        if t == "u16": return struct.unpack_from("<H", b, p)[0]
        if t in ("i32", "enumrow"): return struct.unpack_from("<i", b, p)[0]
        if t == "u32": return struct.unpack_from("<I", b, p)[0]
        if t == "f32": return struct.unpack_from("<f", b, p)[0]
        if t == "i64": return struct.unpack_from("<q", b, p)[0]
        if t == "u64": return struct.unpack_from("<Q", b, p)[0]
        if t == "string": return self._str(struct.unpack_from("<Q", b, p)[0])
        if t in ("row", "foreignrow"):
            v = struct.unpack_from("<Q", b, p)[0]
            return None if v == NULL_ROW else v
        return None

    def get(self, row, name):
        i = self.names.index(name)
        c = self.cols[i]
        p = 4 + row * self.width + self.offs[i]
        if c["array"]:
            cnt, off = struct.unpack_from("<QQ", self.b, p)
            t = c["type"]
            if t == "array" or cnt > 100000:
                return []
            sz = 16 if t == "foreignrow" else SIZES[t]
            base = self.var + off
            return [self._scalar(t, base + k * sz) for k in range(cnt)]
        return self._scalar(c["type"], p)

    def row(self, r, fields=None):
        return {n: self.get(r, n) for n in (fields or self.names)}

    def rows(self, fields=None):
        return [self.row(r, fields) for r in range(self.n)]
