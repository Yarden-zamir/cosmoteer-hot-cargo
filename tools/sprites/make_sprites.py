# /// script
# requires-python = ">=3.12"
# dependencies = ["pillow>=11", "numpy>=2"]
# ///
"""
Draws the sprites of the autonomous ship parts (spec 1057): Computer, targeted reactor and Funnel.
Run: uv run tools/sprites/make_sprites.py   (writes mod/autonomy/sprites/<part>/*.png)

Conventions of the vanilla terran parts (read from the game's part sprites, no art copied):
- 64 px per tile. Layers: floor, walls, roof (paint mask: green 0,230,0 takes the ship paint), roof normals (flat = 128,128,255),
  doodads (operational / off), lights (additive), blueprints (blue fill and outline), editor icon.
- Damage levels _33 and _66: the same layer with dark burned patches.
Every shape is drawn at SS x the size and scaled down, for clean edges. Random detail uses a fixed seed, so a rerun gives the same files.
"""
import math
import pathlib
import random

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

TILE = 64
SS = 4
OUT = pathlib.Path(__file__).resolve().parents[2] / "mod" / "autonomy" / "sprites"

GREEN = (0, 230, 0, 255)
CLEAR = (0, 0, 0, 0)
METAL = (146, 146, 148)
WALL = (66, 60, 56)
WALL_LIGHT = (118, 110, 100)
DARK = (34, 34, 38)


def canvas(w_tiles, h_tiles, fill=CLEAR):
    return Image.new("RGBA", (w_tiles * TILE * SS, h_tiles * TILE * SS), fill)


