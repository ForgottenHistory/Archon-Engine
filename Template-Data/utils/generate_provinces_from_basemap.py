#!/usr/bin/env python3
"""
Generate random Voronoi-style provinces from a basemap.

Land provinces: Organic blob-like shapes using Lloyd's relaxation
Ocean provinces: Sharp geometric Voronoi shapes

Multi-threaded using shared memory for performance on large maps.

Creates:
- provinces.png: RGB image where each unique color = one province
- definition.csv: Province ID, RGB, Name, water flag mappings
"""

import argparse
import random
import os
from pathlib import Path
from collections import Counter
import multiprocessing as mp
from multiprocessing import shared_memory
import time

try:
    from PIL import Image
    import numpy as np
except ImportError:
    print("ERROR: Required packages not installed. Run: pip install Pillow numpy")
    exit(1)

try:
    from scipy.spatial import KDTree
    HAS_SCIPY = True
except ImportError:
    HAS_SCIPY = False
    print("ERROR: scipy required. Run: pip install scipy")
    exit(1)

# Try to import numba for JIT compilation
try:
    from numba import njit, prange
    HAS_NUMBA = True
except ImportError:
    HAS_NUMBA = False
    print("INFO: numba not installed. For faster processing, run: pip install numba")


# Colors in basemap
LAND_COLOR = (255, 255, 255)
OCEAN_COLOR = (153, 204, 255)
OCEAN_BORDER_COLOR = (0, 0, 0)

# Color tolerance
COLOR_TOLERANCE = 5

# Number of workers
NUM_WORKERS = max(1, mp.cpu_count() - 1)


def generate_unique_rgb_land(province_id: int) -> tuple[int, int, int]:
    """Generate unique RGB for land province (warm colors, no blue)."""
    if province_id == 0:
        return (0, 0, 0)

    golden = 0.618033988749895
    hue = (province_id * golden) % 1.0
    hue = hue * 0.6  # Avoid blue

    h = hue * 6.0
    i = int(h)
    f = h - i

    v, s = 0.85, 0.7
    p = v * (1 - s)
    q = v * (1 - s * f)
    t = v * (1 - s * (1 - f))

    if i == 0:
        r, g, b = v, t, p
    elif i == 1:
        r, g, b = q, v, p
    elif i == 2:
        r, g, b = p, v, t
    elif i == 3:
        r, g, b = p, q, v
    elif i == 4:
        r, g, b = t, p, v
    else:
        r, g, b = v, p, q

    r_int = int(r * 255)
    g_int = int(g * 255)
    b_int = int(b * 255)

    r_int = (r_int & 0xE0) | ((province_id >> 0) & 0x1F)
    g_int = (g_int & 0xE0) | ((province_id >> 5) & 0x1F)
    b_int = (b_int & 0xC0) | ((province_id >> 10) & 0x3F)

    r_int = max(1, min(255, r_int))
    g_int = max(0, min(255, g_int))
    b_int = max(0, min(180, b_int))

    if province_id > 32000:
        r_int = 50 + (province_id & 0x7F)
        g_int = 50 + ((province_id >> 7) & 0x7F)
        b_int = ((province_id >> 14) & 0x3F)

    return (r_int, g_int, b_int)


def generate_unique_rgb_ocean(province_id: int) -> tuple[int, int, int]:
    """Generate unique RGB for ocean province (blue dominant)."""
    if province_id == 0:
        return (0, 0, 0)

    r = (province_id * 17) % 80
    g = (province_id * 31) % 120
    b = 150 + (province_id * 7) % 106

    r = (r & 0xC0) | ((province_id >> 0) & 0x3F)
    g = (g & 0x80) | ((province_id >> 6) & 0x7F)
    b = 150 + ((province_id >> 13) & 0x6F)

    r = max(0, min(100, r))
    g = max(0, min(150, g))
    b = max(150, min(255, b))

    return (r, g, b)


def generate_seed_points(mask: np.ndarray, num_provinces: int, seed: int = None) -> np.ndarray:
    """Generate random seed points within masked area. Returns Nx2 array of (x, y)."""
    if seed is not None:
        np.random.seed(seed)

    coords = np.argwhere(mask)  # Returns (row, col) = (y, x)
    total_pixels = len(coords)

    if total_pixels == 0:
        return np.array([], dtype=np.int32).reshape(0, 2)

    num_provinces = min(num_provinces, total_pixels)
    indices = np.random.choice(total_pixels, size=num_provinces, replace=False)

    # Convert from (row, col) to (x, y)
    selected = coords[indices]
    return np.column_stack((selected[:, 1], selected[:, 0])).astype(np.int32)


