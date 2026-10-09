# ESKD drawing formatting

[Русский](../eskd.md) | **English**

Script: [NX_ESKD_Format_GOST_A.cs](../../scripts/NX_ESKD_Format_GOST_A.cs). [All scripts](../../README.en.md#scripts).

Creates an ESKD border and title block on the current metric drawing sheet and updates annotation formatting. ESKD is the Unified System for Design Documentation used by GOST standards.

## Latest release

**V1.37 · 2026-10-09**

- After Apply, the script deletes the previous border, title block, auxiliary boxes and their text on the current sheet, including hidden old Siemens template elements and formatting from earlier runs.
- It collects formatting objects first, then deletes containers, tables and remaining loose lines/labels. Cleanup after sheet refreshes finishes before the new border is created; a deletion failure rolls back this run.
- In the Material cell, the grade and GOST/OST/TU standard start on separate explicit lines. Longer entries, including strings without spaces, wrap further to fit the cell width; the font shrinks when the complete block is too tall.
- The final sheet check verifies that explicit line breaks survived. The 185 × 55 mm title block and 70 × 15 mm Material cell retain their sizes.
- Cancel remains read-only. INI handling, the external-font workflow and section geometry are retained; no new service files are created.

Checked C# syntax, short/long material wrapping, nonbreaking spaces and Unicode, border recognition on A4/A3/A2, deletion order and failure handling. Algorithm checks run without NX; actual object deletion and table rendering in the target NX / Designcenter builds still require in-application verification.

## Features

- A4 and larger metric sheets, with portrait/landscape choices. Standard A4 is portrait; a custom landscape option is available.
- Margins of 20/5/5/5 mm, a 185 × 55 mm title block and additional format boxes.
- A horizontal 70 × 14 mm upper duplicate-designation box, with text rotated by 180° in both sheet orientations.
- Long title text wraps to measured widths before trying smaller font sizes; cells keep their dimensions. Text that still cannot fit at 2.5 mm uses further NX fitting with a readability warning.
- Editable part number and descriptive name derived from the PRT filename on each launch; if it cannot split the name, it uses the full filename as the description.
- Material groups/grades or free text, with grade/standard line breaks and wrapping before font reduction; organization and signature fields, mass, designation letter and sheet data.
- Apply deletes the previous current-sheet border, title block, auxiliary boxes and their labels before creating the new format. It recognizes script object names, title-block containers and title/auxiliary areas. Tables outside these areas and drawing views are outside the cleanup scope.
- Existing drawing-view positions, scales and projection are retained; a sheet without views is set to first-angle projection.
- Optional numbered technical requirements. Each launch starts with three default items; base values can be reloaded from INI. An existing requirements block is updated.
- General surface roughness with Ra and a choice of parenthesized symbol.
- GOST A Italic text, sizes and spacing; line weights, dimension arrows/leaders, tables, view and section labels.
- Checks section letters, lines and arrow positions; retains hatch spacing/directions and supports symmetric tolerances.

Annotation style changes affect existing annotations on all sheets and future annotations in the part. The border and title block are created on the current sheet only.

## Use

1. Open the part as both work and displayed part and activate the required Drafting sheet.
2. Run the journal and check the fields.
3. Reload base INI values if required, then apply.
4. Inspect the drawing and save the PRT.

Cancel leaves the drawing unchanged. Successful choices are stored in `LastUsed` in the INI and in sheet attributes. Errors roll back NX changes; the PRT is not saved automatically.

## Configuration and limits

Use `NX_ESKD_Settings.ini` beside the script. The exact compatible external font is required; it is not bundled. See [font setup and requirements](configuration.md#external-font) and [third-party notices](../../THIRD_PARTY_NOTICES.en.md).

Cleanup also deletes loose labels and lines within the old title/auxiliary areas. Script-created technical requirements are handled separately; dimensions and view geometry are not deleted. A single Undo operation restores the drawing changes.

The script can register the permitted font for the user and NX; restarting NX may be necessary. If the font is unavailable, formatting does not proceed. Automatic formatting does not verify the engineering correctness of dimensions, technical requirements or drawing layout.