def done(img):
    """Scales a supersampled image to the final size."""
    return img.resize((img.width // SS, img.height // SS), Image.LANCZOS)


def s(v):
    """A length in final pixels, in supersampled pixels."""
    return int(round(v * SS))


def noise(img, amount, seed):
    rng = np.random.default_rng(seed)
    a = np.asarray(img).astype(np.int16)
    n = rng.integers(-amount, amount + 1, size=a.shape[:2])[..., None]
    a[..., :3] = np.clip(a[..., :3] + n, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def bevel_rect(d, box, fill, light, dark, width):
    """A plate with a light top-left and a dark bottom-right edge."""
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=fill)
    d.line([(x0, y1), (x0, y0), (x1, y0)], fill=light, width=width)
    d.line([(x1, y0), (x1, y1), (x0, y1)], fill=dark, width=width)


def metal_floor(w, h, seed):
    """Grey deck plates with seams and rivets, one plate per tile."""
    img = canvas(w, h, METAL + (255,))
    d = ImageDraw.Draw(img)
    for ty in range(h):
        for tx in range(w):
            x0, y0 = s(tx * TILE), s(ty * TILE)
            bevel_rect(d, (x0 + s(1), y0 + s(1), x0 + s(TILE - 2), y0 + s(TILE - 2)), METAL + (255,),
                       (170, 170, 172, 255), (112, 112, 116, 255), s(1))
            for cx, cy in ((5, 5), (TILE - 6, 5), (5, TILE - 6), (TILE - 6, TILE - 6)):
                r = s(1.4)
                d.ellipse((x0 + s(cx) - r, y0 + s(cy) - r, x0 + s(cx) + r, y0 + s(cy) + r), fill=(120, 120, 124, 255))
    return noise(img, 4, seed)


def walls(w, h):
    """The part's own wall frame: a beveled metal band around the edge, clear inside, with notched corners."""
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    W, H = img.size
    t = s(6)
    d.rectangle((0, 0, W - 1, H - 1), outline=WALL + (255,), width=t)
    d.rectangle((s(1), s(1), W - 1 - s(1), H - 1 - s(1)), outline=WALL_LIGHT + (255,), width=s(1))
    d.rectangle((t, t, W - 1 - t, H - 1 - t), outline=(28, 26, 24, 255), width=s(1))
    for cx, cy in ((0, 0), (W, 0), (0, H), (W, H)):
        k = s(10)
        d.rectangle((cx - k if cx else 0, cy - k if cy else 0, cx + k if not cx else W, cy + k if not cy else H), fill=(52, 48, 44, 255))
        d.rectangle((cx - s(4) if cx else s(2), cy - s(4) if cy else s(2), (cx - s(2)) if cx else s(4), (cy - s(2)) if cy else s(4)),
                    fill=WALL_LIGHT + (255,))
    return img


def damaged(img, level, seed, keep_alpha=True):
    """Dark burned patches over a layer. level 1 = _33, 2 = _66."""
    rng = random.Random(seed * 10 + level)
    burn = Image.new("L", img.size, 0)
    d = ImageDraw.Draw(burn)
    count = 5 * level
    for _ in range(count):
        cx, cy = rng.uniform(0, img.width), rng.uniform(0, img.height)
        # The layer is at its final size here, so the sizes are plain pixels.
        r = rng.uniform(4, 7 + 4 * level)
        pts = [(cx + r * rng.uniform(0.6, 1.2) * math.cos(a), cy + r * rng.uniform(0.6, 1.2) * math.sin(a))
               for a in np.linspace(0, 2 * math.pi, 9, endpoint=False)]
        d.polygon(pts, fill=255)
    burn = burn.filter(ImageFilter.GaussianBlur(1.5))
    scorch = Image.new("RGBA", img.size, (38, 36, 36, 255))
    out = Image.composite(scorch, img, burn)
    if keep_alpha:
        out.putalpha(img.getchannel("A"))
    return noise(out, 6, seed + level)


def normals_from_height(height, strength=2.0):
    """A normal map (flat = 128,128,255) from a height map (L), like the vanilla roof normals."""
    h = np.asarray(height).astype(np.float32) / 255.0
    dx = np.zeros_like(h)
    dy = np.zeros_like(h)
    dx[:, 1:-1] = (h[:, 2:] - h[:, :-2]) * 0.5
    dy[1:-1, :] = (h[2:, :] - h[:-2, :]) * 0.5
    nx, ny, nz = -dx * strength * SS, -dy * strength * SS, np.ones_like(h)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([(nx / length) * 127 + 128, (ny / length) * 127 + 128, (nz / length) * 127 + 128], axis=-1)
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


class Roof:
    """A roof: paint (green) with grey detail plates, and a height map for its normals."""

    def __init__(self, w, h):
        self.color = canvas(w, h, GREEN)
        self.height = Image.new("L", self.color.size, 90)
        self.c = ImageDraw.Draw(self.color)
        self.hd = ImageDraw.Draw(self.height)

    def plate(self, box, level=150):
        """A raised grey plate."""
        bevel_rect(self.c, box, (128, 128, 132, 255), (172, 172, 176, 255), (84, 84, 90, 255), s(1.5))
        self.hd.rectangle(box, fill=level)

    def ring(self, box, width, level=150):
        self.c.ellipse(box, outline=(124, 124, 128, 255), width=width)
        self.hd.ellipse(box, outline=level, width=width)

    def disc(self, box, level=170, fill=(136, 136, 140, 255)):
        self.c.ellipse(box, fill=fill, outline=(90, 90, 96, 255), width=s(1.5))
        self.hd.ellipse(box, fill=level)

    def groove(self, xy, width):
        """A paint-coloured seam, cut into the roof."""
        self.c.line(xy, fill=(0, 170, 0, 255), width=width)
        self.hd.line(xy, fill=40, width=width)

    def images(self):
        return done(self.color), done(normals_from_height(self.height.filter(ImageFilter.GaussianBlur(s(0.8)))))


def blueprint(shape):
    """The blueprint layer from a shape mask (L, final size): blue fill, bright outline of the inside, frame at the edge."""
    w, h = shape.size
    img = Image.new("RGBA", (w, h), (0, 108, 204, 76))
    edge = shape.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 40 else 0)
    glow = edge.filter(ImageFilter.GaussianBlur(1.5))
    img = Image.composite(Image.new("RGBA", (w, h), (90, 170, 255, 200)), img, glow)
    img = Image.composite(Image.new("RGBA", (w, h), (150, 210, 255, 255)), img, edge)
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, w - 1, h - 1), outline=(0, 0, 175, 255), width=4)
    return img


def save(part, name, img):
    path = OUT / part / f"{name}.png"
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, optimize=True)


