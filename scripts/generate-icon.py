"""Render the editable icon's simple SVG shapes to PNG and Windows ICO (requires Pillow)."""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parent.parent / "src/ShellX.Desktop/Assets"
source = ET.parse(assets / "ShellX.svg").getroot()
scale = 4
image = Image.new("RGBA", (512 * scale, 512 * scale))
draw = ImageDraw.Draw(image)
for element in source:
    tag = element.tag.rsplit("}", 1)[-1]
    if tag == "rect":
        x, y, width, height = (float(element.attrib[key]) * scale for key in ("x", "y", "width", "height"))
        draw.rounded_rectangle((x, y, x + width, y + height), radius=float(element.attrib["rx"]) * scale,
                               fill=element.attrib["fill"], outline=element.attrib["stroke"], width=int(element.attrib["stroke-width"]) * scale)
    elif tag == "polyline":
        points = [tuple(float(value) * scale for value in point.split(",")) for point in element.attrib["points"].split()]
        stroke = element.attrib["stroke"]
        width = int(element.attrib["stroke-width"]) * scale
        draw.line(points, fill=stroke, width=width, joint="curve")
        for x, y in points if element.attrib.get("stroke-linejoin") == "round" else (points[0], points[-1]):
            radius = width / 2
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=stroke)

image = image.resize((512, 512), Image.Resampling.LANCZOS)
image.save(assets / "ShellX.png")
image.save(assets / "ShellX.ico", sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print(assets / "ShellX.ico")
