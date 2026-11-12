# Curves for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1 · 2026-11-12** — initial release. Adds a text feature, sketch or selected curves to an existing PLANAR_MILL. Connected chains become separate open or closed boundaries. New members use Tool Position **On** and zero Stock.

Source: [NX_PlanarMill_Select_Curves.cs](../../scripts/NX_PlanarMill_Select_Curves.cs).

## Workflow

1. Prepare planar text using a single-stroke font, or sketch curves. Disable bounding-box creation in a Text feature.
2. Create and configure a **PLANAR_MILL** operation: tool, profile machining method, depth, approach and retract. Close its editing dialog.
3. Select **one** PLANAR_MILL operation in Operation Navigator.
4. Run `NX_PlanarMill_Select_Curves.cs` using **Tools → Journal → Play**, or an assigned button.
5. Select the complete **Text** or **Sketch** feature in Part Navigator. Individual curves can also be selected together or with a selection rectangle in the graphics window. Accept the selection.
6. Review the reported curve/boundary counts, inspect the boundaries in the operation, and generate the toolpath using the normal NX command.

Selecting a whole sketch includes all its active curves; reference geometry is skipped. Selecting individual curves includes only those objects. Select the Text feature to include the entire inscription.

## Boundary behavior

- Curves connected at their endpoints are ordered into chains. Separate letters and strokes are not bridged across gaps.
- Branches become separate chains. Intersections inside a curve do not split the source geometry.
- Closed loops stay closed, including a circle or closed spline represented by one curve.
- Local **Part Boundary** geometry is appended to the selected operation. Existing local boundaries are preserved.
- Repeated objects are deduplicated by NX Tag. Objects already assigned to this operation are skipped. Geometrically coincident curves with different Tags are not automatically deleted or merged.
- New boundaries and members use Stock = 0; new members use Tool Position = On. Other operation parameters are not assigned by the journal.
- The plane is determined from the selected geometry. For a collinear selection, a valid plane is oriented relative to WCS. Align WCS with the intended engraving plane before running such a selection.

This journal does not convert outline TrueType text into single-stroke text. Outline source geometry remains outlines. It does not remove configured cutter compensation or choose an engraving depth.

## Limits and undo

- Supports lines, arcs, circles, ellipses and splines in the work part, all in one plane.
- Assembly occurrences, solid edges, Drafting Notes and nonplanar inscriptions are unsupported in V1.
- Operations inheriting existing parent boundaries are rejected before editing. First define local boundaries for that operation using NX.
- Endpoint joining and coplanarity use a tolerance of 0.000001 mm, converted for inch parts. Larger gaps leave chains separate.
- New boundary membership, open/closed state, tool position and stock are checked. Detected errors trigger rollback to the beginning of the edit.
- A successful run is undone with one **Ctrl+Z**.
- No automatic toolpath generation, PRT save, files, logs, INI files, copied curves or new CAM operations.

## Validation

The source compiles against Designcenter 2606 libraries. Chain and plane algorithms pass 21 scenarios covering separate strokes, branches, closed loops, reversed curves, gaps, spatial-cell boundaries, inclined planes and rejection of nonplanar geometry.

A complete interactive NX run and generation of the resulting toolpath are not yet confirmed: the background NX process in the validation environment exits before executing the journal. Compilation and automated checks do not replace this validation.