def assign_provinces_kdtree(width: int, height: int, mask: np.ndarray,
                           seed_points: np.ndarray, start_id: int = 1) -> np.ndarray:
    """Assign each pixel to nearest seed point using KDTree."""
    province_map = np.zeros((height, width), dtype=np.int32)

    if len(seed_points) == 0:
        return province_map

    tree = KDTree(seed_points)
    mask_y, mask_x = np.where(mask)
    coords = np.column_stack((mask_x, mask_y))

    print(f"    Querying KDTree for {len(coords):,} pixels ({NUM_WORKERS} workers)...")
    _, indices = tree.query(coords, workers=NUM_WORKERS)
    province_map[mask_y, mask_x] = indices + start_id

    return province_map


# Numba-accelerated functions if available
if HAS_NUMBA:
    @njit(parallel=True)
    def compute_centroids_numba(province_map, num_provinces, start_id):
        """Compute centroids for all provinces in parallel using numba."""
        height, width = province_map.shape

        # Arrays to accumulate sums
        sum_x = np.zeros(num_provinces, dtype=np.float64)
        sum_y = np.zeros(num_provinces, dtype=np.float64)
        counts = np.zeros(num_provinces, dtype=np.int64)

        # Parallel accumulation per row
        for y in prange(height):
            for x in range(width):
                prov_id = province_map[y, x]
                if prov_id >= start_id and prov_id < start_id + num_provinces:
                    idx = prov_id - start_id
                    sum_x[idx] += x
                    sum_y[idx] += y
                    counts[idx] += 1

        # Compute centroids
        centroids = np.zeros((num_provinces, 2), dtype=np.int32)
        for i in range(num_provinces):
            if counts[i] > 0:
                centroids[i, 0] = int(sum_x[i] / counts[i])
                centroids[i, 1] = int(sum_y[i] / counts[i])

        return centroids, counts

    @njit(parallel=True)
    def smooth_boundaries_numba(province_map, mask, start_id, end_id):
        """Smooth province boundaries using mode filter (numba parallel)."""
        height, width = province_map.shape
        result = province_map.copy()

        for y in prange(1, height - 1):
            for x in range(1, width - 1):
                if not mask[y, x]:
                    continue

                current = province_map[y, x]
                if current < start_id or current > end_id:
                    continue

                # Check if boundary
                is_boundary = False
                for dy in range(-1, 2):
                    for dx in range(-1, 2):
                        neighbor = province_map[y + dy, x + dx]
                        if neighbor != current and neighbor >= start_id and neighbor <= end_id:
                            is_boundary = True
                            break
                    if is_boundary:
                        break

                if not is_boundary:
                    continue

                # Count neighbors (simple mode finding)
                # Using a fixed-size array for counting (max 9 unique neighbors)
                neighbor_ids = np.zeros(9, dtype=np.int32)
                neighbor_counts = np.zeros(9, dtype=np.int32)
                num_unique = 0

                for dy in range(-1, 2):
                    for dx in range(-1, 2):
                        nid = province_map[y + dy, x + dx]
                        if nid >= start_id and nid <= end_id:
                            # Find or add
                            found = False
                            for i in range(num_unique):
                                if neighbor_ids[i] == nid:
                                    neighbor_counts[i] += 1
                                    found = True
                                    break
                            if not found and num_unique < 9:
                                neighbor_ids[num_unique] = nid
                                neighbor_counts[num_unique] = 1
                                num_unique += 1

                # Find mode
                if num_unique > 0:
                    max_count = 0
                    max_id = current
                    for i in range(num_unique):
                        if neighbor_counts[i] > max_count:
                            max_count = neighbor_counts[i]
                            max_id = neighbor_ids[i]
                    result[y, x] = max_id

        return result

    @njit(parallel=True)
    def add_noise_numba(province_map, mask, boundary_y, boundary_x,
                        change_indices, start_id, end_id, random_values):
        """Add noise to boundary pixels (numba parallel)."""
        height, width = province_map.shape
        result = province_map.copy()

        for idx in prange(len(change_indices)):
            i = change_indices[idx]
            y = boundary_y[i]
            x = boundary_x[i]

            # Collect valid neighbors
            neighbors = np.zeros(8, dtype=np.int32)
            num_neighbors = 0

            for dy in range(-1, 2):
                for dx in range(-1, 2):
                    if dy == 0 and dx == 0:
                        continue
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < height and 0 <= nx < width:
                        nid = province_map[ny, nx]
                        if nid >= start_id and nid <= end_id and nid != province_map[y, x]:
                            neighbors[num_neighbors] = nid
                            num_neighbors += 1

            if num_neighbors > 0:
                # Use pre-generated random value
                chosen_idx = int(random_values[idx] * num_neighbors) % num_neighbors
                result[y, x] = neighbors[chosen_idx]

        return result


