"""Build the mod-author reference documents from the data dumped out of the game.

Everything here is read from the running game's own data, so the tables describe what
is really installed rather than what a hand-written list claims.

Run:  python build_reference.py <art folder> <data folder> <output folder>
"""

import os
import re
import sys
from collections import defaultdict

from PIL import Image
from docx import Document
from docx.enum.section import WD_ORIENT
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.shared import Emu, Inches, Pt, RGBColor

# Pose bundles the game ships for its own cut scenes and deaths. They are separate
# from the four main characters but are called by the scripts just the same.
CHARACTER_BUNDLES = ["monika", "natsuki", "sayori", "yuri"]
EXTRA_SPRITE_BUNDLES = [
    "nm", "sm", "sn", "sy", "ym", "ny", "nf1", "nf2",
    "y_kill", "stab", "stab_censor", "oneeye2_base", "yuri_eyesfull", "yuri_hack",
]
SPRITE_BUNDLES = CHARACTER_BUNDLES + EXTRA_SPRITE_BUNDLES

# Loose parts: referenced only to build a pose, never shown on their own.
DETAILS = {
    "mouth", "eye", "eyes", "n_eye", "n_blackeyes", "eyesfull",
    "mask", "maskb", "mask_2", "mask_3",
    "child", "child_2", "ghost_blood", "ghost", "blood_drop",
    "g2", "g3", "g4", "g2a", "g3a", "g4a",
    "noise1", "noise2", "noise3", "noise4",
    "vignette", "veinmask", "veinmaskb",
}

# What each of the game's own portrait bundles is for, in the words a modder needs.
BUNDLE_NOTES = {
    "nm": "Monika during Natsuki's death",
    "sm": "Sayori during Monika's death",
    "sn": "Sayori during Natsuki's death",
    "sy": "Sayori during Yuri's death",
    "ym": "Yuri during Monika's death",
    "ny": "Nat and Yuri sharing a pose",
    "nf1": "Friend 1 (unused club member)",
    "nf2": "Friend 2 (unused club member)",
    "y_kill": "Yuri's final scene",
    "stab": "Stabbing scene",
    "stab_censor": "Stabbing scene, censored overlay",
    "oneeye2_base": "One-eyed glitch",
    "yuri_eyesfull": "Yuri, full eyes",
    "yuri_hack": "Yuri, glitched",
}

TITLE_NAMES = {
    "1": "Just Monika", "2": "Sad Monika", "2g": "Glitchy Monika", "2g3": "Monika, glitched",
    "3": "Poem", "3g": "Poem, glitched", "3g2": "Poem, glitched (alt)",
    "4": "Club", "4g": "Club, glitched",
    "5": "Club activity", "5_monika": "Club activity, Monika",
    "5_natsuki": "Club activity, Natsuki", "5_sayori": "Club activity, Sayori",
    "5_yuri": "Club activity, Yuri", "5_yuri2": "Club activity, Yuri (alt)",
    "5_ghost": "Ghost theme", "6": "Poem minigame", "6g": "Poem minigame, glitched",
    "6o": "Poem minigame, orange", "6r": "Poem minigame, red",
    "6s": "Poem minigame, Sayori", "7": "Reading", "7g": "Reading, glitched",
    "7g2": "Reading, glitched (alt)", "8": "Choices", "9": "Sleep", "9g": "Sleep, glitched",
    "10": "Finale", "10-yuri": "Finale, Yuri",
    "Candy_Hearts": "Candy Hearts (Plus)", "Candy_Heartz": "Candy Hearts",
    "Candy_Heartz_First_Time": "Candy Hearts, first time",
    "credits": "Credits", "d": "Dramatic sting", "d-ost": "Dramatic ostinato",
    "end-voice": "Ending voice", "g1a": "Glitch", "g2": "Glitch (alt)",
    "ghostmenu": "Ghost menu", "heartbeat": "Heartbeat",
    "m1a": "Main theme, part A", "monika-end": "Monika, ending",
    "monika-start": "Monika, beginning",
    "s_kill_early_bgm": "Suicide, early",
}


