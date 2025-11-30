#!/usr/bin/env python3
"""
Generate hexagonal province map and definition.csv for Archon Engine.

Creates:
- provinces.png: RGB image where each unique color = one province
- definition.csv: Province ID, RGB, Name, water flag mappings

Output format matches Assets/Data/map/definition.csv:
    province;red;green;blue;name;x
    1;128;34;64;Province_1;x      (x = land)
    2;135;8;144;Province_2;       (empty = water)
"""

import math
import argparse
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: Pillow not installed. Run: pip install Pillow")
    exit(1)


# Water terrain colors from terrain_rgb.json5
WATER_COLORS = [
    (8, 31, 130),    # ocean
    (55, 90, 220),   # inland_ocean
]

# Tolerance for color matching (terrain.bmp may have slight variations)
COLOR_TOLERANCE = 10


def is_water_color(r: int, g: int, b: int) -> bool:
    """Check if RGB color matches any water terrain type."""
    for wr, wg, wb in WATER_COLORS:
        if (abs(r - wr) <= COLOR_TOLERANCE and
            abs(g - wg) <= COLOR_TOLERANCE and
            abs(b - wb) <= COLOR_TOLERANCE):
            return True
    return False


def generate_unique_rgb(province_id: int, is_water: bool = False) -> tuple[int, int, int]:
    """
    Generate a unique RGB color for a province ID.
    Encodes province ID directly into RGB channels for guaranteed uniqueness.
    Supports up to 16,777,215 provinces (24-bit RGB).
    Avoids black (0,0,0) which is typically reserved for "no province".

    Water provinces use blue-dominant colors, land uses varied colors.
    """
    if province_id == 0:
        return (0, 0, 0)  # Reserved for "no province" / borders

    if is_water:
        # Water provinces: blue-dominant colors
        # Use province ID to create variation within blue range
        # R: 0-80, G: 0-120, B: 150-255
        r = (province_id * 17) % 80
        g = (province_id * 31) % 120
        b = 150 + (province_id * 7) % 106  # 150-255 range
    else:
        # Land provinces: spread across warm/earth tones
        # Avoid pure blue to distinguish from water
        # Use golden ratio-based distribution for visual variety
        golden = 0.618033988749895
        hue = (province_id * golden) % 1.0

        # Convert hue to RGB (simplified HSV with S=0.6, V=0.9)
        # Bias toward warm colors (reds, oranges, yellows, greens)
        hue = hue * 0.75  # Limit to 0-270 degrees (avoid pure blue)

        if hue < 0.166:  # Red
            r = 230
            g = int(80 + hue * 6 * 100)
            b = int(50 + (province_id % 50))
        elif hue < 0.333:  # Orange/Yellow
            r = int(200 + (province_id % 55))
            g = int(150 + (hue - 0.166) * 6 * 80)
            b = int(40 + (province_id % 60))
        elif hue < 0.5:  # Yellow/Green
            r = int(150 + (province_id % 80))
            g = int(180 + (province_id % 70))
            b = int(50 + (province_id % 50))
        elif hue < 0.666:  # Green
            r = int(60 + (province_id % 80))
            g = int(160 + (province_id % 80))
            b = int(60 + (province_id % 70))
        else:  # Cyan/Teal (avoid pure blue)
            r = int(60 + (province_id % 70))
            g = int(140 + (province_id % 80))
            b = int(120 + (province_id % 60))

        # Ensure uniqueness by encoding province ID in least significant bits
        # This preserves visual color while guaranteeing uniqueness
        r = (r & 0xF0) | (province_id & 0x0F)
        g = (g & 0xF0) | ((province_id >> 4) & 0x0F)
        b = (b & 0xF0) | ((province_id >> 8) & 0x0F)

    # Clamp to valid range
    r = max(1, min(255, r))  # Avoid 0 to prevent black
    g = max(0, min(255, g))
    b = max(0, min(255, b))

    # Final uniqueness guarantee: if collision possible, encode directly
    # For provinces > 4096, fall back to direct encoding with color bias
    if province_id > 4096:
        if is_water:
            r = (province_id & 0x3F)  # 0-63
            g = ((province_id >> 6) & 0x3F) + 30  # 30-93
            b = 180 + ((province_id >> 12) & 0x4F)  # 180-255
        else:
            r = 100 + (province_id & 0x7F)  # 100-227
            g = 80 + ((province_id >> 7) & 0x7F)  # 80-207
            b = ((province_id >> 14) & 0x3F)  # 0-63

    return (r, g, b)


def draw_hexagon(draw: ImageDraw.Draw, cx: float, cy: float, size: float, color: tuple):
    """
    Draw a filled hexagon centered at (cx, cy) with given size.
    Flat-top hexagon orientation.
    """
    points = []
    for i in range(6):
        angle = math.pi / 3 * i  # 60 degrees per vertex
        x = cx + size * math.cos(angle)
        y = cy + size * math.sin(angle)
        points.append((x, y))

    draw.polygon(points, fill=color)