def compute_centroids_numpy(province_map, num_provinces, start_id, mask):
    """Compute centroids using numpy (fallback if no numba)."""
    centroids = np.zeros((num_provinces, 2), dtype=np.int32)
    counts = np.zeros(num_provinces, dtype=np.int64)

    for i in range(num_provinces):
        prov_id = start_id + i
        prov_mask = province_map == prov_id
        prov_y, prov_x = np.where(prov_mask)

        if len(prov_x) > 0:
            cx = int(np.mean(prov_x))
            cy = int(np.mean(prov_y))

            # Ensure centroid is on valid land
            if not mask[cy, cx]:
                valid_in_prov = prov_mask & mask
                valid_y, valid_x = np.where(valid_in_prov)
                if len(valid_x) > 0:
                    distances = (valid_x - cx)**2 + (valid_y - cy)**2
                    nearest = np.argmin(distances)
                    cx, cy = valid_x[nearest], valid_y[nearest]

            centroids[i] = [cx, cy]
            counts[i] = len(prov_x)

    return centroids, counts


def lloyd_relaxation(seed_points: np.ndarray, province_map: np.ndarray,
                     mask: np.ndarray, iterations: int, start_id: int) -> tuple[np.ndarray, np.ndarray]:
    """Apply Lloyd's relaxation for organic shapes."""
    height, width = mask.shape
    current_seeds = seed_points.copy()
    num_provinces = len(current_seeds)

    for iteration in range(iterations):
        start_time = time.time()

        # Compute centroids
        if HAS_NUMBA:
            centroids, counts = compute_centroids_numba(province_map, num_provinces, start_id)
        else:
            centroids, counts = compute_centroids_numpy(province_map, num_provinces, start_id, mask)

        # Update seeds where we have valid centroids
        for i in range(num_provinces):
            if counts[i] > 0:
                cx, cy = centroids[i]
                # Ensure on mask
                if 0 <= cy < height and 0 <= cx < width:
                    if mask[cy, cx]:
                        current_seeds[i] = [cx, cy]
                    else:
                        # Find nearest valid point
                        prov_mask = province_map == (start_id + i)
                        valid_mask = prov_mask & mask
                        valid_y, valid_x = np.where(valid_mask)
                        if len(valid_x) > 0:
                            distances = (valid_x - cx)**2 + (valid_y - cy)**2
                            nearest = np.argmin(distances)
                            current_seeds[i] = [valid_x[nearest], valid_y[nearest]]

        elapsed = time.time() - start_time
        print(f"    Lloyd iteration {iteration + 1}/{iterations} ({elapsed:.1f}s)")

        # Reassign pixels (except on last iteration)
        if iteration < iterations - 1:
            tree = KDTree(current_seeds)
            mask_y, mask_x = np.where(mask)
            coords = np.column_stack((mask_x, mask_y))
            _, indices = tree.query(coords, workers=NUM_WORKERS)
            province_map[mask_y, mask_x] = indices + start_id

    return current_seeds, province_map


def find_boundary_pixels(province_map, mask, start_id, end_id):
    """Find pixels that are on province boundaries."""
    height, width = province_map.shape

    padded = np.pad(province_map, 1, mode='edge')
    boundary_mask = np.zeros((height, width), dtype=bool)

    valid_range = (province_map >= start_id) & (province_map <= end_id)

    for dy, dx in [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)]:
        shifted = padded[1+dy:height+1+dy, 1+dx:width+1+dx]
        neighbor_valid = (shifted >= start_id) & (shifted <= end_id)
        boundary_mask |= (province_map != shifted) & valid_range & neighbor_valid

    boundary_mask &= mask
    return np.where(boundary_mask)


