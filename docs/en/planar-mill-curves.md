# Engraving centerlines for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1.13 · 2026-11-14** — Closed chains and dot circles now use Boundary Type = Closed in Part Boundaries; open strokes and chains with junction gaps use Open. Set the type before and after appending curves, then verify the exact type after committing the operation.

Source and working filename remain [NX_PlanarMill_Select_Curves.cs](../../scripts/NX_PlanarMill_Select_Curves.cs). Existing buttons need no change. The updater entry is now **«Оси гравировки для PLANAR_MILL»**. No Windows font or original text is required.

## Workflow

1. Import the SolidWorks model, for example through STEP. Letters must have selectable planar groove-bottom faces.
2. Configure **PLANAR_MILL** with the desired tool, profile machining, orientation, cut levels and non-cutting moves. An operation without previous boundaries is convenient for the first test. Close its editing dialog.
3. Select **one PLANAR_MILL** in Operation Navigator and run the existing `NX_PlanarMill_Select_Curves.cs`.
4. Select the planar **bottom faces of letters and round or square dots**. Do not select the overall front face of the part, the solid islands inside letters or groove side walls. All selected faces must lie in one plane.
5. Accept the selection. The script creates standalone curves in the work CAM part and appends local **Part Boundary** geometry to the operation.
6. Inspect the centerlines, boundaries, depth and tool orientation. Generate and verify the toolpath with NX. **Ctrl+Z** undoes the whole run.

For assembly components, the CAM part must be both the work part and the displayed part. Component and CAM-part length units must match. Component placement is applied when reading geometry.

## Geometry

- First find visible pairs of straight walls. Build their center axes and intersection graph without character identities, font names or orientation rules. Rectangular, angled and branching strokes share the same method. Short unmatched outline runs can be end caps. Accept the reconstruction only after wall-coverage, connectivity, hole-count and area-consistency checks.
- This supported outline bypasses skeletonization: a sharp corner is constructed directly as the intersection of two stroke axes. No terminal branch towards the outer outline vertex is created. Moderately tapering walls use their internal angle bisector. A short edge is accepted as a cap only when it meets both walls of the same stroke; a bevel between neighbouring strokes does not create an extra endpoint.
- If straight wall pairs do not describe the complete outline, use approximate medial reconstruction with straight-run restoration and cubic fitting for curved portions. This is a hybrid geometric method; the original path of an arbitrary font is not always uniquely recoverable.
- Medial reconstruction checks false leaves at convex corners and bevels. Removal requires support from two adjacent walls or proof that the walls continue along neighbouring strokes; short length alone is insufficient. Collinear boundary pieces are merged first so subdividing STEP edges does not change this classification. Genuine short arms are retained.
- Recheck false leaves after merging nearby nodes, when both continuing strokes are visible. Remove a corner-snapping artifact only after explicit endpoint contraction and a local face check; reconnect its two remaining incident pieces as one stroke. On faces with holes the removed loop must fit inside a boundary-free disk, so a real island cannot be discarded.
- At a straight/curved stroke joint with a confirmed sharp inner outline vertex, locally continue the curved wall and measure the midline to the actual opposite wall. Intersect that midline with the reconstructed straight axis. Circular and noncircular walls are supported; validate the local fit, containment and splice to the remaining curve. Genuine tangential fillets are not converted into corners. Protect the reconstructed corner from subsequent fitting and terminal trimming. Insufficient geometric evidence retains the previous medial reconstruction.
- Adjacent selected faces are merged across shared edges; separate letters are not connected. Circular text placement in a common plane is retained.
- CAM junction separation follows axis reconstruction. K has a straight stem and a separate sharp checkmark. In 4, preserve the straight triangular sides and longer through-crossbar, ending the crossing stroke short on both sides. A plus has two separate elbows; P loop returns and Phi arcs stop short of the through-stroke. Connections depend on geometry, with no character lookup table.
- Before junction routing, each free physical end is positioned half the local stroke width before the cap, measured along the terminal axis/tangent. A 0.4 mm stroke leaves 0.2 mm. Width comes from the supporting side walls or local cross-section, not cutter diameter. Existing medial setbacks are accounted for. Shared nodes, sharp corners and closed paths are preserved; new gap endpoints are not treated as physical terminals. If insufficient length remains, calculation stops without silently deleting the stroke.
- Target chain clearance is **0.02 mm**, retried at **0.01 / 0.005 / 0.004 mm** if needed. Validate containment, self-intersections and contacts between different chains. Independent rings remain closed. Set CAM tolerance in the operation. Trimming starts at the requested clearance plus the curve-check margin, instead of a blanket 1.5 times the gap. Oblique branches increase the trim only when measured separation is insufficient.
- A filled round dot becomes a concentric **D/2** circle: Ø0.6 mm face → Ø0.3 mm curve. A compact, slightly oval dot uses the area centroid and half the equal-area-circle diameter. Its radial shape check also uses the area radius, rather than the sampling-dependent mean of vertex radii. A compact square or near-square parallelogram without a hole becomes a central circle of diameter half its smaller width: a 0.6 mm square gives Ø0.3 mm. Italic shear and subdivided straight sides are supported. Elongated bars, other polygons and counters are not replaced by this rule. This is a geometric convention, not cutter compensation.
- Curves are named `PM_ENGRAVING_CENTER`. Source bodies and faces are retained. Output is not associative; recreate it after changing the model.

## Boundaries and reruns