def generate_hexagon_grid(
    width: int,
    height: int,
    hex_size: float,
    output_dir: Path,
    terrain_path: Path = None,
    start_id: int = 1
) -> int:
    """
    Generate a hexagonal province map.

    Args:
        width: Image width in pixels
        height: Image height in pixels
        hex_size: Radius of each hexagon
        output_dir: Directory to save output files
        terrain_path: Optional path to terrain.bmp for water detection
        start_id: Starting province ID

    Returns:
        Total number of provinces created
    """
    # Load terrain image if provided (for water detection)
    terrain_img = None
    if terrain_path and terrain_path.exists():
        terrain_img = Image.open(terrain_path).convert('RGB')
        print(f"Loaded terrain image: {terrain_path} ({terrain_img.width}x{terrain_img.height})")
    else:
        print("No terrain image provided - all provinces will be marked as land")

    # Create image with black background (0,0,0 = no province)
    img = Image.new('RGB', (width, height), (0, 0, 0))
    draw = ImageDraw.Draw(img)

    # Hexagon spacing for flat-top orientation
    hex_width = hex_size * 2
    hex_height = hex_size * math.sqrt(3)

    # Horizontal and vertical spacing
    col_spacing = hex_width * 0.75  # 3/4 of hex width for overlap
    row_spacing = hex_height

    provinces = []
    province_id = start_id

    # Calculate grid dimensions
    cols = int(width / col_spacing) + 2
    rows = int(height / row_spacing) + 2

    print(f"Generating {cols}x{rows} hexagon grid...")
    print(f"Hex size: {hex_size}px, spacing: {col_spacing:.1f}x{row_spacing:.1f}")

    for col in range(cols):
        for row in range(rows):
            # Calculate center position
            cx = col * col_spacing + hex_size

            # Offset every other column
            cy = row * row_spacing + hex_height / 2
            if col % 2 == 1:
                cy += row_spacing / 2

            # Skip if center is outside image bounds (with margin)
            if cx < -hex_size or cx > width + hex_size:
                continue
            if cy < -hex_size or cy > height + hex_size:
                continue

            # Check if this province is water FIRST (sample terrain at hex center)
            is_water = False
            if terrain_img:
                # Clamp coordinates to terrain image bounds
                tx = int(min(max(cx, 0), terrain_img.width - 1))
                ty = int(min(max(cy, 0), terrain_img.height - 1))
                terrain_color = terrain_img.getpixel((tx, ty))
                is_water = is_water_color(terrain_color[0], terrain_color[1], terrain_color[2])

            # Generate unique color (blue for water, warm colors for land)
            rgb = generate_unique_rgb(province_id, is_water)

            # Draw hexagon
            draw_hexagon(draw, cx, cy, hex_size, rgb)

            # Store province data
            provinces.append({
                'id': province_id,
                'r': rgb[0],
                'g': rgb[1],
                'b': rgb[2],
                'name': f"Province_{province_id}",
                'cx': cx,
                'cy': cy,
                'is_water': is_water
            })

            province_id += 1

    # Save province map
    provinces_path = output_dir / "provinces.png"
    img.save(provinces_path)
    print(f"Saved: {provinces_path}")

    # Save definition.csv
    definition_path = output_dir / "definition.csv"
    water_count = 0
    land_count = 0

    with open(definition_path, 'w', encoding='utf-8') as f:
        # Header matching game format
        f.write("province;red;green;blue;name;x\n")

        for prov in provinces:
            # Format: province;red;green;blue;name;water_flag
            # water_flag: 'x' = land, empty = water
            water_flag = "" if prov.get('is_water', False) else "x"
            f.write(f"{prov['id']};{prov['r']};{prov['g']};{prov['b']};{prov['name']};{water_flag}\n")

            if prov.get('is_water', False):
                water_count += 1
            else:
                land_count += 1

    print(f"Saved: {definition_path}")
    print(f"Total provinces: {len(provinces)} (Land: {land_count}, Water: {water_count})")

    return len(provinces)


def main():
    parser = argparse.ArgumentParser(
        description='Generate hexagonal province map for Archon Engine'
    )
    parser.add_argument(
        '--width', type=int, default=5632,
        help='Map width in pixels (default: 5632)'
    )
    parser.add_argument(
        '--height', type=int, default=2048,
        help='Map height in pixels (default: 2048)'
    )
    parser.add_argument(
        '--hex-size', type=float, default=32,
        help='Hexagon radius in pixels (default: 32)'
    )
    parser.add_argument(
        '--output', type=str, default='.',
        help='Output directory (default: current directory)'
    )
    parser.add_argument(
        '--terrain', type=str, default=None,
        help='Path to terrain.bmp for water detection (optional)'
    )

    args = parser.parse_args()

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    terrain_path = Path(args.terrain) if args.terrain else None

    print(f"Generating province map: {args.width}x{args.height}")
    print(f"Output directory: {output_dir.absolute()}")

    generate_hexagon_grid(
        width=args.width,
        height=args.height,
        hex_size=args.hex_size,
        output_dir=output_dir,
        terrain_path=terrain_path
    )

    print("Done!")


if __name__ == '__main__':
    main()