def add_organic_noise(province_map: np.ndarray, mask: np.ndarray,
                      noise_strength: float, start_id: int, end_id: int,
                      seed: int = None) -> np.ndarray:
    """Add organic noise to province boundaries."""
    if seed is not None:
        np.random.seed(seed)

    height, width = province_map.shape

    # Find boundary pixels
    boundary_y, boundary_x = find_boundary_pixels(province_map, mask, start_id, end_id)
    num_boundary = len(boundary_x)

    if num_boundary == 0:
        return province_map

    print(f"    Processing {num_boundary:,} boundary pixels...")

    # Select pixels to change
    num_to_change = int(num_boundary * noise_strength)
    change_indices = np.random.choice(num_boundary, size=num_to_change, replace=False)

    if HAS_NUMBA:
        random_values = np.random.random(len(change_indices))
        result = add_noise_numba(province_map, mask, boundary_y, boundary_x,
                                 change_indices, start_id, end_id, random_values)
    else:
        result = province_map.copy()
        for idx in change_indices:
            y, x = boundary_y[idx], boundary_x[idx]
            neighbors = []
            for dy in range(-1, 2):
                for dx in range(-1, 2):
                    ny, nx = y + dy, x + dx
                    if 0 <= ny < height and 0 <= nx < width:
                        nid = province_map[ny, nx]
                        if nid >= start_id and nid <= end_id and nid != result[y, x]:
                            neighbors.append(nid)
            if neighbors:
                result[y, x] = np.random.choice(neighbors)

    return result


def smooth_provinces(province_map: np.ndarray, mask: np.ndarray,
                     iterations: int, start_id: int, end_id: int) -> np.ndarray:
    """Smooth province boundaries."""
    result = province_map.copy()

    for i in range(iterations):
        start_time = time.time()

        if HAS_NUMBA:
            result = smooth_boundaries_numba(result, mask, start_id, end_id)
        else:
            # Numpy fallback
            height, width = result.shape
            boundary_y, boundary_x = find_boundary_pixels(result, mask, start_id, end_id)

            new_result = result.copy()
            for idx in range(len(boundary_x)):
                y, x = boundary_y[idx], boundary_x[idx]

                y_min, y_max = max(0, y-1), min(height, y+2)
                x_min, x_max = max(0, x-1), min(width, x+2)

                neighborhood = result[y_min:y_max, x_min:x_max].flatten()
                valid = (neighborhood >= start_id) & (neighborhood <= end_id)
                valid_neighbors = neighborhood[valid]

                if len(valid_neighbors) > 0:
                    counts = np.bincount(valid_neighbors - start_id)
                    new_result[y, x] = np.argmax(counts) + start_id

            result = new_result

        elapsed = time.time() - start_time
        print(f"    Smoothing pass {i + 1}/{iterations} ({elapsed:.1f}s)")

    return result


