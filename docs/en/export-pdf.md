# Export the current drawing sheet to PDF

[Русский](../export-pdf.md) | **English**

Script: [NX_Export_Drawing_To_PDF.cs](../../scripts/NX_Export_Drawing_To_PDF.cs). [All scripts](../../README.en.md#scripts).

Exports the entire current sheet of the displayed part at its physical size, 1:1 in millimetres.

## Features

Live line-weight preview covers eight groups:

1. View and section contours.
2. Dimensions, leaders and symbols.
3. Centerlines and center marks.
4. Hatching.
5. Hidden lines.
6. ESKD border.
7. Section lines and arrows.
8. General surface-roughness symbol.

Available weights are 0.13, 0.18, 0.25, 0.35, 0.50, 0.70, 1, 1.4 and 2 mm. Defaults are selected for the sheet size on each launch. Colors are taken from NX, text is exported as text, and raster output uses high resolution.

## Use

1. Save the PRT, open the required sheet of the displayed part and run the journal.
2. Adjust line weights while inspecting the live preview.
3. Enter the PDF filename. It defaults to the project name; do not include a path.
4. Export and check the PDF in the saved project's folder.

The script adds `.pdf` and replaces an existing file with the same name.

## Files and restoration

Original annotation styles and line-weight display settings are restored after success, cancellation or error. If restoration fails after a successful export, the script warns you.

On a write failure, it attempts to restore the previous PDF from memory or delete an incomplete new file. It does not create an INI, persistent log or extra output folder, install fonts or automatically save the PRT.
