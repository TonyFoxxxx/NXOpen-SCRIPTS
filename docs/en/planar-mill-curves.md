# Engraving centerlines for PLANAR_MILL

[Русский](../planar-mill-curves.md) | **English**

## Latest release

**V1.09 · 2026-11-14** — Free centerline ends now stop half the local stroke width before the terminal boundary. Both reconstruction engines measure the setback from the source outline, without shortening already inset ends twice. Square and near-square dots, including sheared italic shapes, become circles with a diameter equal to half their smaller width.

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
- This supported outline bypasses skeletonization: a sharp corner is constructed directly as the intersection of two stroke axes. No terminal branch towards the outer outline vertex is created. Slightly nonparallel walls use their internal angle bisector.
- If straight wall pairs do not describe the complete outline, use approximate medial reconstruction with straight-run restoration and cubic fitting for curved portions. This is a hybrid geometric method; the original path of an arbitrary font is not always uniquely recoverable.
- Adjacent selected faces are merged across shared edges; separate letters are not connected. Circular text placement in a common plane is retained.
- CAM junction separation follows axis reconstruction. K has a straight stem and a separate sharp checkmark. In 4, preserve the straight triangular sides and longer through-crossbar, ending the crossing stroke short on both sides. A plus has two separate elbows; P loop returns and Phi arcs stop short of the through-stroke. Connections depend on geometry, with no character lookup table.
- Before junction routing, each free physical end is positioned half the local stroke width before the cap, measured along the terminal axis/tangent. A 0.4 mm stroke leaves 0.2 mm. Width comes from the supporting side walls or local cross-section, not cutter diameter. Existing medial setbacks are accounted for. Shared nodes, sharp corners and closed paths are preserved; new gap endpoints are not treated as physical terminals. If insufficient length remains, calculation stops without silently deleting the stroke.
- Target chain clearance is **0.02 mm**, retried at **0.01 / 0.005 / 0.004 mm** if needed. Validate containment, self-intersections and contacts between different chains. Independent rings remain closed. Set CAM tolerance in the operation.
- A filled round dot becomes a concentric **D/2** circle: Ø0.6 mm face → Ø0.3 mm curve. A compact, slightly oval dot uses the area centroid and half the equal-area-circle diameter. Its radial shape check also uses the area radius, rather than the sampling-dependent mean of vertex radii. A compact square or near-square parallelogram without a hole becomes a central circle of diameter half its smaller width: a 0.6 mm square gives Ø0.3 mm. Italic shear and subdivided straight sides are supported. Elongated bars, other polygons and counters are not replaced by this rule. This is a geometric convention, not cutter compensation.
- Curves are named `PM_ENGRAVING_CENTER`. Source bodies and faces are retained. Output is not associative; recreate it after changing the model.

## Boundaries and reruns

Each chain is added as a separate **NXOpen BoundarySetPlanarMill**. After appending curves, **Open**, **Tool Position = On**, **Stock = 0** are explicitly set. New boundaries and members have **Custom Tolerance** disabled and inherit the operation tolerance. The script neither reads nor writes nor compares numeric Intol / Outtol values; the V1.04 tolerance rollback check has been removed. Member settings are read back through a fresh builder after Commit. An open chain must remain Open. For an actually closed ring or D/2 circle, the returned Closed type is accepted only if curve membership and tool position are preserved; an added closing member is rejected. Existing local boundaries are retained and their count is reported. Cutter compensation, depths, feeds, tool and other operation parameters are not reset. Use **Cut Pattern = Profile**. Operation-level path smoothing and compensation remain active and require inspection; their values are not changed by the script. Different curve and cutting-level heights can also look offset in a perspective view.

**When upgrading from V1.01–V1.08, recreate any retained previous axes:** undo that run or remove its curves from the operation boundaries and model. Previous results are not automatically replaced; the journal reports that they use the earlier algorithm.

An unchanged face group processed by the current algorithm is skipped. Changed source geometry, changed generated boundary membership or a partially selected previous group causes an explanatory stop. For a recalculation, remove the previous axes from the operation and model, or undo the previous run. Repeat detection uses attributes on generated curves; no attributes are added to source faces.

Operations inheriting existing boundaries first require local boundary geometry. Errors after the edit starts trigger a single Undo rollback of generated curves and new boundaries.

## Limits

- Supports planar engraving with planar bottom faces. V grooves without a flat bottom, cylindrical surfaces, different-depth inscriptions and side-wall-only selections are unsupported. A single planar side wall cannot be distinguished automatically from a bottom face; selecting the intended faces is the user's responsibility.
- Numerical medial-axis reconstruction does not recover the exact original font path. Variable stroke widths, serifs, acute corners and short strokes can produce extra branches or different endpoints; inspect the actual model result.
- Fitting parameter: 0.002 mm; outline sampling: 0.0002 mm; endpoint joining: 0.000001 mm. Values are converted for inch parts. These are internal geometry calculation parameters, not CAM tolerance overrides. They do not guarantee the overall medial reconstruction error.
- Under-resolved, degenerate, excessively complex or disconnected results are rejected before editing. Maximum 10000 boundary samples per region; accuracy is never silently reduced.
- If even the minimum 0.004 mm gap cannot fit inside a stroke, consumes a genuine short branch or leaves an intersection, reject the calculation without silently skipping that branch. This is geometric clearance: it does not account for cutter diameter or guarantee continuous engraved coverage.
- The D/2 rule is not automatically applied to elongated marks or dots of other cross sections.
- No automatic toolpath generation, PRT save, files, logs, INI files or new operations. Creates the model curves, one shared boundary plane and the operation boundaries.

## Validation

Validation covers 277 new terminal/punctuation scenarios, 586 routing scenarios, 122 centerline scenarios, 118 CAM adapter contract checks and 21 chain/plane checks. New tests compare endpoints with analytical coordinates for flat, oblique and rounded caps, arcs, short strokes, sharp bends and varying widths; rotations, reflections, mm/inch units and subdivided square edges are included.

For a 0.4 mm wide bar, V1.08 leaves 0.02 mm before the cap; V1.09 leaves 0.2 mm. A 0.6×0.6 mm square now yields one Ø0.3 mm circle instead of a branched axis. Closed counters and previously tested round dots are retained.

All 1104 variants of 23 faces from the previously supplied STEP model passed again outside NX. Spline sampling follows the journal: 16 initial intervals and adaptive subdivision. The source model is not included in the repository. This does not validate results on other, unavailable models.

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. **Interactive V1.09 execution and the resulting NX toolpath remain untested.** Geometry and contract tests do not validate native CAM behavior. Inspect the result and the normally generated NX toolpath before machining.
