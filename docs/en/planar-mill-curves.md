# Engraving centerlines for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1.03 · 2026-11-13** — Stroke junctions are separated into nonintersecting chains with at least 0.02 mm clearance. A plus uses two separate elbows, K uses a stem and diagonals, and loop returns in P/4/Phi stop short of the through-stroke. Boundaries are created through NXOpen BoundarySet; On, zero stock and 0.001 mm tolerance are checked after Commit.

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
- Stroke junctions become independent chains separated by small gaps. A plus uses two elbows without a shared center; K uses a stem and a separate diagonal pair. P loop returns, the crossbar in 4 and arcs in Phi stop short of the through-stroke. Connections depend on geometry, not character recognition. Standalone rings without branches remain closed. Short terminal branches towards outline corners are pruned.
- Away from separated junctions, parallel straight outline sides produce an exact midline. Ordinary corners without branches are retained. Junctions use local connections and branch trimming with **at least 0.02 mm clearance**. Actual outline fillets remain curved; other portions are fitted with cubic splines. Checks cover containment, self-intersections and contacts between chains.
- A filled circular dot becomes a true concentric circle with half the face diameter: Ø0.6 mm face → Ø0.3 mm curve. This is a geometric convention, not a cutter-radius compensation calculation.
- Curves are named `PM_ENGRAVING_CENTER`. Source bodies and faces remain unchanged. Output is not associative; recreate it after changing the model.

## Boundaries and reruns

Each chain is added as a separate **NXOpen BoundarySetPlanarMill**. After appending curves, **Open**, **Tool Position = On**, **Stock = 0** and **Intol / Outtol = 0.001 mm** are explicitly set, with conversion for inch parts. Member settings are read back through a fresh builder after Commit. An open chain must remain Open. For an actually closed ring or D/2 circle, the returned Closed type is accepted only if curve membership and tool position are preserved; an added closing member is rejected. Existing local boundaries are retained and their count is reported. Cutter compensation, depths, feeds, tool and other operation parameters are not reset. Use **Cut Pattern = Profile**. Operation-level path smoothing and compensation remain active and require inspection; local boundary tolerance does not override them. Different curve and cutting-level heights can also look offset in a perspective view.

**When upgrading from V1.01 or V1.02, recreate the previous axes:** undo that run or remove its curves from the operation boundaries and model. Previous results are not automatically replaced; the journal reports that they use the earlier algorithm.

An unchanged face group processed by the current algorithm is skipped. Changed source geometry, changed generated boundary membership or a partially selected previous group causes an explanatory stop. For a recalculation, remove the previous axes from the operation and model, or undo the previous run. Repeat detection uses attributes on generated curves; no attributes are added to source faces.

Operations inheriting existing boundaries first require local boundary geometry. Errors after the edit starts trigger a single Undo rollback of generated curves and new boundaries.

## Limits

- Supports planar engraving with planar bottom faces. V grooves without a flat bottom, cylindrical surfaces, different-depth inscriptions and side-wall-only selections are unsupported. A single planar side wall cannot be distinguished automatically from a bottom face; selecting the intended faces is the user's responsibility.
- Numerical medial-axis reconstruction does not recover the exact original font path. Variable stroke widths, serifs, acute corners and short strokes can produce extra branches or different endpoints; inspect the actual model result.
- Fitting parameter: 0.002 mm; outline sampling: 0.0002 mm; endpoint joining: 0.000001 mm. Values are converted for inch parts. These values do not guarantee the overall medial reconstruction error.
- Under-resolved, degenerate, excessively complex or disconnected results are rejected before editing. Maximum 10000 boundary samples per region; accuracy is never silently reduced.
- If the gap cannot fit inside a thin stroke, consumes a short branch or leaves an intersection, the calculation is rejected. This is geometric clearance: it does not account for cutter diameter or guarantee continuous engraved coverage.
- The D/2 rule is not automatically applied to noncircular dots.
- No automatic toolpath generation, PRT save, files, logs, INI files or new operations. Creates the model curves, one shared boundary plane and the operation boundaries.

## Validation

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. The 122 centerline scenarios and 21 chain/plane checks remain enabled. Added 59 junction-routing scenarios: plus, K, P, 4, Phi, H, E, T, rotations, reflection, inch units, a standalone ring and D/2 dot, and rejection of intersections and an undersized stroke. 102 NXOpen builder contract checks cover retained boundaries, settings reapplied after AppendCurves, separate chains, Open / On, tolerance, stock and failure handling.

The contract double does not validate native NX behavior. A full interactive run on the user's STEP/PRT model and its resulting toolpath has not yet been tested: the background NX attempt terminated before executing the journal. Compilation and algorithm tests do not replace that step.
