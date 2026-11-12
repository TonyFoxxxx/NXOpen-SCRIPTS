# Smooth single-stroke GOST A text

[Русский](../gost-smooth-text.md) | **English**

File: `NX_GOST_Smooth_Text.cs` · **V1** · 2026-11-12.

Creates model lines and cubic splines for later CAM selection. Rounded portions no longer use the coarse integer FON coordinate grid. Strokes have zero geometric width; cutter and depth determine the engraved width.

This creates independent curves, not an installed system font or an associative NX Text feature. To change an inscription, undo or delete its curves and create it again.

## Font source

Requires an external vector **GOST_A.FON**, GOST type A (plotter). Defaults to the Windows Fonts folder. The previously prepared **GOST A SingleLine 75 V1** also works through Browse; its measured shear is removed before applying exactly 15 degrees from vertical.

Prefer the original upright plotter font, which has not undergone additional oblique-coordinate rounding. Outline TTF/OTF, arbitrary FON files and the experimental R2 are not validated sources. Font bytes are not included in this repository, installed or modified. The font owner's usage terms apply.

## Use

1. Set the current **WCS XY plane** to the inscription plane. The journal uses WCS, not the CAM operation's MCS.
2. Run `NX_GOST_Smooth_Text.cs` with Tools → Journal → Play or a custom button.
3. Choose the FON, enter one line and the capital height in millimetres. Optionally rotate the whole line relative to WCS X. Letter slant is already included.
4. Click Create and indicate the line origin. The screen position projects onto WCS XY; it does not snap to model entities.
5. Review the text. Select an existing PLANAR_MILL, run [Curves for PLANAR_MILL](planar-mill-curves.md) and select the new curves with a rectangle.
6. Review boundaries, tool and cutting depth, then generate the toolpath normally.

Millimetres are converted in inch parts. One Ctrl+Z undoes creation. Handled construction errors roll back objects created by that run. No automatic save or toolpath generation.

## Geometry

- Preserves straight strokes and intentional corners. Reconstructs rounded sections with cubic curves sharing tangent directions at smooth joints.
- Creates each cubic directly from four poles without small-line tessellation.
- Preserves source nodes and stroke topology. Removes retraced source segments within each glyph.
- Available Cyrillic/Latin letters, digits and symbols depend on the source FON. An unsupported character stops creation before modifying the model.
- Curves are named `GOST_TEXT` and carry the inscription in `GOST_TEXT_SOURCE`. They belong to the work part.

This reconstructs plotter-font roundings. It is not an exact medial-axis conversion of arbitrary GOST_A.TTF outlines or certification of all GOST proportions. Source points and spacing preserve characteristics of the chosen font. Intentional corners remain angular.

## Validation and limits

Compiled against Designcenter 2606 libraries. Passed 10 scenarios covering font parsing, metrics, slant, tangency, corners, closure, zero segments and retraces. A separate local check processed 180 installed-source glyphs into 1297 entities, including 687 cubic segments.

Supports one line up to 250 characters. Does not change existing Text features, sketches or operations. Requires a legacy vector FON 2.0 with byte coordinates. Raster/outline fonts, Unicode without corresponding Windows-1251 geometry and text on nonplanar surfaces are unsupported.

Interactive NX execution and machining of the resulting curves are not yet confirmed: the background validation process failed before running the journal. First check a short inscription in a test part.