def load_sizes(path):
    """asset name -> (width, height, internal path)"""
    sizes = {}
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("  ", 2)
            if len(parts) < 3:
                continue
            name, size, asset_path = parts
            match = re.match(r"(\d+)x(\d+)$", size)
            if not match:
                continue
            sizes[name] = (int(match.group(1)), int(match.group(2)), asset_path.strip())
    return sizes


def load_art_index(art_dir):
    """asset name -> png path"""
    index = {}
    for bundle in sorted(os.listdir(art_dir)):
        folder = os.path.join(art_dir, bundle)
        if not os.path.isdir(folder):
            continue
        for name in sorted(os.listdir(folder)):
            if not name.lower().endswith(".png"):
                continue
            index[name[:-4]] = os.path.join(folder, name)
    return index


def load_bundles(path):
    """bundle name -> [asset names], from the dump the game wrote"""
    bundles = defaultdict(list)
    current = None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if line.startswith("## bundle "):
                current = line.split()[2]
            elif line.startswith("## "):
                current = None
            elif current and line.strip():
                name = line.strip()
                if name.endswith("[audio]"):
                    name = name[: -len("[audio]")].strip()
                bundles[current].append(name)
    return bundles


def load_audio(path):
    sections = []
    current = None
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if line.startswith("## "):
                current = {"bundle": line[3:].split()[0], "items": []}
                sections.append(current)
            elif current and line.strip():
                current["items"].append(line.strip())
    return sections


def load_characters(path):
    rows = []
    capture = False
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if line.startswith("## characters"):
                capture = True
                continue
            if capture and line.startswith("## "):
                break
            if capture and line.strip():
                rows.append(line)
    return rows


def load_segments(path):
    rows = []
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            if "\t" not in line:
                continue
            name, size = line.rstrip("\n").split("\t", 1)
            rows.append((name, int(size)))
    return sorted(rows)


def load_poses(path):
    """pose name -> [part names], from the dump the game wrote"""
    poses = defaultdict(list)
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            for line in handle:
                if line.startswith("#") or not line.strip():
                    continue
                parts = [p.strip() for p in line.split("|")]
                if len(parts) < 2:
                    continue
                if parts[1] not in poses[parts[0]]:
                    poses[parts[0]].append(parts[1])
    except FileNotFoundError:
        pass
    return poses


def thumbnail(source, target, max_h=260, max_w=340):
    """Write a small preview; returns (width_px, height_px) or None on failure."""
    try:
        with Image.open(source) as image:
            image = image.convert("RGBA")
            image.thumbnail((max_w, max_h), Image.LANCZOS)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            image.save(target, "PNG", optimize=True)
            return image.size
    except Exception:
        return None


def style_document(document, title):
    normal = document.styles["Normal"]
    normal.font.name = "Calibri"
    normal.font.size = Pt(9)

    heading = document.add_heading(title, 0)
    heading.alignment = WD_ALIGN_PARAGRAPH.LEFT


def add_table(document, headers, widths=None):
    table = document.add_table(rows=1, cols=len(headers))
    table.style = "Table Grid"
    table.alignment = WD_TABLE_ALIGNMENT.LEFT
    table.autofit = False
    for cell, text in zip(table.rows[0].cells, headers):
        cell.text = ""
        run = cell.paragraphs[0].add_run(text)
        run.bold = True
    if widths:
        for row in table.rows:
            for cell, width in zip(row.cells, widths):
                cell.width = Inches(width)
    return table


def monospace(cell, text, size=8):
    cell.text = ""
    run = cell.paragraphs[0].add_run(text)
    run.font.name = "Consolas"
    run.font.size = Pt(size)


def portrait_cells(cell, size, thumb, caption):
    """A cell holding a sprite preview above its name."""
    cell.text = ""
    paragraph = cell.paragraphs[0]
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    if thumb:
        run = paragraph.add_run()
        run.add_picture(thumb, height=Inches(1.35))
        paragraph.add_run("\n")
    caption_run = paragraph.add_run(caption)
    caption_run.font.size = Pt(8)
    caption_run.bold = True
    subtitle = paragraph.add_run("\n" + size)
    subtitle.font.size = Pt(7)
    subtitle.font.color.rgb = RGBColor(0x66, 0x66, 0x66)


