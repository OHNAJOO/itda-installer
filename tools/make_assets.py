"""assets/의 원본 이미지로 아이콘·설치 마법사 이미지를 만든다 (결과: assets/generated/, 커밋 대상).

    python tools/make_assets.py        # Pillow 필요: pip install pillow

build.ps1은 이 스크립트를 부르지 않고 커밋된 결과물만 쓴다. 원본 이미지를 바꿨을 때만 다시 실행한다.
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
OUT = ASSETS / "generated"

# logo.png(457x457)에서 글자를 뺀 심볼 영역. 작은 아이콘에서 '잇다' 글자는 뭉개지므로 심볼만 쓴다.
SYMBOL_BOX = (120, 50, 346, 268)
ICON_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# Inno Setup 6 권장 크기 (100% / 150% / 200% DPI). iss에서 쉼표로 나열하면 DPI에 맞춰 고른다.
WIZARD_LARGE = {100: (164, 314), 150: (246, 459), 200: (328, 604)}
WIZARD_SMALL = {100: (55, 55), 150: (83, 80), 200: (110, 106)}
WIZARD_BG = (238, 247, 243)  # 마스코트 초록 톤을 옅게


def transparent_background(im: Image.Image, gap: int = 5) -> Image.Image:
    """바깥 흰 배경만 투명하게 만든다. 심볼 안쪽의 흰 문서는 윤곽 틈으로 바깥과 이어져 있어서,
    색 있는 부분을 gap만큼 넓혀 틈을 막은 뒤 모서리부터 배경을 칠하고 그 배경을 다시 gap만큼 넓혀 뺀다."""
    im = im.convert("RGBA")
    colored = im.convert("L").point(lambda v: 255 if v < 235 else 0)
    closed = colored.filter(ImageFilter.MaxFilter(gap))
    for corner in [(0, 0), (im.width - 1, 0), (0, im.height - 1), (im.width - 1, im.height - 1)]:
        ImageDraw.floodfill(closed, corner, 128, thresh=0)
    background = closed.point(lambda v: 255 if v == 128 else 0).filter(ImageFilter.MaxFilter(gap))
    opaque = ImageChops.lighter(colored, ImageChops.invert(background))
    # 가장자리를 부드럽게: 원래 알파와 곱한 뒤 살짝 흐림
    im.putalpha(ImageChops.multiply(im.getchannel("A"), opaque.filter(ImageFilter.GaussianBlur(0.7))))
    return im


def square(im: Image.Image, size: int, pad: float = 0.04) -> Image.Image:
    """투명한 정사각형 캔버스 가운데에 비율을 유지해 넣는다."""
    im = im.crop(im.getbbox())
    inner = int(size * (1 - 2 * pad))
    scale = inner / max(im.size)
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(im, ((size - im.width) // 2, (size - im.height) // 2), im)
    return canvas


def on_background(im: Image.Image, size: tuple[int, int], bg, fill: float, anchor_y: float) -> Image.Image:
    """배경색 위에 이미지를 가로 fill 비율로 얹는다. BMP는 알파가 없으므로 24비트로 합성."""
    im = im.crop(im.getbbox())
    w = int(size[0] * fill)
    h = round(im.height * w / im.width)
    if h > size[1] * 0.9:
        h = int(size[1] * 0.9)
        w = round(im.width * h / im.height)
    im = im.resize((w, h), Image.LANCZOS)
    canvas = Image.new("RGB", size, bg)
    canvas.paste(im, ((size[0] - w) // 2, int((size[1] - h) * anchor_y)), im)
    return canvas


def main() -> None:
    OUT.mkdir(exist_ok=True)

    symbol = transparent_background(Image.open(ASSETS / "logo.png").crop(SYMBOL_BOX))
    icon = square(symbol, 256)
    icon.save(OUT / "itda.ico", sizes=[(s, s) for s in ICON_SIZES])
    icon.save(OUT / "itda-256.png")

    mascot = Image.open(ASSETS / "mascot.png").convert("RGBA")
    # 거의 투명한 가장자리 픽셀 때문에 bbox가 커지지 않도록 알파를 약간 정리
    mascot.putalpha(mascot.getchannel("A").point(lambda a: 0 if a < 8 else a))
    for pct, size in WIZARD_LARGE.items():
        on_background(mascot, size, WIZARD_BG, fill=0.9, anchor_y=0.55).save(OUT / f"wizard-large-{pct}.bmp")
    for pct, size in WIZARD_SMALL.items():
        on_background(symbol, size, (255, 255, 255), fill=0.86, anchor_y=0.5).save(OUT / f"wizard-small-{pct}.bmp")

    print(f"완료: {OUT}")


if __name__ == "__main__":
    main()