def save_damage_levels(part, name, img, seed, keep_alpha=True):
    save(part, name, img)
    save(part, f"{name}_33", damaged(img, 1, seed, keep_alpha))
    save(part, f"{name}_66", damaged(img, 2, seed, keep_alpha))


# ---- Computer: 2x1. A server cabinet: a dark rack bay with module slots, a screen strip at the front (top edge), status lights.

def computer():
    W, H = 2, 1
    part = "computer"
    floor = metal_floor(W, H, 11)
    d = ImageDraw.Draw(floor)
    bay = (s(8), s(8), s(120), s(56))
    bevel_rect(d, bay, DARK + (255,), (20, 20, 24, 255), (78, 78, 84, 255), s(1.5))
    for i in range(6):
        x0 = s(14 + i * 17.5)
        bevel_rect(d, (x0, s(22), x0 + s(14), s(52)), (58, 60, 66, 255), (92, 94, 100, 255), (30, 30, 34, 255), s(1))
        for k in range(4):
            y = s(26 + k * 6.5)
            d.line([(x0 + s(2), y), (x0 + s(12), y)], fill=(40, 40, 46, 255), width=s(1))
    # The screen strip at the front: the side that the Computer serves.
    d.rectangle((s(14), s(11), s(114), s(18)), fill=(16, 26, 40, 255))
    save_damage_levels(part, "floor", done(floor), 11)

    for frame in range(2):
        on = Image.new("RGBA", floor.size, CLEAR)
        od = ImageDraw.Draw(on)
        rng = random.Random(100 + frame)
        od.rectangle((s(15), s(12), s(113), s(17)), fill=(60, 150, 230, 255))
        for k in range(10):
            x = s(18 + k * 9.5)
            od.line([(x, s(13)), (x + s(rng.uniform(2, 7)), s(13))], fill=(170, 225, 255, 255), width=s(1))
        for i in range(6):
            x0 = s(14 + i * 17.5)
            for k in range(3):
                lit = rng.random() < 0.6
                color = (90, 255, 140, 255) if lit else (40, 70, 50, 255)
                r = s(1.2)
                cx, cy = x0 + s(3 + k * 4), s(48)
                od.ellipse((cx - r, cy - r, cx + r, cy + r), fill=color)
        save(part, f"doodads_on{frame + 1}", done(on))
    off = Image.new("RGBA", floor.size, CLEAR)
    ImageDraw.Draw(off).rectangle((s(15), s(12), s(113), s(17)), fill=(30, 40, 52, 255))
    save(part, "doodads_off", done(off))

    lights = Image.new("RGBA", floor.size, (0, 0, 0, 255))
    ld = ImageDraw.Draw(lights)
    ld.rectangle((s(14), s(9), s(114), s(22)), fill=(40, 110, 200, 255))
    save(part, "lights", done(lights.filter(ImageFilter.GaussianBlur(s(4)))))

    save_damage_levels(part, "walls", done(walls(W, H)), 12)

    roof = Roof(W, H)
    roof.plate((s(10), s(10), s(56), s(54)))
    roof.plate((s(72), s(10), s(118), s(54)))
    for i in range(5):
        y = s(16 + i * 8)
        roof.groove([(s(16), y), (s(50), y)], s(2))
        roof.groove([(s(78), y), (s(112), y)], s(2))
    roof.disc((s(58), s(26), s(70), s(38)))
    color, nrm = roof.images()
    save_damage_levels(part, "roof", color, 13, keep_alpha=False)
    save(part, "roof_normals", nrm)
    save(part, "roof_normals_33", nrm)
    save(part, "roof_normals_66", nrm)

    shape = Image.new("L", (W * TILE, H * TILE), 0)
    sd = ImageDraw.Draw(shape)
    sd.rectangle((8, 8, 120, 56), outline=255, width=1)
    for i in range(6):
        x0 = 14 + int(i * 17.5)
        sd.rectangle((x0, 22, x0 + 14, 52), outline=255, width=1)
    sd.rectangle((14, 11, 114, 18), outline=255, width=1)
    save(part, "blueprints", blueprint(shape))

    icon = Image.alpha_composite(done(floor), Image.open(OUT / part / "doodads_on1.png"))
    icon = Image.alpha_composite(icon, Image.open(OUT / part / "walls.png"))
    save(part, "icon", icon)


