# Engraving centerlines for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1.06 · 2026-11-13** — K uses one straight stem and a separate sharp checkmark with a gap. In 4, retain straight triangle sides and the continuous long crossbar, separating the crossing stroke. Improve recognition of short strokes and slightly nonparallel walls. Tolerance remains inherited from the operation.

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
- Stroke junctions become independent chains separated by small gaps. A plus uses two elbows without a shared center; K uses one straight stem and a separate sharp two-line checkmark. P loop returns and arcs in Phi stop short of the through-stroke. At a 4 crossing, prefer the longer of equally straight through-strokes and stop the other arms short on both sides. Connections depend on geometry, not character recognition. Standalone rings without branches remain closed. Short terminal branches towards outline corners are pruned.
- Away from separated junctions, parallel straight outline sides produce an exact midline. Ordinary corners without branches are retained. At junctions, keep the through-stroke untrimmed where possible and separate the other arms. The target gap is **0.02 mm**. If it cannot fit, retry that region at **0.01 / 0.005 / 0.004 mm**; the result reports the selected values. Set CAM tolerance in the operation itself. Actual outline fillets remain curved; other portions are fitted with cubic splines. Checks cover containment, self-intersections and contacts between chains.
- Slightly nonparallel straight walls use their internal angle bisector. Short strokes are checked for visible opposite walls even near a terminal cap. Recovered straight runs stay lines during subsequent spline fitting.
- At acute triangular stroke corners, such as in 4, use the angle between the sides. Remove the false branch towards the outer vertex only after verifying two pairs of opposite straight walls. Their center axes meet at a sharp triangle corner, preserving real stem and crossbar extensions. This is a geometric check, not character-template substitution.
- Straight-axis snapping can collapse two junctions while leaving a false out-and-back connector. Remove that connector only when both ends coincide at a shared node and the entire connector fits within the stroke without enclosing a hole. This rule retains genuine loops and independent short strokes.
- A filled circular dot becomes a true concentric circle with half the face diameter: Ø0.6 mm face → Ø0.3 mm curve. This is a geometric convention, not a cutter-radius compensation calculation.
- Curves are named `PM_ENGRAVING_CENTER`. Source bodies and faces remain unchanged. Output is not associative; recreate it after changing the model.

## Boundaries and reruns

Each chain is added as a separate **NXOpen BoundarySetPlanarMill**. After appending curves, **Open**, **Tool Position = On**, **Stock = 0** are explicitly set. New boundaries and members have **Custom Tolerance** disabled and inherit the operation tolerance. The script neither reads nor writes nor compares numeric Intol / Outtol values; the V1.04 tolerance rollback check has been removed. Member settings are read back through a fresh builder after Commit. An open chain must remain Open. For an actually closed ring or D/2 circle, the returned Closed type is accepted only if curve membership and tool position are preserved; an added closing member is rejected. Existing local boundaries are retained and their count is reported. Cutter compensation, depths, feeds, tool and other operation parameters are not reset. Use **Cut Pattern = Profile**. Operation-level path smoothing and compensation remain active and require inspection; their values are not changed by the script. Different curve and cutting-level heights can also look offset in a perspective view.

**When upgrading from V1.01–V1.05, recreate any retained previous axes:** undo that run or remove its curves from the operation boundaries and model. Previous results are not automatically replaced; the journal reports that they use the earlier algorithm.

An unchanged face group processed by the current algorithm is skipped. Changed source geometry, changed generated boundary membership or a partially selected previous group causes an explanatory stop. For a recalculation, remove the previous axes from the operation and model, or undo the previous run. Repeat detection uses attributes on generated curves; no attributes are added to source faces.

Operations inheriting existing boundaries first require local boundary geometry. Errors after the edit starts trigger a single Undo rollback of generated curves and new boundaries.

## Limits

- Supports planar engraving with planar bottom faces. V grooves without a flat bottom, cylindrical surfaces, different-depth inscriptions and side-wall-only selections are unsupported. A single planar side wall cannot be distinguished automatically from a bottom face; selecting the intended faces is the user's responsibility.
- Numerical medial-axis reconstruction does not recover the exact original font path. Variable stroke widths, serifs, acute corners and short strokes can produce extra branches or different endpoints; inspect the actual model result.
- Fitting parameter: 0.002 mm; outline sampling: 0.0002 mm; endpoint joining: 0.000001 mm. Values are converted for inch parts. These are internal geometry calculation parameters, not CAM tolerance overrides. They do not guarantee the overall medial reconstruction error.
- Under-resolved, degenerate, excessively complex or disconnected results are rejected before editing. Maximum 10000 boundary samples per region; accuracy is never silently reduced.
- If even the minimum 0.004 mm gap cannot fit inside a stroke, consumes a genuine short branch or leaves an intersection, reject the calculation without silently skipping that branch. This is geometric clearance: it does not account for cutter diameter or guarantee continuous engraved coverage.
- The D/2 rule is not automatically applied to noncircular dots.
- No automatic toolpath generation, PRT save, files, logs, INI files or new operations. Creates the model curves, one shared boundary plane and the operation boundaries.

## Validation

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. The 122 centerline scenarios and 21 chain/plane checks remain enabled. A total of 381 routing scenarios cover junction separation, retained strokes, rotation, slant, reflection, mm/inch units, standalone rings, D/2 dots and rejection of intersections.

The 36 new acute-4 variants check exactly three straight triangle sides, their known corner locations, retention of both external stroke extensions, no extra branches and separated routed paths. This regression reproduces the failure on V1.04. A separate case retains a genuine long pointed arm. The 118 NXOpen builder contract checks cover Open / On, zero stock, retained boundaries, tolerance inheritance, rejection of any numeric tolerance read or write, and failure handling.

The 192 added K/4 scenarios include quantized outline vertices, arbitrary rotations, slant, reflection, smaller letters and mm/inch units. They require one line plus a two-line checkmark for K, and a three-line triangular path with a continuous crossbar plus a separate short stub for 4. They also check the corners, terminal endpoints and separation. V1.05 fails the new shape regression.

The contract double does not validate native NX behavior. A full interactive V1.06 run on the user's STEP/PRT model and its resulting toolpath has not yet been tested. Compilation and algorithm tests do not replace that step.
