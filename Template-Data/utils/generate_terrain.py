#!/usr/bin/env python3
"""
Generate terrain map for Archon Engine.

Creates:
- terrain.bmp: RGB image where each hexagon province = one terrain type
- terrain_rgb.json5: Terrain type definitions with RGB colors

Terrain is assigned per-hexagon based on heightmap, matching province boundaries.
RGB colors must match terrain_rgb.json5 exactly for the shader to work.
"""

import math
import argparse
import random
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: Pillow not installed. Run: pip install Pillow")
    exit(1)


# Terrain type definitions matching terrain_rgb.json5
# Format: name -> (index, rgb, type_category)
TERRAIN_TYPES = {
    "grasslands":         (0,  (86, 124, 27),   "grasslands"),
    "hills":              (1,  (0, 86, 6),      "hills"),
    "desert_mountain":    (2,  (112, 74, 31),   "mountain"),
    "desert":             (3,  (206, 169, 99),  "desert"),
    "plains":             (4,  (200, 214, 107), "grasslands"),
    "mountain":           (5,  (65, 42, 17),    "mountain"),
    "marsh":              (6,  (75, 147, 174),  "marsh"),
    "forest":             (7,  (42, 55, 22),    "forest"),
    "ocean":              (8,  (8, 31, 130),    "ocean"),
    "snow":               (9,  (255, 255, 255), "mountain"),
    "inland_ocean":       (10, (55, 90, 220),   "inland_ocean"),
    "coastal_desert":     (11, (203, 191, 103), "coastal_desert"),
    "savannah":           (12, (180, 160, 80),  "savannah"),
    "highlands":          (13, (23, 23, 23),    "highlands"),
    "jungle":             (14, (254, 254, 254), "jungle"),
}


class PerlinNoise:
    """Simple Perlin noise for terrain variation."""

    def __init__(self, seed: int = 0):
        self.seed = seed
        random.seed(seed)
        self.p = list(range(256))
        random.shuffle(self.p)
        self.p = self.p + self.p

    def _fade(self, t: float) -> float:
        return t * t * t * (t * (t * 6 - 15) + 10)

    def _lerp(self, a: float, b: float, t: float) -> float:
        return a + t * (b - a)

    def _grad(self, hash_val: int, x: float, y: float) -> float:
        h = hash_val & 3
        if h == 0:
            return x + y
        elif h == 1:
            return -x + y
        elif h == 2:
            return x - y
        else:
            return -x - y

    def noise2d(self, x: float, y: float) -> float:
        xi = int(math.floor(x)) & 255
        yi = int(math.floor(y)) & 255
        xf = x - math.floor(x)
        yf = y - math.floor(y)
        u = self._fade(xf)
        v = self._fade(yf)
        aa = self.p[self.p[xi] + yi]
        ab = self.p[self.p[xi] + yi + 1]
        ba = self.p[self.p[xi + 1] + yi]
        bb = self.p[self.p[xi + 1] + yi + 1]
        x1 = self._lerp(self._grad(aa, xf, yf), self._grad(ba, xf - 1, yf), u)
        x2 = self._lerp(self._grad(ab, xf, yf - 1), self._grad(bb, xf - 1, yf - 1), u)
        return self._lerp(x1, x2, v)


def get_terrain_rgb(terrain_name: str) -> tuple[int, int, int]:
    """Get RGB color for a terrain type."""
    if terrain_name in TERRAIN_TYPES:
        return TERRAIN_TYPES[terrain_name][1]
    return TERRAIN_TYPES["grasslands"][1]


def determine_terrain_for_hex(
    height: int,
    cx: float,
    cy: float,
    width: int,
    img_height: int,
    noise: PerlinNoise,
    sea_level: int = 94
) -> str:
    """
    Determine terrain type for a hexagon based on height at center.
    Valid terrain types: grasslands, hills, desert_mountain, desert, plains,
    mountain, marsh, forest, ocean, snow, inland_ocean, coastal_desert,
    savannah, highlands, jungle
    """
    # Normalize coordinates for noise
    nx = cx / width * 8.0
    ny = cy / img_height * 8.0

    # Get noise values for biome variation
    moisture = (noise.noise2d(nx * 2, ny * 2) + 1) / 2
    temperature = (noise.noise2d(nx * 1.5 + 100, ny * 1.5 + 100) + 1) / 2

    # Latitude effect
    latitude_factor = abs(cy / img_height - 0.5) * 2
    temperature = temperature * (1 - latitude_factor * 0.5)

    # Ocean
    if height < sea_level - 5:
        return "ocean"

    # Coastline (use marsh or plains instead of removed "coastline")
    if height < sea_level + 3:
        if moisture > 0.6:
            return "marsh"
        return "plains"

    # Inland water
    if height < sea_level + 5 and moisture > 0.8:
        return "inland_ocean"

    # Normalized land height
    land_height = (height - sea_level) / (255 - sea_level)

    # Snow peaks
    if land_height > 0.8:
        return "snow"

    # Mountains
    if land_height > 0.6:
        if temperature < 0.3:
            return "snow"
        if moisture < 0.3:
            return "desert_mountain"
        return "mountain"

    # Highlands/hills (use highlands instead of removed "dry_highlands")
    if land_height > 0.4:
        if moisture < 0.4:
            return "highlands"
        if moisture > 0.7:
            return "forest"
        return "hills"

    # Mid-elevation (use forest instead of removed "woods")
    if land_height > 0.2:
        if temperature > 0.7 and moisture < 0.3:
            return "desert"
        if temperature > 0.6 and moisture < 0.4:
            return "savannah"
        if moisture > 0.75:
            if temperature > 0.7:
                return "jungle"
            return "forest"
        if moisture > 0.5:
            return "forest"
        return "plains"

    # Low elevation
    if temperature > 0.7 and moisture < 0.35:
        return "coastal_desert"
    if temperature > 0.6 and moisture < 0.4:
        return "savannah"
    if moisture > 0.7:
        return "marsh"
    if moisture > 0.5:
        return "grasslands"

    return "plains"