def landscape(document):
    section = document.sections[0]
    section.orientation = WD_ORIENT.LANDSCAPE
    section.page_width, section.page_height = section.page_height, section.page_width
    return section


def write_pose_tables(document, names, poses, sizes, art_index, thumbs):
    """The poses of one bundle: what each one is called and what it is made of.

    A pose is put together from parts at run time, so there is no picture of a pose to
    show. What can be shown is the list of parts, and that is what a mod author needs:
    the parts are what `pose` moves and what `layer` replaces.
    """
    document.add_heading("Poses", level=2)
    table = add_table(document, ["Name to use in a script", "Made of", "Pixels"],
                      [2.6, 3.2, 1.2])
    for name in names:
        parts = poses.get(name)
        if not parts:
            continue
        row = table.add_row()
        monospace(row.cells[0], name, 8)
        monospace(row.cells[1], ", ".join(parts), 8)
        width, height, _ = sizes.get(name, (0, 0, ""))
        monospace(row.cells[2], f"{width}x{height}" if width else "-", 8)


def write_part_sheets(document, names, poses, art_index, thumbs, sizes):
    """The sheets the parts of a bundle are cut from, with a picture of each."""
    used = []
    for name in names:
        for part in poses.get(name, []):
            if part not in used:
                used.append(part)

    sheets = [part for part in used if part in art_index]
    if not sheets:
        return

    document.add_heading("Sheets the parts are cut from", level=2)
    document.add_paragraph(
        "Each sheet holds many frames; the game picks the one a pose needs."
    ).runs[0].font.size = Pt(9)

    columns = 4
    rows = (len(sheets) + columns - 1) // columns
    table = document.add_table(rows=rows, cols=columns)
    table.style = "Table Grid"
    for index, part in enumerate(sheets):
        cell = table.rows[index // columns].cells[index % columns]
        width, height, _ = sizes.get(part, (0, 0, ""))
        portrait_cells(cell, f"{width}x{height}" if width else "", thumbs.get(part), part)
    for index in range(len(sheets), rows * columns):
        cell = table.rows[index // columns].cells[index % columns]
        cell.text = ""


def write_sprite_sheets(document, art_index, sizes, thumbs, bundles, bundle_list, heading, poses):
    for bundle in bundle_list:
        names = [n for n in bundles.get(bundle, []) if n not in DETAILS]
        names = [n for n in names if n in art_index]
        if not names:
            continue

        document.add_heading(bundle, level=1)
        note = BUNDLE_NOTES.get(bundle)
        count = len(names)
        lead = f"{count} name(s)."
        if note:
            lead += "  " + note
        document.add_paragraph(lead).runs[0].font.size = Pt(9)

        posed = [n for n in names if n in poses]
        plain = [n for n in names if n not in poses]

        if posed:
            write_pose_tables(document, posed, poses, sizes, art_index, thumbs)

        if plain:
            document.add_heading("Other art", level=2)
            document.add_paragraph(
                "These are plain pictures rather than poses."
            ).runs[0].font.size = Pt(9)

            columns = 4
            rows = (len(plain) + columns - 1) // columns
            table = document.add_table(rows=rows, cols=columns)
            table.style = "Table Grid"

            for index, name in enumerate(plain):
                cell = table.rows[index // columns].cells[index % columns]
                width, height, _ = sizes.get(name, (0, 0, ""))
                portrait_cells(cell, f"{width}x{height}", thumbs.get(name), name)

            for index in range(len(plain), rows * columns):
                cell = table.rows[index // columns].cells[index % columns]
                cell.text = ""

        write_part_sheets(document, names, poses, art_index, thumbs, sizes)


def build_sprites(art, data, thumbs_dir, out_dir):
    sizes = load_sizes(os.path.join(data, "asset-sizes.txt"))
    art_index = load_art_index(art)
    bundles = load_bundles(os.path.join(data, "asset-reference.txt"))
    poses = load_poses(os.path.join(data, "pose-reference.txt"))

    document = Document()
    landscape(document)
    style_document(
        document,
        "DDLC Plus - Character sprites",
    )
    document.add_paragraph(
        "Every name below is used exactly as written in a script:\n"
        "    show monika 1 at left\n"
        "    show natsuki 0\n"
        "    pose monika 1 at left\n"
        "    layer monika 1 a monika 5a\n\n"
        "Positions: left, center, right, offscreenleft, offscreenright."
    )
    document.add_paragraph(
        "A pose is not a single picture. The game puts it together at run time out of\n"
        "parts - the body, the eyes, the mouth - and every pose of a character is cut\n"
        "from the same sheets. So there is no picture of a pose here: what is listed is\n"
        "what each pose is made of, and the sheets at the end of each character show\n"
        "those parts.\n\n"
        "That is also what makes a pose useful to a mod. `pose` takes a pose over, so it\n"
        "can be moved and faded, and `layer` replaces one of its parts with other art\n"
        "the game ships."
    )

    missing = 0
    thumbs = {}
    for name, path in art_index.items():
        if name in DETAILS:
            continue
        target = os.path.join(thumbs_dir, name + ".png")
        if thumbnail(path, target):
            thumbs[name] = target
        else:
            missing += 1

    write_sprite_sheets(document, art_index, sizes, thumbs, bundles,
                        [b for b in CHARACTER_BUNDLES],
                        "Main characters", poses)
    write_sprite_sheets(document, art_index, sizes, thumbs, bundles,
                        EXTRA_SPRITE_BUNDLES,
                        "Cut scene and death poses", poses)

    out = os.path.join(out_dir, "01-Character-Sprites.docx")
    document.save(out)
    return out, len(thumbs), missing


def build_images(art, data, thumbs_dir, out_dir, bundles_wanted, title, intro, number):
    sizes = load_sizes(os.path.join(data, "asset-sizes.txt"))
    art_index = load_art_index(art)

    document = Document()
    landscape(document)
    style_document(document, title)
    document.add_paragraph(intro)

    table = document.add_table(rows=1, cols=3)
    table.style = "Table Grid"
    for cell, text in zip(table.rows[0].cells, ["Preview", "Name to use in script", "Pixels"]):
        cell.text = ""
        run = cell.paragraphs[0].add_run(text)
        run.bold = True

    count = 0
    for bundle in bundles_wanted:
        folder = os.path.join(art, bundle)
        if not os.path.isdir(folder):
            continue

        document.add_heading(bundle, level=1)
        for file_name in sorted(os.listdir(folder)):
            if not file_name.lower().endswith(".png"):
                continue
            # The script name has no extension, but the file on disk does.
            name = file_name[:-4]
            source = os.path.join(folder, file_name)
            target = os.path.join(thumbs_dir, bundle + "__" + name + ".png")
            if not thumbnail(source, target, max_h=200, max_w=420):
                continue

            row = table.add_row()
            cell = row.cells[0]
            cell.text = ""
            cell.paragraphs[0].alignment = WD_ALIGN_PARAGRAPH.CENTER
            cell.paragraphs[0].add_run().add_picture(target, height=Inches(1.2))
            monospace(row.cells[1], name, 9)
            width, height, _ = sizes.get(name, (0, 0, ""))
            monospace(row.cells[2], f"{width}x{height}" if width else "-", 8)
            count += 1

    out = os.path.join(out_dir, number)
    document.save(out)
    return out, count


def build_audio(data, out_dir):
    sections = load_audio(os.path.join(data, "audio-paths.txt"))
    document = Document()
    style_document(
        document,
        "DDLC Plus - Music and sound effects",
    )
    document.add_paragraph(
        "Sound is addressed by file path, not by a short name:\n"
        "    play \"bgm/9.ogg\"\n"
        "    play \"sfx-coarse/poem-glitch_1.ogg\"\n\n"
        "Looping music:\n"
        "    play \"bgm/2.ogg\" loop\n\n"
        "Stop it with:\n"
        "    stop music\n\n"
        "Sound effects:\n"
        "    play \"sfx-coarse/page-turn.ogg\"\n\n"
        "The folder part of the path is what decides the channel: anything under bgm/\n"
        "is music, anything under sfx-coarse/ is a sound effect. Music keeps playing\n"
        "until it is stopped or another track replaces it."
    )

    table = add_table(document, ["Path to use in a script", "Track", "File"], [3.2, 2.4, 1.4])
    count = 0
    for section in sections:
        document.add_heading(section["bundle"], level=1)
        for asset in section["items"]:
            name = asset.split("/")[-1]
            relative = asset.split("/audio/", 1)[-1]
            track = name[:-4] if name.lower().endswith(".ogg") else name
            is_music = relative.startswith("bgm/")

            row = table.add_row()
            monospace(row.cells[0], '"' + relative + '"', 8)
            row.cells[1].text = ""
            title = TITLE_NAMES.get(track)
            if is_music:
                row.cells[1].text = title or "Music"
                row.cells[1].paragraphs[0].runs[0].font.size = Pt(9)
            else:
                monospace(row.cells[1], track, 8)
            row.cells[2].text = ""
            kind = row.cells[2].add_paragraph("music" if is_music else "sound")
            kind.runs[0].font.size = Pt(8)
            count += 1

    out = os.path.join(out_dir, "04-Music-and-Sound-Effects.docx")
    document.save(out)
    return out, count


def build_speakers(data, out_dir):
    rows = load_characters(os.path.join(data, "asset-reference.txt"))
    document = Document()
    landscape(document)
    style_document(document, "DDLC Plus - Speakers and dialogue")

    document.add_paragraph("Who can talk")
    table = add_table(document, ["Tag", "Used as", "Notes"], [1.4, 2.6, 4.5])
    notes = {
        "m": "Monika. The default speaker.",
        "s": "Sayori.",
        "y": "Yuri.",
        "n": "Natsuki.",
        "mc": "The player. Shows as 'Player' in the name box.",
        "narrator": "No name box, no portrait. Plain narration.",
        "ny": "Nat and Yuri together.",
        "nf1": "Friend 1, a club member that is never used.",
        "nf2": "Friend 2, a club member that is never used.",
    }
    for line in rows:
        parts = [p.strip() for p in line.split("|")]
        if len(parts) < 3:
            continue
        tag = parts[0]
        name = parts[1].replace("name=", "").strip()
        image = parts[2].replace("image=", "").strip()

        row = table.add_row()
        monospace(row.cells[0], tag, 10)
        row.cells[1].text = name or "(no name box)"
        row.cells[1].paragraphs[0].runs[0].font.size = Pt(9)
        row.cells[2].text = notes.get(tag, "")
        row.cells[2].paragraphs[0].runs[0].font.size = Pt(9)

    document.add_heading("How a line of dialogue is written", level=1)
    for line, kind in [
        ('m "Hi, it is me, Monika."', "A character speaks."),
        ('"The room is quiet."', "Narration: no name box."),
        ('m happy "Like this!"', "With an expression after the tag."),
        ('player "This is me."', "The player character."),
        ("$ points = points + 1", "Change a variable."),
        ('if points > 3:', "A condition."),
        ("    jump chapter2", "Go to a label."),
        ("else:", "Otherwise."),
    ]:
        paragraph = document.add_paragraph()
        run = paragraph.add_run(line)
        run.font.name = "Consolas"
        run.font.size = Pt(9)

    document.add_paragraph(
        "The expression after the speaker tag chooses a pose, and the names for those\n"
        "expressions are the sprite names in 01-Character-Sprites.docx."
    )

    out = os.path.join(out_dir, "05-Speakers-and-Dialogue.docx")
    document.save(out)
    return out, len(rows)


def describe_segment(name):
    """Turn a bundle name into something a mod author can read."""
    clean = name.replace("label ", "").strip()
    label = clean.startswith("label ")
    clean = clean[5:].strip() if label else clean

    text = clean.replace("_", " ").strip()
    if not text:
        return "", ""
    return text[0].upper() + text[1:], ("label" if label else "script")


def build_hooks(data, out_dir):
    segments = load_segments(os.path.join(data, "script-segments.tsv"))
    document = Document()
    landscape(document)
    style_document(document, "DDLC Plus - Where a mod can be injected")

    document.add_paragraph(
        "The game is split into named script segments. A mod label can take the place of\n"
        "one of them, or jump to one, which is how a mod changes a specific moment rather\n"
        "than replacing the whole story.\n\n"
        "Jump into a moment from your own mod:\n"
        "    jump ch1_start\n\n"
        "Or take a moment over completely:\n"
        "    label ch1_start:\n"
        "        \"My own version of this scene.\"\n\n"
        "Names are listed below exactly as they are stored. Those starting with 'label'\n"
        "are entry points you can jump straight to."
    )

    labels = [s for s in segments if s[0].startswith("label ")]
    scripts = [s for s in segments if not s[0].startswith("label ")]

    for heading, rows in [("Entry points (jump straight to these)", labels),
                          ("Script segments", scripts)]:
        document.add_heading(f"{heading} - {len(rows)}", level=1)
        table = add_table(document, ["Name", "Reads as", "Kind", "Size"],
                          [3.2, 3.4, 1.2, 1.2])
        for name, size in rows:
            reads, kind = describe_segment(name)
            row = table.add_row()
            monospace(row.cells[0], name, 8)
            row.cells[1].text = reads
            row.cells[1].paragraphs[0].runs[0].font.size = Pt(9)
            row.cells[2].text = kind
            row.cells[2].paragraphs[0].runs[0].font.size = Pt(8)
            size_text = f"{size / 1048576:.1f} MB" if size > 1048576 else f"{max(size // 1024, 1)} KB"
            row.cells[3].text = size_text
            row.cells[3].paragraphs[0].runs[0].font.size = Pt(8)

    out = os.path.join(out_dir, "06-Injection-Points.docx")
    document.save(out)
    return out, len(segments)


def main():
    if len(sys.argv) < 4:
        print(__doc__)
        return 1

    art, data, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
    thumbs_dir = os.path.join(out_dir, "thumbnails")
    os.makedirs(thumbs_dir, exist_ok=True)
    os.makedirs(out_dir, exist_ok=True)

    results = []

    print("character sprites...")
    results.append(build_sprites(art, data, thumbs_dir, out_dir))

    print("backgrounds...")
    results.append(build_images(
        art, data, thumbs_dir, out_dir, ["bg"],
        "DDLC Plus - Backgrounds",
        "Every name below is used exactly as written in a script:\n"
        "    scene bg_class_day\n"
        "    scene bg_bedroom\n\n"
        "Names ending in _original are the unmasked, full-resolution artwork, which is\n"
        "usually what a mod wants. The others are the version the game displays.",
        "02-Backgrounds.docx"))

    print("cut scenes...")
    results.append(build_images(
        art, data, thumbs_dir, out_dir, ["cg"],
        "DDLC Plus - Cut scenes and CG",
        "Cut scene art, shown like a background or a sprite:\n"
        "    show n_cg1_base at center\n"
        "    scene s_kill\n\n"
        "A CG with a _bg part is the layer behind it; _exp parts are the expressions\n"
        "layered on top.",
        "03-Cut-Scenes.docx"))

    print("audio...")
    results.append(build_audio(data, out_dir))
    print("speakers...")
    results.append(build_speakers(data, out_dir))
    print("injection points...")
    results.append(build_hooks(data, out_dir))

    print()
    for result in results:
        path, count = result[0], result[1]
        print(f"  {os.path.basename(path):<34} {count} row(s)  "
              f"{os.path.getsize(path) / 1048576:.1f} MB")

    return 0


if __name__ == "__main__":
    sys.exit(main())