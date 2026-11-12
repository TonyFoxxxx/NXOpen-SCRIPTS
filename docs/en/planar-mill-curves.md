# Engraving centerlines for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1.02 · 2026-11-13** — Straight stroke axes and sharp intersections between parallel outline sides are preserved. Engraving boundaries use Open / On, including complete loops and D/2 circles. New boundaries and members receive a local 0.001 mm tolerance, checked again after committing the operation.

Source and working filename remain [NX_PlanarMill_Select_Curves.cs](../../scripts/NX_PlanarMill_Select_Curves.cs). Existing buttons need no change. The updater entry is now **«Оси гравировки для PLANAR_MILL»**. No Windows font or original text is required.

## Workflow

1. Import the SolidWorks model, for example through STEP. Letters must have selectable planar groove-bottom faces.
2. Configure **PLANAR_MILL** with the desired tool, profile machining, orientation, cut levels and non-cutting moves. An operation without previous boundaries is convenient for the first test. Close its editing dialog.
3. Select **one PLANAR_MILL** in Operation Navigator and run the existing `NX_PlanarMill_Select_Curves.cs`.
4. Select the planar **bottom faces of letters and round dots**. Do not select the overall front face of the part or groove side walls. All selected faces must lie in one plane.
5. Accept the selection. The script creates standalone curves in the work CAM part and appends local **Part Boundary** geometry to the operation.
6. Inspect the centerlines, boundaries, depth and tool orientation. Generate and verify the toolpath with NX. **Ctrl+Z** undoes the whole run.

For assembly components, the CAM part must be both the work part and the displayed part. Component and CAM-part length units must match. Component placement is applied when reading geometry.

## Geometry

- Approximate medial curves are calculated from outer and inner face loops. No text recognition or replacement font is involved.
- Adjacent selected faces are joined across shared edges. Internal dividing edges are excluded.
- Separate letters are not bridged across gaps. Circular arrangements in a common plane retain their positions.
- Letter loops are retained; stroke junctions become separate branches without assigning any generated curve twice. Short terminal branches towards outline corners are pruned.
- Parallel straight outline sides produce an exact midline. Sharp joins between these strokes retain their line intersection, including rotated letters. Actual outline fillets remain curved. Other portions are fitted with cubic splines. The output is checked to stay inside the selected region without crossing holes.
- A filled circular dot becomes a true concentric circle with half the face diameter: Ø0.6 mm face → Ø0.3 mm curve. This is a geometric convention, not a cutter-radius compensation calculation.
- Curves are named `PM_ENGRAVING_CENTER`. Source bodies and faces remain unchanged. Output is not associative; recreate it after changing the model.

## Boundaries and reruns

New boundaries use **type Open** and members use **Tool Position = On**. Open selects the CAM profile condition; geometrically closed letters and dot circles remain complete without a gap or an added closing segment. **Stock = 0** and **Intol / Outtol = 0.001 mm** are explicitly enabled on both boundaries and members, with conversion for inch parts. These settings and boundary membership are checked before and after committing the operation. Existing local boundaries are retained and their count is reported. Cutter compensation, depths, feeds, tool and other operation parameters are not reset. Use **Cut Pattern = Profile**. Operation-level path smoothing and compensation remain active and require inspection; local boundary tolerance does not override them. Different curve and cutting-level heights can also look offset in a perspective view.

**When upgrading from V1.01, recreate its previous axes:** undo that run or remove its curves from the operation boundaries and model. Previous results are not automatically replaced; the journal reports that they use the earlier algorithm.

An unchanged face group processed by the current algorithm is skipped. Changed source geometry, changed generated boundary membership or a partially selected previous group causes an explanatory stop. For a recalculation, remove the previous axes from the operation and model, or undo the previous run. Repeat detection uses attributes on generated curves; no attributes are added to source faces.

Operations inheriting existing boundaries first require local boundary geometry. Errors after the edit starts trigger a single Undo rollback of generated curves and new boundaries.

## Limits

- Supports planar engraving with planar bottom faces. V grooves without a flat bottom, cylindrical surfaces, different-depth inscriptions and side-wall-only selections are unsupported. A single planar side wall cannot be distinguished automatically from a bottom face; selecting the intended faces is the user's responsibility.
- Numerical medial-axis reconstruction does not recover the exact original font path. Variable stroke widths, serifs, acute corners and short strokes can produce extra branches or different endpoints; inspect the actual model result.
- Fitting parameter: 0.002 mm; outline sampling: 0.0002 mm; endpoint joining: 0.000001 mm. Values are converted for inch parts. These values do not guarantee the overall medial reconstruction error.
- Under-resolved, degenerate, excessively complex or disconnected results are rejected before editing. Maximum 10000 boundary samples per region; accuracy is never silently reduced.
- The D/2 rule is not automatically applied to noncircular dots.
- No automatic toolpath generation, PRT save, files, logs, INI files or new operations. Only the requested model curves and operation boundaries are created.

## Validation

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. 122 geometry scenarios exercise straight strokes, rings, C/L/plus/Phi and two-loop shapes, short strokes, rotated text, reflection, inch units, holes, separate regions, fitted curves staying inside the outline, duplicate/zero-length segments and the D/2 convention. New checks cover exact straightness and corner intersections across 72 bend/rotation combinations, subdivided straight STEP edges, a closed square ring and preservation of an actual fillet. 70 checks exercise Open / On, stock and tolerance through a UF API contract double, including simulated settings loss at commit. The 21 existing chain and plane checks remain enabled. The contract double does not validate native NX behavior.

A full interactive run on the user's STEP/PRT model and its resulting toolpath has not yet been tested. Compilation and geometry-engine tests do not replace that step.
