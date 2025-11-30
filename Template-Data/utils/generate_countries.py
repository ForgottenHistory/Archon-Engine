#!/usr/bin/env python3
"""
Generate template country and province ownership data for Archon Engine.

Creates:
- common/country_tags/00_countries.txt - Country tag to file mapping
- common/countries/*.txt - Basic country definitions with colors
- Province ownership assignment (assigns provinces to countries in regions)

This is ENGINE layer template data - minimal fields only.
"""

import argparse
import random
import math
from pathlib import Path


# Template countries with distinct colors
# Format: (tag, name, color_rgb)
TEMPLATE_COUNTRIES = [
    ("RED", "Red Empire", (200, 50, 50)),
    ("BLU", "Blue Kingdom", (50, 100, 200)),
    ("GRN", "Green Republic", (50, 180, 80)),
    ("YEL", "Yellow Dominion", (220, 200, 50)),
    ("PUR", "Purple Realm", (150, 50, 180)),
    ("ORG", "Orange Federation", (230, 140, 50)),
    ("CYN", "Cyan Alliance", (50, 180, 180)),
    ("PNK", "Pink Dynasty", (220, 100, 150)),
    ("BRN", "Brown Confederacy", (140, 90, 50)),
    ("GRY", "Gray Union", (120, 120, 130)),
]

# Max provinces per country
MAX_PROVINCES_PER_COUNTRY = 10


def generate_country_tags(output_dir: Path) -> None:
    """Generate 00_countries.txt with tag mappings."""
    tags_dir = output_dir / "common" / "country_tags"
    tags_dir.mkdir(parents=True, exist_ok=True)

    with open(tags_dir / "00_countries.txt", "w", encoding="utf-8") as f:
        f.write("# Template country tags for Archon Engine\n")
        f.write("# Format: TAG = \"countries/Filename.json5\"\n\n")

        for tag, name, _ in TEMPLATE_COUNTRIES:
            filename = name.replace(" ", "")
            f.write(f'{tag} = "countries/{filename}.json5"\n')

    print(f"Generated: {tags_dir / '00_countries.txt'}")


def generate_country_files(output_dir: Path) -> None:
    """Generate individual country definition files in JSON5 format."""
    countries_dir = output_dir / "common" / "countries"
    countries_dir.mkdir(parents=True, exist_ok=True)

    for tag, name, (r, g, b) in TEMPLATE_COUNTRIES:
        filename = name.replace(" ", "") + ".json5"
        filepath = countries_dir / filename

        with open(filepath, "w", encoding="utf-8") as f:
            f.write(f"// {name}\n")
            f.write("{\n")
            f.write(f'  tag: "{tag}",\n')
            f.write('  graphical_culture: "westerngfx",\n')
            f.write(f"  color: [{r}, {g}, {b}]\n")
            f.write("}\n")

        print(f"Generated: {filepath}")


def load_definition_csv(definition_path: Path) -> list[dict]:
    """Load province definitions from definition.csv including water flag."""
    provinces = []

    with open(definition_path, "r", encoding="utf-8") as f:
        header = f.readline()  # Skip header

        for line in f:
            line = line.strip()
            if not line:
                continue

            parts = line.split(";")
            if len(parts) >= 5:
                # Water flag: 'x' = land, empty = water
                water_flag = parts[5].strip() if len(parts) > 5 else "x"
                is_water = water_flag != "x"

                provinces.append({
                    "id": int(parts[0]),
                    "r": int(parts[1]),
                    "g": int(parts[2]),
                    "b": int(parts[3]),
                    "name": parts[4] if len(parts) > 4 else f"Province_{parts[0]}",
                    "is_water": is_water
                })

    return provinces


def assign_provinces_to_countries(
    provinces: list[dict],
    map_width: int,
    map_height: int,
    hex_size: float,
    unowned_ratio: float = 0.3
) -> dict[int, str]:
    """
    Assign provinces to countries in contiguous clusters with space between them.
    Each country gets up to MAX_PROVINCES_PER_COUNTRY provinces.
    Returns mapping of province_id -> country_tag (or None for unowned).
    """
    num_countries = len(TEMPLATE_COUNTRIES)
    assignments = {}

    # Build position data for provinces (approximate from hex grid)
    hex_width = hex_size * 2
    hex_height = hex_size * math.sqrt(3)
    col_spacing = hex_width * 0.75
    row_spacing = hex_height
    rows_per_col = int(map_height / row_spacing) + 2

    province_positions = {}
    for prov in provinces:
        pid = prov["id"]
        # Reconstruct hex position from province ID
        col = (pid - 1) // rows_per_col
        row = (pid - 1) % rows_per_col
        cx = col * col_spacing + hex_size
        cy = row * row_spacing + hex_height / 2
        if col % 2 == 1:
            cy += row_spacing / 2
        province_positions[pid] = (cx, cy)

    # Place country capitals spread far apart across the map
    capitals = []
    # Use wider spacing - 5 columns x 2 rows for 10 countries
    cols = 5
    rows = 2

    for i, (tag, _, _) in enumerate(TEMPLATE_COUNTRIES):
        col = i % cols
        row = i // cols
        # Spread capitals with large padding from edges and each other
        x = 300 + col * ((map_width - 600) / (cols - 1)) if cols > 1 else map_width / 2
        y = 400 + row * ((map_height - 800) / (rows - 1)) if rows > 1 else map_height / 2
        capitals.append((tag, x, y))

    # Track how many provinces each country has
    country_counts = {tag: 0 for tag, _, _ in TEMPLATE_COUNTRIES}

    # Maximum radius from capital - provinces must be within this distance
    # This ensures countries don't spread too far and overlap
    max_radius = 400  # pixels

    # Assign provinces closest to their capital, one country at a time
    # This prevents interleaving and ensures contiguous clusters
    for tag, cap_x, cap_y in capitals:
        # Find all provinces within max_radius of this capital
        nearby = []
        for prov in provinces:
            pid = prov["id"]
            if pid not in province_positions:
                continue
            if pid in assignments:
                continue  # Already assigned
            cx, cy = province_positions[pid]
            dist = math.sqrt((cx - cap_x) ** 2 + (cy - cap_y) ** 2)
            if dist <= max_radius:
                nearby.append((dist, pid))

        # Sort by distance and assign closest ones
        nearby.sort()
        for dist, pid in nearby[:MAX_PROVINCES_PER_COUNTRY]:
            assignments[pid] = tag
            country_counts[tag] += 1

    # All unassigned provinces remain unowned (None)
    for prov in provinces:
        pid = prov["id"]
        if pid not in assignments:
            assignments[pid] = None

    return assignments


