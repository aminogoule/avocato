import json, sys, numpy as np
from PIL import Image

SRC, OUT = sys.argv[1], sys.argv[2]
W = H = 2048
data = json.load(open(SRC))["tris"]
img = np.zeros((H, W, 3), np.float32)
mask = np.zeros((H, W), bool)

# палитра по референсу: тёмная кожура, светлая жёлто-зелёная мякоть
SKIN   = np.array([ 58,  80,  42], np.float32)
SKIN_D = np.array([ 38,  55,  30], np.float32)
FLESH  = np.array([206, 220, 138], np.float32)
FLESH_E= np.array([182, 200, 112], np.float32)
PIT    = np.array([108,  60,  32], np.float32)
PIT_L  = np.array([142,  86,  46], np.float32)
INK    = np.array([ 34,  42,  28], np.float32)   # линии мордочки
NOSE   = np.array([214, 130, 140], np.float32)

EYE_Y, EYE_X, EYE_R = 0.470, 0.112, 0.045        # кошачьи глазки — небольшие
HL_R   = 0.013
NOSE_Y = 0.432
# усы: (начало X, конец X, Y начала, Y конца) для правой половины, левая зеркалится
WHISKERS = [(0.165, 0.360, 0.470, 0.505),
            (0.170, 0.375, 0.440, 0.442),
            (0.165, 0.355, 0.412, 0.382)]
WHISK_W = 0.0075

def colorize(p, is_pit):
    """p: (N,3) позиции на модели. Возвращает (N,3) цвет."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    out = np.empty_like(p)
    if is_pit:
        t = np.clip((z - 0.0) / 0.22, 0, 1)[:, None]
        return PIT * (1 - t) + PIT_L * t
    # перёд модели — плоская грань среза на z ~ 0.02, всё остальное кожура
    front = np.clip((z + 0.04) / 0.06, 0, 1)[:, None]
    r = np.sqrt(x ** 2 + (y - 0.05) ** 2)
    edge = np.clip((r - 0.30) / 0.35, 0, 1)[:, None]
    flesh = FLESH * (1 - edge) + FLESH_E * edge
    updown = np.clip((y + 0.7) / 1.8, 0, 1)[:, None]
    skin = SKIN_D * (1 - updown) + SKIN * updown
    out = skin * (1 - front) + flesh * front
    # мордочка рисуется только на передней грани среза
    on_face = (z > -0.02).astype(np.float32)

    def stroke(a):
        """подмешать чернильный цвет с мягким краем"""
        aa = (a * on_face)[:, None]
        return aa

    for sx in (1.0, -1.0):
        dx = x - sx * EYE_X
        dy = y - EYE_Y
        d = np.sqrt(dx ** 2 + (dy * 0.80) ** 2)          # глаз чуть вытянут по горизонтали
        a = np.clip((EYE_R - d) / 0.006, 0, 1)
        out = out * (1 - stroke(a)) + INK * stroke(a)
        hd = np.sqrt((dx - 0.012) ** 2 + (dy - 0.013) ** 2)
        ha = np.clip((HL_R - hd) / 0.004, 0, 1)
        out = out * (1 - stroke(ha)) + np.array([250, 250, 244], np.float32) * stroke(ha)

    # носик-сердечко между глазами
    nd = np.sqrt((x / 0.030) ** 2 + ((y - NOSE_Y) / 0.019) ** 2)
    na = np.clip((1.0 - nd) / 0.35, 0, 1)
    out = out * (1 - stroke(na)) + NOSE * stroke(na)

    # усы
    for x0, x1, y0, y1 in WHISKERS:
        for sx in (1.0, -1.0):
            ax, ay = sx * x0, y0
            bx, by = sx * x1, y1
            vx, vy = bx - ax, by - ay
            L2 = vx * vx + vy * vy
            t = np.clip(((x - ax) * vx + (y - ay) * vy) / L2, 0, 1)
            dd = np.sqrt((x - (ax + t * vx)) ** 2 + (y - (ay + t * vy)) ** 2)
            wa = np.clip((WHISK_W - dd) / 0.0035, 0, 1)
            out = out * (1 - stroke(wa)) + INK * stroke(wa)
    return out

for tri in data:
    v = np.array(tri["v"], np.float32)      # 3 x (x,y,z,u,v)
    uv = v[:, 3:5].copy()
    px = uv[:, 0] * (W - 1)
    py = (1.0 - uv[:, 1]) * (H - 1)
    x0, x1 = int(np.floor(px.min())) - 1, int(np.ceil(px.max())) + 1
    y0, y1 = int(np.floor(py.min())) - 1, int(np.ceil(py.max())) + 1
    x0 = max(x0, 0); y0 = max(y0, 0); x1 = min(x1, W - 1); y1 = min(y1, H - 1)
    if x1 < x0 or y1 < y0: continue
    xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
    xs = xs.ravel().astype(np.float32); ys = ys.ravel().astype(np.float32)
    d = (py[1] - py[2]) * (px[0] - px[2]) + (px[2] - px[1]) * (py[0] - py[2])
    if abs(d) < 1e-9: continue
    l0 = ((py[1] - py[2]) * (xs - px[2]) + (px[2] - px[1]) * (ys - py[2])) / d
    l1 = ((py[2] - py[0]) * (xs - px[2]) + (px[0] - px[2]) * (ys - py[2])) / d
    l2 = 1 - l0 - l1
    eps = -0.35 / max(abs(d) ** 0.5, 1)     # небольшой запас, чтобы не было дыр по швам
    inside = (l0 > eps) & (l1 > eps) & (l2 > eps)
    if not inside.any(): continue
    l0, l1, l2 = l0[inside], l1[inside], l2[inside]
    p = (l0[:, None] * v[0, :3] + l1[:, None] * v[1, :3] + l2[:, None] * v[2, :3])
    col = colorize(p, bool(tri["pit"]))
    yi = ys[inside].astype(int); xi = xs[inside].astype(int)
    img[yi, xi] = col
    mask[yi, xi] = True

print("покрыто текселей: %d из %d (%.1f%%)" % (mask.sum(), W * H, 100 * mask.sum() / (W * H)))
# расширяем края островов, иначе на швах вылезает фон
for _ in range(6):
    m = mask.copy()
    acc = np.zeros_like(img); cnt = np.zeros((H, W), np.float32)
    for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
        s = np.roll(np.where(m[:,:,None], img, 0), (dy,dx), (0,1))
        c = np.roll(m.astype(np.float32), (dy,dx), (0,1))
        acc += s; cnt += c
    fill = (~m) & (cnt > 0)
    img[fill] = acc[fill] / cnt[fill][:, None]
    mask |= fill
Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(OUT)
print("текстура записана:", OUT)
