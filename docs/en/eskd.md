# ESKD drawing formatting

[Русский](../eskd.md) | **English**

Script: [NX_ESKD_Format_GOST_A.cs](../../scripts/NX_ESKD_Format_GOST_A.cs). [All scripts](../../README.en.md#scripts).

Creates an ESKD border and title block on the current metric drawing sheet and updates annotation formatting. ESKD is the Unified System for Design Documentation used by GOST standards.

## Features

- A4 and larger metric sheets, with portrait/landscape choices. Standard A4 is portrait; a custom landscape option is available.
- Margins of 20/5/5/5 mm, a 185 × 55 mm title block and additional format boxes.
- Editable part number and descriptive name derived from the PRT filename on each launch; if it cannot split the name, it uses the full filename as the description.
- Material groups/grades or free text, organization and signature fields, mass, designation letter and sheet data.
- Repeated runs update the script's objects and hide old Siemens template objects.
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

The script can register the permitted font for the user and NX; restarting NX may be necessary. If the font is unavailable, formatting does not proceed. Automatic formatting does not verify the engineering correctness of dimensions, technical requirements or drawing layout.
