# ESKD drawing formatting

[Русский](../eskd.md) | **English**

Script: [NX_ESKD_Format_GOST_A.cs](../../scripts/NX_ESKD_Format_GOST_A.cs). [All scripts](../../README.en.md#scripts).

Creates an ESKD border and title block on the current metric drawing sheet and updates annotation formatting. ESKD is the Unified System for Design Documentation used by GOST standards.

## Latest release

**V1.41 · 2026-10-23**

- Fixed the journal compilation error `SHA256 does not exist in the namespace System.Security.Cryptography`. Font verification resolves the system SHA-256 implementation at runtime without an additional NX compiler assembly reference.
- Retained size, CRC32 and SHA-256 checks before and after font repair. An unavailable system algorithm stops processing with an explicit error; verification is never skipped.
- Retained external `v110` to `v111` font metadata repair and reuse of an already corrected `v111`. Outlines, advance widths, hinting and the character map remain unchanged; the source copy is not overwritten. The font itself is not included in the public package.
- Formatting, chamfer spacing, three-place tolerances, paths and INI behavior are unchanged. No additional files, reports or logs are created.

Checked C# syntax, the catalog, SHA-256 execution without compiler references to cryptographic assemblies, font migration with synthetic data and rejection of damaged data. This revision was not executed in NX / Designcenter here. NX warnings about the obsolete surface-finish API may remain; this release does not change that API usage.

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
- Chamfer-separator spacing is 1/7 of text height. Linear nominal values and tolerances use 3 decimal places; stored tolerances are retained.
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

The script accepts compatible `NX_ESKD_GOST_A_Italic_v111.ttf` or the earlier `NX_ESKD_GOST_A_Italic_v110.ttf`; it repairs and installs `v111` automatically when given `v110`. If neither compatible font is available, formatting does not proceed. After updating, apply ESKD formatting to the drawing before using [PDF exporter V1.13](export-pdf.md). If NX requests a restart, save your work, restart NX and apply ESKD again. Previously created PDFs must be exported again. Automatic formatting does not verify the engineering correctness of dimensions, technical requirements or drawing layout.