def create_masks_vectorized(basemap_array: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Create land and ocean masks using vectorized operations."""
    r, g, b = basemap_array[:, :, 0], basemap_array[:, :, 1], basemap_array[:, :, 2]

    land_mask = (
        (np.abs(r.astype(np.int16) - LAND_COLOR[0]) <= COLOR_TOLERANCE) &
        (np.abs(g.astype(np.int16) - LAND_COLOR[1]) <= COLOR_TOLERANCE) &
        (np.abs(b.astype(np.int16) - LAND_COLOR[2]) <= COLOR_TOLERANCE)
    )

    ocean_mask = (
        ((np.abs(r.astype(np.int16) - OCEAN_COLOR[0]) <= COLOR_TOLERANCE) &
         (np.abs(g.astype(np.int16) - OCEAN_COLOR[1]) <= COLOR_TOLERANCE) &
         (np.abs(b.astype(np.int16) - OCEAN_COLOR[2]) <= COLOR_TOLERANCE)) |
        ((np.abs(r.astype(np.int16) - OCEAN_BORDER_COLOR[0]) <= COLOR_TOLERANCE) &
         (np.abs(g.astype(np.int16) - OCEAN_BORDER_COLOR[1]) <= COLOR_TOLERANCE) &
         (np.abs(b.astype(np.int16) - OCEAN_BORDER_COLOR[2]) <= COLOR_TOLERANCE))
    )

    return land_mask, ocean_mask


def generate_provinces(
    basemap_path: Path,
    output_dir: Path,
    total_provinces: int = 50000,
    ocean_density_multiplier: float = 1.0,
    seed: int = None,
    relaxation_iterations: int = 5
) -> int:
    """Generate province map with organic land and sharp ocean provinces."""

    total_start = time.time()

    print(f"Loading basemap: {basemap_path}")
    print(f"Using {NUM_WORKERS} CPU workers")
    print(f"Numba JIT: {'enabled' if HAS_NUMBA else 'disabled'}")

    Image.MAX_IMAGE_PIXELS = None
    basemap = Image.open(basemap_path).convert('RGB')
    width, height = basemap.size
    print(f"Basemap size: {width}x{height} ({width * height:,} pixels)")

    basemap_array = np.array(basemap)

    print("Creating masks...")
    land_mask, ocean_mask = create_masks_vectorized(basemap_array)

    land_pixels = np.sum(land_mask)
    ocean_pixels = np.sum(ocean_mask)
    total_valid = land_pixels + ocean_pixels

    print(f"Land pixels: {land_pixels:,}")
    print(f"Ocean pixels: {ocean_pixels:,}")

    if total_valid == 0:
        print("ERROR: No valid pixels found!")
        return 0

    # Calculate province distribution
    land_ratio = land_pixels / total_valid
    ocean_ratio = ocean_pixels / total_valid

    adjusted_ocean_ratio = ocean_ratio * ocean_density_multiplier
    adjusted_total = land_ratio + adjusted_ocean_ratio

    num_land = int(total_provinces * (land_ratio / adjusted_total))
    num_ocean = total_provinces - num_land

    num_land = min(num_land, land_pixels)
    num_ocean = min(num_ocean, ocean_pixels)

    print(f"\nTarget distribution:")
    print(f"  Land provinces: {num_land:,} (~{land_pixels // max(1, num_land):,} pixels each)")
    print(f"  Ocean provinces: {num_ocean:,} (~{ocean_pixels // max(1, num_ocean):,} pixels each)")

    province_map = np.zeros((height, width), dtype=np.int32)

    # === LAND PROVINCES (organic) ===
    if num_land > 0 and land_pixels > 0:
        print(f"\n=== Generating {num_land:,} land provinces (organic) ===")

        land_seeds = generate_seed_points(land_mask, num_land, seed)
        print(f"  Generated {len(land_seeds):,} seed points")

        print("  Initial assignment...")
        province_map = assign_provinces_kdtree(width, height, land_mask, land_seeds, start_id=1)

        if relaxation_iterations > 0:
            print(f"  Lloyd relaxation ({relaxation_iterations} iterations)...")
            land_seeds, province_map = lloyd_relaxation(
                land_seeds, province_map, land_mask, relaxation_iterations, start_id=1
            )

            print("  Adding organic noise...")
            province_map = add_organic_noise(
                province_map, land_mask, 0.4,
                start_id=1, end_id=len(land_seeds), seed=seed
            )

            print("  Smoothing boundaries...")
            province_map = smooth_provinces(
                province_map, land_mask, 3,
                start_id=1, end_id=len(land_seeds)
            )

        actual_land = len(land_seeds)
    else:
        actual_land = 0

    # === OCEAN PROVINCES (sharp) ===
    ocean_start_id = actual_land + 1

    if num_ocean > 0 and ocean_pixels > 0:
        print(f"\n=== Generating {num_ocean:,} ocean provinces (sharp) ===")

        ocean_seed = seed + 1000 if seed else None
        ocean_seeds = generate_seed_points(ocean_mask, num_ocean, ocean_seed)
        print(f"  Generated {len(ocean_seeds):,} seed points")

        print("  Assigning provinces...")
        ocean_province_map = assign_provinces_kdtree(
            width, height, ocean_mask, ocean_seeds, start_id=ocean_start_id
        )

        ocean_pixels_mask = ocean_province_map > 0
        province_map[ocean_pixels_mask] = ocean_province_map[ocean_pixels_mask]

        actual_ocean = len(ocean_seeds)
    else:
        actual_ocean = 0

    # === Generate output ===
    print("\n=== Creating output files ===")

    unique_ids = np.unique(province_map)
    unique_ids = unique_ids[unique_ids > 0]

    land_ids = unique_ids[unique_ids < ocean_start_id]
    ocean_ids = unique_ids[unique_ids >= ocean_start_id]

    print(f"  Actual land provinces: {len(land_ids):,}")
    print(f"  Actual ocean provinces: {len(ocean_ids):,}")
    print(f"  Total provinces: {len(unique_ids):,}")

    # Generate colors
    print("  Generating colors...")
    province_colors = {}
    province_is_water = {}

    for prov_id in land_ids:
        province_colors[prov_id] = generate_unique_rgb_land(prov_id)
        province_is_water[prov_id] = False

    for prov_id in ocean_ids:
        province_colors[prov_id] = generate_unique_rgb_ocean(prov_id)
        province_is_water[prov_id] = True

    # Create output image
    print("  Creating province image...")
    output_array = np.zeros((height, width, 3), dtype=np.uint8)

    max_id = max(province_colors.keys()) if province_colors else 0
    color_lut = np.zeros((max_id + 1, 3), dtype=np.uint8)
    for prov_id, color in province_colors.items():
        color_lut[prov_id] = color

    valid_mask = province_map > 0
    output_array[valid_mask] = color_lut[province_map[valid_mask]]

    output_img = Image.fromarray(output_array, 'RGB')
    provinces_path = output_dir / "provinces.png"
    output_img.save(provinces_path)
    print(f"  Saved: {provinces_path}")

    # Save definition.csv
    print("  Writing definition.csv...")
    definition_path = output_dir / "definition.csv"
    with open(definition_path, 'w', encoding='utf-8') as f:
        f.write("province;red;green;blue;name;x\n")

        for prov_id in sorted(province_colors.keys()):
            r, g, b = province_colors[prov_id]
            is_water = province_is_water[prov_id]
            water_flag = "" if is_water else "x"
            name = f"Sea_{prov_id}" if is_water else f"Province_{prov_id}"
            f.write(f"{prov_id};{r};{g};{b};{name};{water_flag}\n")

    print(f"  Saved: {definition_path}")

    total_elapsed = time.time() - total_start
    print(f"\nTotal time: {total_elapsed:.1f}s")

    return len(unique_ids)


def main():
    global NUM_WORKERS

    parser = argparse.ArgumentParser(
        description='Generate provinces from basemap (organic land, sharp ocean)'
    )
    parser.add_argument('basemap', type=str, help='Path to basemap image')
    parser.add_argument('--output', '-o', type=str, default='.', help='Output directory')
    parser.add_argument('--provinces', '-p', type=int, default=50000, help='Total provinces (default: 50000)')
    parser.add_argument('--ocean-density', '-od', type=float, default=0.5, help='Ocean density multiplier (default: 0.5)')
    parser.add_argument('--seed', '-s', type=int, default=None, help='Random seed')
    parser.add_argument('--relaxation', '-r', type=int, default=5, help='Lloyd relaxation iterations (default: 5)')
    parser.add_argument('--workers', '-w', type=int, default=None, help=f'Number of workers (default: {NUM_WORKERS})')

    args = parser.parse_args()

    if args.workers:
        NUM_WORKERS = args.workers

    basemap_path = Path(args.basemap)
    if not basemap_path.exists():
        print(f"ERROR: Basemap not found: {basemap_path}")
        exit(1)

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    print("=== Province Generator ===")
    print(f"Basemap: {basemap_path}")
    print(f"Output: {output_dir.absolute()}")
    print(f"Target provinces: {args.provinces:,}")
    print(f"Ocean density: {args.ocean_density}")
    print(f"Relaxation: {args.relaxation}")
    print(f"Workers: {NUM_WORKERS}")
    if args.seed:
        print(f"Seed: {args.seed}")
    print()

    num_provinces = generate_provinces(
        basemap_path=basemap_path,
        output_dir=output_dir,
        total_provinces=args.provinces,
        ocean_density_multiplier=args.ocean_density,
        seed=args.seed,
        relaxation_iterations=args.relaxation
    )

    if num_provinces > 0:
        print(f"\nDone! Generated {num_provinces:,} total provinces.")
    else:
        print("\nFailed to generate provinces.")
        exit(1)


if __name__ == '__main__':
    main()
