#!/usr/bin/env python3
"""
Generate heightmap for Archon Engine.

Creates:
- heightmap.bmp: Grayscale image where brightness = elevation
  - (0, 0, 0) = lowest point
  - (94, 94, 94) = sea level
  - (255, 255, 255) = highest point

Uses Perlin noise to generate natural-looking terrain.
Height is assigned per-hexagon to match province boundaries.
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


# Perlin noise implementation (no external dependencies)
class PerlinNoise:
    """Simple Perlin noise generator."""

    def __init__(self, seed: int = 0):
        self.seed = seed
        random.seed(seed)
        # Permutation table
        self.p = list(range(256))
        random.shuffle(self.p)
        self.p = self.p + self.p  # Duplicate for overflow

    def _fade(self, t: float) -> float:
        """Smoothstep function."""
        return t * t * t * (t * (t * 6 - 15) + 10)

    def _lerp(self, a: float, b: float, t: float) -> float:
        """Linear interpolation."""
        return a + t * (b - a)

    def _grad(self, hash_val: int, x: float, y: float) -> float:
        """Gradient function."""
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
        """
        Generate 2D Perlin noise value.
        Returns value in range [-1, 1].
        """
        # Grid cell coordinates
        xi = int(math.floor(x)) & 255
        yi = int(math.floor(y)) & 255

        # Relative position in cell
        xf = x - math.floor(x)
        yf = y - math.floor(y)

        # Fade curves
        u = self._fade(xf)
        v = self._fade(yf)

        # Hash coordinates of corners
        aa = self.p[self.p[xi] + yi]
        ab = self.p[self.p[xi] + yi + 1]
        ba = self.p[self.p[xi + 1] + yi]
        bb = self.p[self.p[xi + 1] + yi + 1]

        # Blend gradients
        x1 = self._lerp(self._grad(aa, xf, yf), self._grad(ba, xf - 1, yf), u)
        x2 = self._lerp(self._grad(ab, xf, yf - 1), self._grad(bb, xf - 1, yf - 1), u)

        return self._lerp(x1, x2, v)

    def octave_noise(self, x: float, y: float, octaves: int = 6,
                     persistence: float = 0.5, lacunarity: float = 2.0) -> float:
        """
        Generate fractal noise by combining multiple octaves.
        Returns value in range approximately [-1, 1].
        """
        total = 0.0
        amplitude = 1.0
        frequency = 1.0
        max_value = 0.0

        for _ in range(octaves):
            total += self.noise2d(x * frequency, y * frequency) * amplitude
            max_value += amplitude
            amplitude *= persistence
            frequency *= lacunarity

        return total / max_value


def draw_hexagon(draw: ImageDraw.Draw, cx: float, cy: float, size: float, color: tuple):
    """Draw a filled hexagon centered at (cx, cy)."""
    points = []
    for i in range(6):
        angle = math.pi / 3 * i
        x = cx + size * math.cos(angle)
        y = cy + size * math.sin(angle)
        points.append((x, y))
    draw.polygon(points, fill=color)


def generate_heightmap(
    width: int,
    height: int,
    output_dir: Path,
    hex_size: float = 32,
    seed: int = 42,
    sea_level: int = 94,
    scale: float = 4.0,
    octaves: int = 6,
    land_percentage: float = 0.4
) -> None:
    """
    Generate a heightmap using Perlin noise with hexagonal provinces.

    Args:
        width: Image width in pixels
        height: Image height in pixels
        output_dir: Directory to save output
        hex_size: Hexagon radius - must match province map! (default: 32)
        seed: Random seed for reproducibility
        sea_level: Grayscale value for sea level (default 94)
        scale: Noise scale (larger = bigger features)
        octaves: Number of noise octaves (more = more detail)
        land_percentage: Approximate percentage of land vs water
    """
    print(f"Generating heightmap: {width}x{height}")
    print(f"Hex size: {hex_size}, Seed: {seed}, Sea level: {sea_level}, Scale: {scale}")

    noise = PerlinNoise(seed)

    # Hexagon spacing (must match province map generator!)
    hex_width = hex_size * 2
    hex_height = hex_size * math.sqrt(3)
    col_spacing = hex_width * 0.75
    row_spacing = hex_height

    cols = int(width / col_spacing) + 2
    rows = int(height / row_spacing) + 2

    print(f"Generating {cols}x{rows} hexagon grid...")

    # First pass: calculate noise values for each hex center
    hex_values = []
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

            # Normalize coordinates to noise space
            nx = cx / width * scale
            ny = cy / height * scale

            # Generate multi-octave noise
            value = noise.octave_noise(nx, ny, octaves=octaves)

            # Add some continent-like large-scale variation
            continent_noise = noise.octave_noise(nx * 0.3, ny * 0.3, octaves=2)
            value = value * 0.7 + continent_noise * 0.3

            hex_values.append((cx, cy, value))

    # Find min/max for normalization
    all_values = [v[2] for v in hex_values]
    min_val = min(all_values)
    max_val = max(all_values)

    print(f"Noise range: [{min_val:.3f}, {max_val:.3f}]")

    # Calculate threshold for desired land percentage
    sorted_values = sorted(all_values)
    threshold_idx = int(len(sorted_values) * (1 - land_percentage))
    land_threshold = sorted_values[threshold_idx]

    print(f"Land threshold: {land_threshold:.3f} ({land_percentage*100:.0f}% land)")

    # Create image with ocean as default (below sea level)
    ocean_gray = sea_level // 2  # Default underwater
    img = Image.new('RGB', (width, height), (ocean_gray, ocean_gray, ocean_gray))
    draw = ImageDraw.Draw(img)

    # Second pass: draw hexagons with calculated heights
    print("Drawing hexagons...")
    water_count = 0
    land_count = 0

    for cx, cy, value in hex_values:
        if value < land_threshold:
            # Underwater: map to [0, sea_level-1]
            underwater_range = land_threshold - min_val
            if underwater_range > 0:
                normalized = (value - min_val) / underwater_range
            else:
                normalized = 0
            gray = int(normalized * (sea_level - 1))
            water_count += 1
        else:
            # Above water: map to [sea_level, 255]
            above_range = max_val - land_threshold
            if above_range > 0:
                normalized = (value - land_threshold) / above_range
            else:
                normalized = 0
            gray = int(sea_level + normalized * (255 - sea_level))
            land_count += 1

        # Clamp to valid range
        gray = max(0, min(255, gray))

        # Draw hexagon with this height
        draw_hexagon(draw, cx, cy, hex_size, (gray, gray, gray))

    # Save as BMP
    heightmap_path = output_dir / "heightmap.bmp"
    img.save(heightmap_path, "BMP")
    print(f"Saved: {heightmap_path}")

    # Print statistics
    total_hexes = water_count + land_count
    if total_hexes > 0:
        actual_water_pct = water_count / total_hexes * 100
        print(f"Total hexagons: {total_hexes}")
        print(f"Water coverage: {actual_water_pct:.1f}%")
        print(f"Land coverage: {100 - actual_water_pct:.1f}%")


def main():
    parser = argparse.ArgumentParser(
        description='Generate heightmap for Archon Engine'
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
        '--output', type=str, default='.',
        help='Output directory (default: current directory)'
    )
    parser.add_argument(
        '--hex-size', type=float, default=32,
        help='Hexagon radius - must match province map! (default: 32)'
    )
    parser.add_argument(
        '--seed', type=int, default=42,
        help='Random seed (default: 42)'
    )
    parser.add_argument(
        '--sea-level', type=int, default=94,
        help='Sea level grayscale value (default: 94)'
    )
    parser.add_argument(
        '--scale', type=float, default=4.0,
        help='Noise scale - larger values = bigger landmasses (default: 4.0)'
    )
    parser.add_argument(
        '--land', type=float, default=0.4,
        help='Approximate land percentage 0.0-1.0 (default: 0.4)'
    )

    args = parser.parse_args()

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    generate_heightmap(
        width=args.width,
        height=args.height,
        output_dir=output_dir,
        hex_size=args.hex_size,
        seed=args.seed,
        sea_level=args.sea_level,
        scale=args.scale,
        land_percentage=args.land
    )

    print("Done!")


if __name__ == '__main__':
    main()
