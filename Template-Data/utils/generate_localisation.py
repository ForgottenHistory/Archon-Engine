#!/usr/bin/env python3
"""
Generate template localization files for Archon Engine.

Creates Paradox-style YAML localization files:
- localisation/english/provinces_l_english.yml - Province names
- localisation/english/countries_l_english.yml - Country names
- localisation/english/terrain_l_english.yml - Terrain type names

UI localisation (ui_l_english.yml) is manually maintained - not generated.
This is ENGINE layer template data - uses Paradox YAML format.
"""

import argparse
from pathlib import Path


# Template countries (must match generate_countries.py)
TEMPLATE_COUNTRIES = [
    ("RED", "Red Empire", "Red"),
    ("BLU", "Blue Kingdom", "Blue"),
    ("GRN", "Green Republic", "Green"),
    ("YEL", "Yellow Dominion", "Yellow"),
    ("PUR", "Purple Realm", "Purple"),
    ("ORG", "Orange Federation", "Orange"),
    ("CYN", "Cyan Alliance", "Cyan"),
    ("PNK", "Pink Dynasty", "Pink"),
    ("BRN", "Brown Confederacy", "Brown"),
    ("GRY", "Gray Union", "Gray"),
]


def load_definition_csv(definition_path: Path) -> list[dict]:
    """Load province definitions from definition.csv."""
    provinces = []

    with open(definition_path, "r", encoding="utf-8") as f:
        header = f.readline()  # Skip header

        for line in f:
            line = line.strip()
            if not line:
                continue

            parts = line.split(";")
            if len(parts) >= 5:
                water_flag = parts[5].strip() if len(parts) > 5 else "x"
                is_water = water_flag != "x"

                provinces.append({
                    "id": int(parts[0]),
                    "name": parts[4] if len(parts) > 4 else f"Province_{parts[0]}",
                    "is_water": is_water
                })

    return provinces


def generate_province_localisation(output_dir: Path, definition_path: Path, language: str) -> None:
    """Generate province name localization file."""
    provinces = load_definition_csv(definition_path)

    loc_dir = output_dir / "localisation" / language
    loc_dir.mkdir(parents=True, exist_ok=True)

    filepath = loc_dir / f"provinces_l_{language}.yml"

    with open(filepath, "w", encoding="utf-8-sig") as f:  # UTF-8 BOM for Paradox compatibility
        f.write(f"l_{language}:\n")

        for prov in provinces:
            pid = prov["id"]
            name = prov["name"]
            # Escape quotes in names
            name_escaped = name.replace('"', '\\"')
            f.write(f' PROV{pid}:0 "{name_escaped}"\n')

    print(f"Generated: {filepath} ({len(provinces)} provinces)")


def generate_country_localisation(output_dir: Path, language: str) -> None:
    """Generate country name localization file."""
    loc_dir = output_dir / "localisation" / language
    loc_dir.mkdir(parents=True, exist_ok=True)

    filepath = loc_dir / f"countries_l_{language}.yml"

    with open(filepath, "w", encoding="utf-8-sig") as f:  # UTF-8 BOM for Paradox compatibility
        f.write(f"l_{language}:\n")

        for tag, name, adjective in TEMPLATE_COUNTRIES:
            f.write(f' {tag}:0 "{name}"\n')
            f.write(f' {tag}_ADJ:0 "{adjective}"\n')

    print(f"Generated: {filepath} ({len(TEMPLATE_COUNTRIES)} countries)")


def generate_terrain_localisation(output_dir: Path, language: str) -> None:
    """Generate terrain type localization file."""
    loc_dir = output_dir / "localisation" / language
    loc_dir.mkdir(parents=True, exist_ok=True)

    filepath = loc_dir / f"terrain_l_{language}.yml"

    # Standard terrain types
    terrain_types = [
        ("plains", "Plains"),
        ("farmlands", "Farmlands"),
        ("hills", "Hills"),
        ("mountains", "Mountains"),
        ("forest", "Forest"),
        ("jungle", "Jungle"),
        ("marsh", "Marsh"),
        ("desert", "Desert"),
        ("ocean", "Ocean"),
        ("sea", "Sea"),
        ("coastal_sea", "Coastal Sea"),
    ]

    with open(filepath, "w", encoding="utf-8-sig") as f:
        f.write(f"l_{language}:\n")

        for terrain_key, terrain_name in terrain_types:
            f.write(f' TERRAIN_{terrain_key}:0 "{terrain_name}"\n')

    print(f"Generated: {filepath} ({len(terrain_types)} terrain types)")



def main():
    parser = argparse.ArgumentParser(
        description="Generate template localization data for Archon Engine"
    )
    parser.add_argument(
        "--output", type=str, default=".",
        help="Output directory (default: current directory)"
    )
    parser.add_argument(
        "--definition", type=str, default=None,
        help="Path to definition.csv (for province names)"
    )
    parser.add_argument(
        "--language", type=str, default="english",
        help="Language code (default: english)"
    )
    parser.add_argument(
        "--all-languages", action="store_true",
        help="Generate stubs for all supported languages"
    )

    args = parser.parse_args()

    output_dir = Path(args.output)
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Generating template localization data...")
    print(f"Output directory: {output_dir.absolute()}")

    languages = ["english"]
    if args.all_languages:
        # Standard Paradox language codes
        languages = [
            "english",
            "french",
            "german",
            "spanish",
            "russian",
            "simp_chinese",
            "braz_por",
            "polish",
        ]

    for lang in languages:
        print(f"\nGenerating {lang} localisation:")

        # Generate country names
        generate_country_localisation(output_dir, lang)

        # Generate terrain names
        generate_terrain_localisation(output_dir, lang)

        # Generate province names if definition.csv provided
        if args.definition:
            definition_path = Path(args.definition)
            if definition_path.exists():
                generate_province_localisation(output_dir, definition_path, lang)
            else:
                print(f"  Warning: definition.csv not found at {definition_path}")
                print("  Skipping province localisation generation.")
        else:
            print("  Note: No --definition provided, skipping province localisation.")

    print("\nDone!")


if __name__ == "__main__":
    main()