def generate_province_ownership(
    output_dir: Path,
    definition_path: Path,
    map_width: int,
    map_height: int,
    hex_size: float,
    unowned_ratio: float
) -> None:
    """Generate province ownership assignments in JSON5 format."""
    # Load provinces
    provinces = load_definition_csv(definition_path)

    # Separate land and water provinces
    land_provinces = [p for p in provinces if not p.get("is_water", False)]
    water_provinces = [p for p in provinces if p.get("is_water", False)]

    print(f"Loaded {len(provinces)} provinces from definition.csv")
    print(f"  Land: {len(land_provinces)}, Water: {len(water_provinces)}")

    # Assign only LAND provinces to countries
    assignments = assign_provinces_to_countries(
        land_provinces, map_width, map_height, hex_size, unowned_ratio
    )

    # Create output directory
    ownership_dir = output_dir / "history" / "provinces"
    ownership_dir.mkdir(parents=True, exist_ok=True)

    # Count assignments
    owned_count = sum(1 for v in assignments.values() if v is not None)
    unowned_land_count = len(land_provinces) - owned_count

    # Generate minimal province history files in JSON5 format
    # Only for owned LAND provinces - ENGINE just needs owner/controller
    for prov in land_provinces:
        pid = prov["id"]
        owner = assignments.get(pid)

        if owner is None:
            continue  # Skip unowned - no file needed

        filename = f"{pid}-{prov['name']}.json5"
        filepath = ownership_dir / filename

        with open(filepath, "w", encoding="utf-8") as f:
            f.write(f"// Province {pid}: {prov['name']}\n")
            f.write("{\n")
            f.write(f'  owner: "{owner}",\n')
            f.write(f'  controller: "{owner}"\n')
            f.write("}\n")

    print(f"\nGenerated province ownership files (JSON5):")
    print(f"  Owned land: {owned_count} provinces")
    print(f"  Unowned land: {unowned_land_count} provinces")
    print(f"  Water (skipped): {len(water_provinces)} provinces")
    print(f"  Output: {ownership_dir}")

    # Print country distribution
    print("\nCountry distribution:")
    country_counts = {}
    for owner in assignments.values():
        if owner:
            country_counts[owner] = country_counts.get(owner, 0) + 1

    for tag, count in sorted(country_counts.items(), key=lambda x: -x[1]):
        name = next((n for t, n, _ in TEMPLATE_COUNTRIES if t == tag), tag)
        print(f"  {tag} ({name}): {count} provinces")


def main():
    parser = argparse.ArgumentParser(
        description="Generate template country data for Archon Engine"
    )
    parser.add_argument(
        "--output", type=str, default=".",
        help="Output directory (default: current directory)"
    )
    parser.add_argument(
        "--definition", type=str, default=None,
        help="Path to definition.csv (for province ownership)"
    )
    parser.add_argument(
        "--map-width", type=int, default=5632,
        help="Map width in pixels (default: 5632)"
    )
    parser.add_argument(
        "--map-height", type=int, default=2048,
        help="Map height in pixels (default: 2048)"
    )
    parser.add_argument(
        "--hex-size", type=float, default=32,
        help="Hexagon radius in pixels (default: 32)"
    )
    parser.add_argument(
        "--unowned-ratio", type=float, default=0.25,
        help="Ratio of provinces to leave unowned (default: 0.25)"
    )
    parser.add_argument(
        "--seed", type=int, default=42,
        help="Random seed for reproducible output (default: 42)"
    )

    args = parser.parse_args()

    random.seed(args.seed)

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Generating template country data...")
    print(f"Output directory: {output_dir.absolute()}")

    # Generate country tags and definitions
    generate_country_tags(output_dir)
    generate_country_files(output_dir)

    # Generate province ownership if definition.csv provided
    if args.definition:
        definition_path = Path(args.definition)
        if definition_path.exists():
            generate_province_ownership(
                output_dir,
                definition_path,
                args.map_width,
                args.map_height,
                args.hex_size,
                args.unowned_ratio
            )
        else:
            print(f"\nWarning: definition.csv not found at {definition_path}")
            print("Skipping province ownership generation.")
    else:
        print("\nNote: No --definition provided, skipping province ownership.")
        print("Run with --definition path/to/definition.csv to generate ownership.")

    print("\nDone!")


if __name__ == "__main__":
    main()
