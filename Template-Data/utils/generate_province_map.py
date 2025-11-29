#!/usr/bin/env python3
"""
Generate hexagonal province map and definition.csv for Archon Engine.

Creates:
- provinces.png: RGB image where each unique color = one province
- definition.csv: Province ID, RGB, Name mappings

Output format matches Assets/Data/map/definition.csv:
    province;red;green;blue;name;x
    1;128;34;64;Province_1;x
"""

import math
import argparse
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    print("ERROR: Pillow not installed. Run: pip install Pillow")
    exit(1)


def generate_unique_rgb(province_id: int) -> tuple[int, int, int]:
    """
    Generate a unique RGB color for a province ID.
    Uses a deterministic algorithm that spreads colors across RGB space.
    Avoids black (0,0,0) which is typically reserved for "no province".
    """
    if province_id == 0:
        return (0, 0, 0)  # Reserved for "no province" / borders

    # Spread across RGB cube using bit manipulation for good distribution
    # This ensures visually distinct colors for adjacent provinces
    r = ((province_id * 67) % 255) + 1  # +1 to avoid 0
    g = ((province_id * 131) % 255) + 1
    b = ((province_id * 199) % 255) + 1

    # Ensure we don't accidentally hit black
    if r == 0 and g == 0 and b == 0:
        r = 1

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
    start_id: int = 1
) -> int:
    """
    Generate a hexagonal province map.

    Args:
        width: Image width in pixels
        height: Image height in pixels
        hex_size: Radius of each hexagon
        output_dir: Directory to save output files
        start_id: Starting province ID

    Returns:
        Total number of provinces created
    """
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

            # Generate unique color
            rgb = generate_unique_rgb(province_id)

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
                'cy': cy
            })

            province_id += 1

    # Save province map
    provinces_path = output_dir / "provinces.png"
    img.save(provinces_path)
    print(f"Saved: {provinces_path}")

    # Save definition.csv
    definition_path = output_dir / "definition.csv"
    with open(definition_path, 'w', encoding='utf-8') as f:
        # Header matching game format
        f.write("province;red;green;blue;x;x\n")

        for prov in provinces:
            # Format: province;red;green;blue;name;x
            # The 'x' at the end is the water flag (x = land, empty or specific marker = water)
            f.write(f"{prov['id']};{prov['r']};{prov['g']};{prov['b']};{prov['name']};x\n")

    print(f"Saved: {definition_path}")
    print(f"Total provinces: {len(provinces)}")

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

    args = parser.parse_args()

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Generating province map: {args.width}x{args.height}")
    print(f"Output directory: {output_dir.absolute()}")

    generate_hexagon_grid(
        width=args.width,
        height=args.height,
        hex_size=args.hex_size,
        output_dir=output_dir
    )

    print("Done!")


if __name__ == '__main__':
    main()