# ---- Targeted reactor: 2x2. A sealed core with four emitters, one toward each side: it powers only the parts that touch it.

def targeted_reactor():
    W, H = 2, 2
    part = "targeted_reactor"
    floor = metal_floor(W, H, 21)
    d = ImageDraw.Draw(floor)
    c = s(64)
    for angle in range(4):
        # Conduits from the core to the middle of each side.
        a = angle * math.pi / 2
        ex, ey = c + math.cos(a) * s(58), c + math.sin(a) * s(58)
        d.line([(c, c), (ex, ey)], fill=(70, 72, 80, 255), width=s(12))
        d.line([(c, c), (ex, ey)], fill=(104, 108, 118, 255), width=s(6))
        bx, by = c + math.cos(a) * s(50), c + math.sin(a) * s(50)
        d.ellipse((bx - s(7), by - s(7), bx + s(7), by + s(7)), fill=(60, 62, 70, 255), outline=(150, 152, 160, 255), width=s(1.5))
    d.ellipse((c - s(30), c - s(30), c + s(30), c + s(30)), fill=(56, 58, 66, 255), outline=(160, 162, 170, 255), width=s(2))
    d.ellipse((c - s(22), c - s(22), c + s(22), c + s(22)), fill=(36, 38, 46, 255), outline=(96, 98, 108, 255), width=s(2))
    save_damage_levels(part, "floor", done(floor), 21)

    for frame in range(4):
        on = Image.new("RGBA", floor.size, CLEAR)
        od = ImageDraw.Draw(on)
        r = s(12 + 2 * math.sin(frame * math.pi / 2))
        od.ellipse((c - r, c - r, c + r, c + r), fill=(120, 220, 255, 255))
        od.ellipse((c - s(6), c - s(6), c + s(6), c + s(6)), fill=(230, 250, 255, 255))
        for angle in range(4):
            a = angle * math.pi / 2
            bx, by = c + math.cos(a) * s(50), c + math.sin(a) * s(50)
            lit = (frame + angle) % 4 != 0
            od.ellipse((bx - s(3.5), by - s(3.5), bx + s(3.5), by + s(3.5)), fill=(140, 230, 255, 255) if lit else (40, 80, 100, 255))
        save(part, f"doodads_on{frame + 1}", done(on))
    off = Image.new("RGBA", floor.size, CLEAR)
    ImageDraw.Draw(off).ellipse((c - s(12), c - s(12), c + s(12), c + s(12)), fill=(40, 60, 72, 255))
    save(part, "doodads_off", done(off))

    lights = Image.new("RGBA", floor.size, (0, 0, 0, 255))
    ld = ImageDraw.Draw(lights)
    ld.ellipse((c - s(34), c - s(34), c + s(34), c + s(34)), fill=(40, 140, 220, 255))
    for angle in range(4):
        a = angle * math.pi / 2
        bx, by = c + math.cos(a) * s(52), c + math.sin(a) * s(52)
        ld.ellipse((bx - s(12), by - s(12), bx + s(12), by + s(12)), fill=(30, 110, 190, 255))
    save(part, "lights", done(lights.filter(ImageFilter.GaussianBlur(s(6)))))

    save_damage_levels(part, "walls", done(walls(W, H)), 22)

    roof = Roof(W, H)
    roof.disc((c - s(32), c - s(32), c + s(32), c + s(32)), level=190)
    roof.ring((c - s(24), c - s(24), c + s(24), c + s(24)), s(3), level=120)
    roof.disc((c - s(10), c - s(10), c + s(10), c + s(10)), level=220, fill=(150, 152, 158, 255))
    for angle in range(4):
        a = angle * math.pi / 2
        x0, y0 = c + math.cos(a) * s(34), c + math.sin(a) * s(34)
        x1, y1 = c + math.cos(a) * s(58), c + math.sin(a) * s(58)
        hw = s(6)
        box = (min(x0, x1) - (hw if angle % 2 else 0), min(y0, y1) - (0 if angle % 2 else hw),
               max(x0, x1) + (hw if angle % 2 else 0), max(y0, y1) + (0 if angle % 2 else hw))
        roof.plate(box, level=160)
    color, nrm = roof.images()
    save_damage_levels(part, "roof", color, 23, keep_alpha=False)
    save(part, "roof_normals", nrm)
    save(part, "roof_normals_33", nrm)
    save(part, "roof_normals_66", nrm)

    shape = Image.new("L", (W * TILE, H * TILE), 0)
    sd = ImageDraw.Draw(shape)
    sd.ellipse((34, 34, 94, 94), outline=255, width=1)
    sd.ellipse((42, 42, 86, 86), outline=255, width=1)
    for angle in range(4):
        a = angle * math.pi / 2
        sd.line([(64 + math.cos(a) * 30, 64 + math.sin(a) * 30), (64 + math.cos(a) * 58, 64 + math.sin(a) * 58)], fill=255, width=1)
    save(part, "blueprints", blueprint(shape))

    icon = Image.alpha_composite(done(floor), Image.open(OUT / part / "doodads_on1.png"))
    icon = Image.alpha_composite(icon, Image.open(OUT / part / "walls.png"))
    save(part, "icon", icon)