Each chain is added as a separate **NXOpen BoundarySetPlanarMill**. Closed chains and dot circles explicitly use **Boundary Type = Closed**; open chains use **Open**. Set the type before and after appending curves. **Tool Position = On**, **Stock = 0**. New boundaries and members have **Custom Tolerance** disabled and inherit the operation tolerance. The script neither reads nor writes nor compares numeric Intol / Outtol values; the V1.04 tolerance rollback check has been removed. Member settings are read back through a fresh builder after Commit. After Commit the type must exactly match the geometry: Open for an open chain, Closed for a ring or D/2 circle. Either Closed-to-Open or Open-to-Closed mismatch triggers rollback. Source curve membership and tool position are also verified; reject added closing members. Existing local boundaries are retained and their count is reported. Cutter compensation, depths, feeds, tool and other operation parameters are not reset. Use **Cut Pattern = Profile**. Operation-level path smoothing and compensation remain active and require inspection; their values are not changed by the script. Different curve and cutting-level heights can also look offset in a perspective view.

**When upgrading from V1.01–V1.12, recreate any retained previous axes:** undo that run or remove its curves from the operation boundaries and model. Previous results are not automatically replaced; the journal reports that they use the earlier algorithm.

An unchanged face group processed by the current algorithm is skipped. Changed source geometry, changed generated boundary membership or a partially selected previous group causes an explanatory stop. For a recalculation, remove the previous axes from the operation and model, or undo the previous run. Repeat detection uses attributes on generated curves; no attributes are added to source faces.

Operations inheriting existing boundaries first require local boundary geometry. Errors after the edit starts trigger a single Undo rollback of generated curves and new boundaries.

A construction error reports the failed selected face group. Highlight its source faces while the message is visible, then clear the highlight when it closes. Stop before editing the operation, without silently skipping the failed character.

## Limits

- Supports planar engraving with planar bottom faces. V grooves without a flat bottom, cylindrical surfaces, different-depth inscriptions and side-wall-only selections are unsupported. A single planar side wall cannot be distinguished automatically from a bottom face; selecting the intended faces is the user's responsibility.
- Numerical medial-axis reconstruction does not recover the exact original font path. Variable stroke widths, serifs, acute corners and short strokes can produce extra branches or different endpoints; inspect the actual model result.
- Fitting parameter: 0.002 mm; outline sampling: 0.0002 mm; endpoint joining: 0.000001 mm. Values are converted for inch parts. These are internal geometry calculation parameters, not CAM tolerance overrides. They do not guarantee the overall medial reconstruction error.
- Under-resolved, degenerate, excessively complex or disconnected results are rejected before editing. Maximum 10000 boundary samples per region; accuracy is never silently reduced.
- If even the minimum 0.004 mm gap cannot fit inside a stroke, consumes a genuine short branch or leaves an intersection, reject the calculation without silently skipping that branch. This is geometric clearance: it does not account for cutter diameter or guarantee continuous engraved coverage.
- The D/2 rule is not automatically applied to elongated marks or dots of other cross sections.
- No automatic toolpath generation, PRT save, files, logs, INI files or new operations. Creates the model curves, one shared boundary plane and the operation boundaries.

## Validation

V1.13 passes 492 Part Boundaries contract checks: Open/Closed with single and multiple curves, opposite inference during AppendCurves, rejection of a mismatched saved type, member identities, Tool Position On, zero stock, inherited tolerances and restoration of initial settings on failure. These use an API double without running NX. The centerline, junction-routing and chain/plane cores are identical to V1.12.

Retain 582 scenarios for two-island contours with rounded outer corners and for retention of genuine closed rings. Assert branch and node counts, preservation of both islands, corner positions, coverage of original strokes after routing, and absence of intersections. Include three heights, two widths, three fillet radii, rotations, reflections, mm/inch units and different loop starting points. V1.11 fails the new regression. Additional routing checks retain the free endpoint of genuine 0.0045 and 0.005 mm branches with at least 0.004 mm clearance; a 0.003 mm branch is still rejected, never silently removed.

Retain 168 checks for sharp straight/circular and straight/noncircular joints, asymmetric walls and retention of tangential fillets. Include rotation, reflection, mm/inch units, reversed winding and uneven wall subdivision. Assert corner location, analytical axis deviation, terminal setbacks, continuity, sharp-joint count and containment. V1.10 fails the new corner-location regression.

Retain 360 junction checks: bevelled bends with different angles and wall taper, curved bands with varying subdivision density, straight-to-arc transitions, an unevenly sampled outline, and genuine short arms with flat and round caps. Assert branch counts, sharp two-line intersections, absence of reversing hooks, and containment. Include rotation, reflection, mm/inch units, a shifted starting vertex and subdivided straight edges. V1.09 fails the new bevelled-corner regression.

Retain 277 terminal/punctuation scenarios, 602 routing scenarios, 122 centerline scenarios, 492 CAM adapter contract checks and 21 chain/plane checks.

For a 0.4 mm wide bar, V1.08 leaves 0.02 mm before the cap; V1.09 leaves 0.2 mm. A 0.6×0.6 mm square now yields one Ø0.3 mm circle instead of a branched axis. Closed counters and previously tested round dots are retained.

For V1.12, all 1104 variants of 23 faces from the previously supplied STEP model passed outside NX. Spline sampling follows the journal: 16 initial intervals and adaptive subdivision. The source model is not included in the repository. For the new engraving shown in screenshots, synthetic contours exercise similar joint types; its STEP/PRT model was not supplied. These tests do not validate that actual new model or arbitrary fonts.

An additional local sweep of 456 outlines from four fonts produced 387 results and 69 rejections by existing terminal-setback checks. All three previous routing failures in that set were resolved with no new rejections. This does not establish universal font support. Font files and their outlines are not distributed.

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. **Interactive V1.13 execution and the resulting NX toolpath remain untested.** Geometry and contract tests do not validate native CAM behavior. Inspect the result and the normally generated NX toolpath before machining.
