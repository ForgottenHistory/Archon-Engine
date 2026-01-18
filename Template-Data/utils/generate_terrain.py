#!/usr/bin/env python3
"""
Generate terrain map for Archon Engine.

Creates:
- terrain.bmp: RGB image where each hexagon province = one terrain type

Reads terrain definitions from terrain.json5 (single source of truth).
RGB colors must match terrain.json5 exactly for the shader to work.
"""

import math
import argparse
import random
import re
import json
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: Pillow not installed. Run: pip install Pillow")
    exit(1)


# Terrain colors loaded from terrain.json5
TERRAIN_COLORS = {}  # name -> (r, g, b)


def load_terrain_colors(terrain_json5_path: Path) -> dict:
    """
    Load terrain colors from terrain.json5 (single source of truth).
    Returns dict of terrain_name -> (r, g, b)
    """
    if not terrain_json5_path.exists():
        print(f"ERROR: terrain.json5 not found at {terrain_json5_path}")
        print("Using default colors...")
        return get_default_terrain_colors()

    content = terrain_json5_path.read_text(encoding='utf-8')

    # Strip comments (// style)
    content = re.sub(r'//.*$', '', content, flags=re.MULTILINE)

    # Convert JSON5 unquoted keys to quoted keys for standard JSON parsing
    # Match word characters followed by colon (but not inside strings)
    content = re.sub(r'(\s)(\w+)(\s*:)', r'\1"\2"\3', content)

    # Remove trailing commas before } or ]
    content = re.sub(r',(\s*[}\]])', r'\1', content)

    try:
        data = json.loads(content)
    except json.JSONDecodeError as e:
        print(f"ERROR: Failed to parse terrain.json5: {e}")
        print("Using default colors...")
        return get_default_terrain_colors()

    categories = data.get('categories', {})
    colors = {}

    for name, props in categories.items():
        if 'color' in props and len(props['color']) >= 3:
            colors[name] = tuple(props['color'][:3])

    print(f"Loaded {len(colors)} terrain colors from terrain.json5")
    return colors


def get_default_terrain_colors() -> dict:
    """Fallback default colors if terrain.json5 not found."""
    return {
        "ocean":          (8, 31, 130),
        "inland_ocean":   (55, 90, 220),
        "grasslands":     (86, 124, 27),
        "plains":         (200, 214, 107),
        "hills":          (0, 86, 6),
        "highlands":      (23, 23, 23),
        "mountain":       (65, 42, 17),
        "desert_mountain": (112, 74, 31),
        "snow":           (255, 255, 255),
        "desert":         (206, 169, 99),
        "coastal_desert": (203, 191, 103),
        "savannah":       (180, 160, 80),
        "forest":         (42, 55, 22),
        "jungle":         (0, 100, 0),
        "marsh":          (75, 147, 174),
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
    """Get RGB color for a terrain type from loaded colors."""
    global TERRAIN_COLORS
    if terrain_name in TERRAIN_COLORS:
        return TERRAIN_COLORS[terrain_name]
    # Fallback to grasslands or first available
    if "grasslands" in TERRAIN_COLORS:
        return TERRAIN_COLORS["grasslands"]
    if TERRAIN_COLORS:
        return next(iter(TERRAIN_COLORS.values()))
    return (86, 124, 27)  # Default grasslands


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

    For vertical continent: top = cold/snow, middle = temperate, bottom = hot/desert
    """
    # Normalize coordinates for noise
    nx = cx / width * 8.0
    ny = cy / img_height * 8.0

    # Get noise values for biome variation
    moisture = (noise.noise2d(nx * 2, ny * 2) + 1) / 2

    # Latitude-based temperature: top of map = cold (0), bottom = hot (1)
    # Use y position directly for clear north-south gradient
    base_temperature = cy / img_height  # 0 at top, 1 at bottom

    # Add some noise variation but keep the gradient dominant
    temp_noise = (noise.noise2d(nx * 1.5 + 100, ny * 1.5 + 100) + 1) / 2
    temperature = base_temperature * 0.8 + temp_noise * 0.2

    # Ocean
    if height < sea_level - 5:
        return "ocean"

    # Coastal areas (just above sea level)
    if height < sea_level + 5:
        if moisture > 0.65:
            return "marsh"
        if temperature > 0.75:
            return "coastal_desert"
        return "plains"

    # Inland water (lakes in wet areas)
    if height < sea_level + 8 and moisture > 0.85:
        return "inland_ocean"

    # Normalized land height
    land_height = (height - sea_level) / (255 - sea_level)

    # High mountains and snow (elevation > 0.7)
    if land_height > 0.7:
        if temperature < 0.35:
            return "snow"
        if temperature > 0.7 and moisture < 0.35:
            return "desert_mountain"
        return "mountain"

    # Mountains (elevation 0.5-0.7)
    if land_height > 0.5:
        if temperature < 0.25:
            return "snow"
        if temperature > 0.65 and moisture < 0.35:
            return "desert_mountain"
        return "mountain"

    # Highlands/hills (elevation 0.3-0.5)
    if land_height > 0.3:
        if temperature < 0.3:
            if moisture > 0.5:
                return "forest"
            return "highlands"
        if temperature > 0.7:
            if moisture < 0.3:
                return "desert"
            return "savannah"
        if moisture > 0.65:
            return "forest"
        if moisture < 0.35:
            return "highlands"
        return "hills"

    # Mid-elevation (0.15-0.3)
    if land_height > 0.15:
        if temperature < 0.25:
            return "forest"  # Cold forest (taiga-like)
        if temperature > 0.75:
            if moisture < 0.3:
                return "desert"
            if moisture > 0.7:
                return "jungle"
            return "savannah"
        if moisture > 0.7:
            if temperature > 0.6:
                return "jungle"
            return "forest"
        if moisture > 0.5:
            return "grasslands"
        return "plains"

    # Low elevation (< 0.15)
    if temperature < 0.3:
        if moisture > 0.6:
            return "marsh"
        return "grasslands"
    if temperature > 0.7:
        if moisture < 0.35:
            return "coastal_desert"
        if moisture > 0.7:
            return "jungle"
        return "savannah"
    if moisture > 0.65:
        return "marsh"
    if moisture > 0.45:
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

    # Save terrain map as PNG (simple RGB, no palette confusion)
    terrain_path = output_dir / "terrain.png"
    terrain_img.save(terrain_path, "PNG")
    print(f"Saved: {terrain_path}")

    # Print distribution
    print(f"\nTerrain distribution ({hex_count} hexagons):")
    for terrain, count in sorted(terrain_counts.items(), key=lambda x: -x[1]):
        pct = count / hex_count * 100
        print(f"  {terrain}: {count} ({pct:.1f}%)")




def main():
    global TERRAIN_COLORS

    parser = argparse.ArgumentParser(
        description='Generate terrain map for Archon Engine'
    )
    parser.add_argument(
        '--heightmap', type=str, default='heightmap.png',
        help='Path to heightmap.png (default: heightmap.png in output dir)'
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

    # Load terrain colors from terrain.json5 (single source of truth)
    terrain_json5_path = output_dir / "terrain.json5"
    TERRAIN_COLORS = load_terrain_colors(terrain_json5_path)

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

    print("\nDone!")


if __name__ == '__main__':
    main()