# ---- Funnel: 2x2, no roof (an open intake). A cone that narrows to a throat, intake vanes, amber hazard marks at the rim.

def funnel():
    W, H = 2, 2
    part = "funnel"
    floor = metal_floor(W, H, 31)
    d = ImageDraw.Draw(floor)
    c = s(64)
    for i, r in enumerate(range(54, 10, -4)):
        shade = int(120 - i * 7)
        d.ellipse((c - s(r), c - s(r), c + s(r), c + s(r)), fill=(shade, shade - 2, shade - 6, 255))
    d.ellipse((c - s(56), c - s(56), c + s(56), c + s(56)), outline=(176, 170, 150, 255), width=s(3))
    for k in range(16):
        # Amber and black hazard marks on the rim.
        a0 = k * 2 * math.pi / 16
        if k % 2 == 0:
            d.arc((c - s(56), c - s(56), c + s(56), c + s(56)), math.degrees(a0), math.degrees(a0) + 11,
                  fill=(222, 168, 40, 255), width=s(3))
    d.ellipse((c - s(10), c - s(10), c + s(10), c + s(10)), fill=(14, 14, 16, 255))
    save_damage_levels(part, "floor", done(floor), 31)

    vanes = Image.new("RGBA", (s(84), s(84)), CLEAR)
    vd = ImageDraw.Draw(vanes)
    vc = s(42)
    for k in range(6):
        a = k * math.pi / 3
        pts = [(vc + math.cos(a) * s(10), vc + math.sin(a) * s(10)),
               (vc + math.cos(a + 0.5) * s(40), vc + math.sin(a + 0.5) * s(40)),
               (vc + math.cos(a + 0.75) * s(38), vc + math.sin(a + 0.75) * s(38)),
               (vc + math.cos(a + 0.35) * s(10), vc + math.sin(a + 0.35) * s(10))]
        vd.polygon(pts, fill=(150, 150, 156, 255), outline=(80, 80, 86, 255))
    vd.ellipse((vc - s(9), vc - s(9), vc + s(9), vc + s(9)), fill=(110, 110, 116, 255), outline=(60, 60, 66, 255), width=s(1.5))
    save(part, "vanes", done(vanes))

    save_damage_levels(part, "walls", done(walls(W, H)), 32)

    shape = Image.new("L", (W * TILE, H * TILE), 0)
    sd = ImageDraw.Draw(shape)
    for r in (56, 40, 24, 10):
        sd.ellipse((64 - r, 64 - r, 64 + r, 64 + r), outline=255, width=1)
    save(part, "blueprints", blueprint(shape))

    icon = done(floor)
    icon.alpha_composite(Image.open(OUT / part / "vanes.png"), ((icon.width - 84) // 2, (icon.height - 84) // 2))
    icon = Image.alpha_composite(icon, Image.open(OUT / part / "walls.png"))
    save(part, "icon", icon)


if __name__ == "__main__":
    computer()
    targeted_reactor()
    funnel()
    print(f"sprites written to {OUT}")