def draw_hexagon(draw: ImageDraw.Draw, cx: float, cy: float, size: float, color: tuple):
    """Draw a filled hexagon centered at (cx, cy)."""
    points = []
    for i in range(6):
        angle = math.pi / 3 * i
        x = cx + size * math.cos(angle)
        y = cy + size * math.sin(angle)
        points.append((x, y))
    draw.polygon(points, fill=color)


def generate_terrain_map(
    heightmap_path: Path,
    output_dir: Path,
    hex_size: float = 32,
    seed: int = 42,
    sea_level: int = 94
) -> None:
    """
    Generate terrain map with hexagonal provinces.
    """
    print(f"Loading heightmap: {heightmap_path}")

    heightmap = Image.open(heightmap_path).convert('RGB')
    width, height = heightmap.size
    height_pixels = heightmap.load()

    print(f"Heightmap size: {width}x{height}")
    print(f"Hex size: {hex_size}, Sea level: {sea_level}")

    # Create terrain image with ocean as default
    ocean_rgb = get_terrain_rgb("ocean")
    terrain_img = Image.new('RGB', (width, height), ocean_rgb)
    draw = ImageDraw.Draw(terrain_img)

    noise = PerlinNoise(seed)

    # Hexagon spacing (must match province map generator!)
    hex_width = hex_size * 2
    hex_height = hex_size * math.sqrt(3)
    col_spacing = hex_width * 0.75
    row_spacing = hex_height

    cols = int(width / col_spacing) + 2
    rows = int(height / row_spacing) + 2

    terrain_counts = {}
    hex_count = 0

    print(f"Generating {cols}x{rows} hexagon terrain grid...")

    for col in range(cols):
        for row in range(rows):
            # Calculate center (must match province generator!)
            cx = col * col_spacing + hex_size
            cy = row * row_spacing + hex_height / 2
            if col % 2 == 1:
                cy += row_spacing / 2

            # Skip if outside bounds
            if cx < -hex_size or cx > width + hex_size:
                continue
            if cy < -hex_size or cy > height + hex_size:
                continue

            # Sample height at hex center
            sample_x = max(0, min(width - 1, int(cx)))
            sample_y = max(0, min(height - 1, int(cy)))
            h = height_pixels[sample_x, sample_y][0]

            # Determine terrain
            terrain_name = determine_terrain_for_hex(
                h, cx, cy, width, height, noise, sea_level
            )

            # Draw hexagon with terrain color
            rgb = get_terrain_rgb(terrain_name)
            draw_hexagon(draw, cx, cy, hex_size, rgb)

            terrain_counts[terrain_name] = terrain_counts.get(terrain_name, 0) + 1
            hex_count += 1

    # Save terrain map
    terrain_path = output_dir / "terrain.bmp"
    terrain_img.save(terrain_path, "BMP")
    print(f"Saved: {terrain_path}")

    # Print distribution
    print(f"\nTerrain distribution ({hex_count} hexagons):")
    for terrain, count in sorted(terrain_counts.items(), key=lambda x: -x[1]):
        pct = count / hex_count * 100
        print(f"  {terrain}: {count} ({pct:.1f}%)")


def generate_terrain_json5(output_dir: Path) -> None:
    """Generate terrain_rgb.json5 with terrain definitions."""
    json5_path = output_dir / "terrain_rgb.json5"

    lines = [
        "// RGB → Terrain Type Mapping",
        "// These RGB colors are extracted from terrain.bmp's palette",
        "// Paint terrain.bmp with these exact RGB values in any editor",
        "",
        "{"
    ]

    entries = []
    for name, (idx, rgb, type_cat) in TERRAIN_TYPES.items():
        entries.append(f'  {name}: {{ type: "{type_cat}", color: [{rgb[0]}, {rgb[1]}, {rgb[2]}] }}')

    lines.append(",\n".join(entries))
    lines.append("}")

    with open(json5_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))
        f.write("\n")

    print(f"Saved: {json5_path}")


def main():
    parser = argparse.ArgumentParser(
        description='Generate terrain map for Archon Engine'
    )
    parser.add_argument(
        '--heightmap', type=str, default='heightmap.bmp',
        help='Path to heightmap.bmp (default: heightmap.bmp in output dir)'
    )
    parser.add_argument(
        '--output', type=str, default='.',
        help='Output directory (default: current directory)'
    )
    parser.add_argument(
        '--hex-size', type=float, default=32,
        help='Hexagon radius - must match province map! (default: 32)'
    )
    parser.add_argument(
        '--seed', type=int, default=42,
        help='Random seed for biome noise (default: 42)'
    )
    parser.add_argument(
        '--sea-level', type=int, default=94,
        help='Sea level grayscale value (default: 94)'
    )

    args = parser.parse_args()

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    heightmap_path = Path(args.heightmap)
    if not heightmap_path.is_absolute():
        heightmap_path = output_dir / heightmap_path

    if not heightmap_path.exists():
        print(f"ERROR: Heightmap not found: {heightmap_path}")
        print("Run generate_heightmap.py first!")
        exit(1)

    generate_terrain_map(
        heightmap_path=heightmap_path,
        output_dir=output_dir,
        hex_size=args.hex_size,
        seed=args.seed,
        sea_level=args.sea_level
    )

    generate_terrain_json5(output_dir)

    print("\nDone!")


if __name__ == '__main__':
    main()
