# Changelog

[Русский](CHANGELOG.md) | **English**

## 2026-11-14 — Engraving centerlines for PLANAR_MILL V1.11

Fix unintended rounding where a straight stroke meets a curved stroke. When the outline confirms a sharp joint, reconstruct it from the supporting walls while retaining the curved portion. Genuine tangential fillets stay smooth; terminal setbacks and circles for dots are retained.

Add 168 checks for circular and noncircular joints, genuine fillet retention and terminal setbacks. Existing geometry checks and all 1104 variants of the previously supplied STEP model pass outside NX. An approximate outline reproduces the new screenshot; its STEP/PRT model was not supplied. Compile against DC 2606 SDK for .NET Framework and .NET 10; native NX execution and toolpaths remain untested. Recreate previous V1.01–V1.10 axes.

## 2026-11-14 — Mirrored CAM project trial V1.03

Replace the Python `Cambnd.AskBoundaryData` call that reported a memory access violation. Use `UF_CAMBND_ask_boundary_data` with a full-width pointer and a C structure; do not dereference application data. Boundary-plane validation and stopping on read errors are retained; the report identifies the boundary number and stage.

Thirty-nine checks passed outside NX, including six with a separate C library for layout, handles, array lifetime and Blank reads. Rollback on an access-violation exception is covered. A complete V1.03 run in NX remains unconfirmed.

## 2026-11-14 — Mirrored CAM project trial V1.02

Fix the missing Python `Modl.AskFaceData` method after picking a face. Face and exact-bound reads, geometry transformation and model update call documented UF functions in the NX library. Entry points are checked before creating a copy; error codes and rollback are retained.

Thirty-two portable checks passed, including ctypes calls without Python modelling methods. A complete V1.02 run in NX remains unverified. The single-part trial restrictions are retained.

## 2026-11-14 — Mirrored CAM project trial V1.01

Pick a planar face or datum plane directly in NX, including oblique planes. Check bounds in the selected-plane frame. On wrong-type error 1345005, query the geometry provider; explicitly record unavailable channels instead of treating them as empty. Other errors stop the trial.

Twenty-six portable checks passed. A complete successful V1.01 run in NX remains unconfirmed; numerical spatial settings are still not transformed.

## 2026-11-14 — Engraving centerlines for PLANAR_MILL V1.10

Rework junction handling from the supporting outline walls. Distinguish an outer corner bevel from a true stroke cap and extend support for tapering straight strokes. Remove short false branches on curved portions only when neighbouring boundary walls support that classification. These rules do not depend on character or font names.

Add 360 checks for bevelled corners, straight-to-arc transitions and retention of genuine short arms. V1.09 fails the new bevelled-corner regression. Existing terminal, punctuation and routing tests remain green; all 1104 variants of the previously supplied STEP model pass outside NX. Synthetic outlines represent the new engraving: its STEP/PRT model was not supplied. Compile against DC 2606 SDK for .NET Framework and .NET 10; native NX execution and toolpaths remain untested. Recreate previous V1.01–V1.09 axes.

## 2026-11-14 — Mirrored CAM project trial V1

First experiment for a saved part without components: a separate PRT copy, geometry reflection with identifier and CAM-reference checks, MCS/RCS transfer, calculated-toolpath removal and attempted rollback on failure. No automatic save or path generation. Numerical spatial parameters are not transformed; supported types and limits are listed in the guide.

Fifteen portable checks passed. NX kernel execution and Windows dialogs remain unverified.


## 2026-11-14 — Engraving centerlines for PLANAR_MILL V1.09

Free centerline ends now stop half the local stroke width before the terminal boundary. Both reconstruction engines measure the setback from the source outline, without shortening already inset ends twice. Square and near-square dots, including sheared italic shapes, become circles with a diameter equal to half their smaller width.

Add 277 geometry regressions. Existing routing checks and all 1104 face variants from the previously supplied STEP model pass outside NX. CAM tolerances and junction separation rules are unchanged. Compile against DC 2606 SDK for .NET Framework and .NET 10; native NX execution and toolpaths remain untested.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.08

Fix punctuation failures reporting insufficient room for a sharp connection. With uneven spline sampling, V1.07 could mistake a slightly oval dot for a branching stroke. Shape validation now uses the equal-area-circle radius, which is independent of the number of samples on individual boundary segments. D/2 circles are retained.

Add 108 sampling-density regressions. Offline STEP tests reproduce 144 V1.07 failures on three dots; V1.08 passes all 1104 variants of 23 faces. K/4 geometry, clearance rules and CAM tolerances are unchanged. Total: 586 routing scenarios. Compile against DC 2606 SDK for .NET Framework and .NET 10. Native NX execution and toolpaths remain untested.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.07

Straight strokes are now reconstructed directly from pairs of opposite outline walls. Validate wall coverage, connectivity, hole count and area consistency. Sharp vertices are intersections of the recovered axes, without a branch towards the outer tip. Curved portions retain medial reconstruction. Slightly oval imported punctuation becomes D/2 circles. CAM tolerance remains inherited from the operation.

The source compiles against Designcenter 2606 libraries for .NET Framework and .NET 10. Validation includes 478 routing scenarios, 122 centerline scenarios, 118 CAM adapter contract checks and 21 chain/plane checks. The 97 new scenarios cover five independent straight-stroke layouts with rotation, slant, reflection and mm/inch units, slightly oval dots, and retention of an elongated mark. The 228 existing acute-triangle and branching-stroke cases additionally require wall-based reconstruction.

Outlines were also read directly from a supplied STEP model outside NX. V1.06 reproduced the unwanted terminal branch on both triangular symbols. All 23 inscription faces passed V1.07 construction and route validation: each 4 has three-line and one-line chains, K has one-line and two-line chains, and each of three dots has a circle. The source model is not included in the repository.

**Interactive V1.07 execution and the resulting NX toolpath remain untested.** The installed NX batch launcher failed before journal execution, and window automation could not attach to the working window. Geometry tests and compilation do not validate native CAM behavior. Inspect the result and the normally generated NX toolpath before machining.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.06

- K: create the through-stem as one line and the diagonals as a separate sharp checkmark clear of the stem. Merge nearby junctions only with four confirmed straight guides; preserve independent adjacent junctions.
- 4: recover straight axes between slightly nonparallel walls and retain short external strokes. Prefer the longer of equally straight through-strokes and separate the other arms on both sides. Keep sharp triangle corners without a false branch towards the outer vertex.
- Protect recovered straight runs from subsequent spline fitting. Remove residual connectors after node contraction and redundant splits of a single line. Retain containment, intersection, genuine-loop and terminal-endpoint checks.
- CAM tolerances remain inherited from the operation; D/2 circles for dots are retained. Recreate previous V1.01–V1.05 axes.
- Add 192 K/4 shape checks with quantized wall coordinates, rotation, slant, reflection, smaller size and mm/inch units. V1.05 fails the new regression. Total: 381 routing scenarios, 122 centerline scenarios, 118 adapter checks and 21 chain checks. Compiles against DC 2606 SDK for .NET Framework and .NET 10. The user's STEP/PRT model and final toolpath remain unverified.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.05

- Remove the forced 0.001 mm tolerance. New boundaries and their members inherit operation tolerance; numeric Intol / Outtol values are neither read nor written nor checked. Remove the resulting tolerance rollback.
- Fix the false branch towards the outer vertex of an acute triangular 4. Removal requires two pairs of constant-width walls; genuine pointed arms remain intact.
- Use the angle between sides to reconstruct acute corners instead of a fixed stroke-width limit. The triangle has three straight sides, retains the stem and crossbar, and keeps junction gaps.
- Add 36 acute-4 variants checking shape, corners and absence of extra branches, plus a retained long pointed-arm case. The new regression reproduces the V1.04 defect. Total: 189 routing scenarios, 122 centerline scenarios, 118 adapter checks and 21 chain checks. Compiles against DC 2606 SDK for .NET Framework and .NET 10; the user model and final toolpath remain unverified.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.04

- Fix the short-stroke gap failure by leaving through-pairs untrimmed, including short connectors between nearby junctions.
- Remove collapsed connectors left by straight-axis snapping. The removal condition excludes genuine loops around holes and independent short strokes; compact duplicate points before fitting.
- Adapt the requested 0.02 mm gap within 0.004–0.02 mm when needed, without changing the 0.001 mm CAM tolerance. Retry from the original curves and report the selected gap range. Never silently omit an unresolvable branch.
- Fix false intersection detection caused by floating-point error on nearly parallel segments.
- Add 93 scenarios, including slanted 4/plus/K/P/Phi outlines reproducing V1.03 failures. Total: 152 routing scenarios, 122 centerline scenarios, 102 adapter checks and 21 chain checks. Compiles against DC 2606 SDK for .NET Framework and .NET 10; the user model and final toolpath remain unverified.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.03

- Separate junctions into nonintersecting chains with at least 0.02 mm clearance: two elbows for plus, a stem and diagonals for K, and gaps at loop returns in P/4/Phi.
- Merge nearby K junctions only when every external straight guide agrees on their intersection. Retain standalone rings and D/2 dot circles.
- Replace UF appends with NXOpen BoundarySetPlanarMill creation and fresh post-Commit readback. Open chains must remain Open; Closed is accepted only for actually closed source chains with unchanged curve membership.
- Set Tool Position On using the NXOpen convention already used in PlanarCHIK; retain zero stock, 0.001 mm tolerance and whole-run Undo. V1.01/V1.02 axes must be recreated.
- 59 new routing scenarios, 102 adapter checks, 122 centerline scenarios and 21 chain checks. Compiles against DC 2606 SDK for .NET Framework and .NET 10. The user model and generated NX toolpath remain unverified.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.02

- Separate geometric curve closure from CAM boundary type: all new engraving profiles use Open / On. Remove the previous comparison against letter closure that caused rollback.
- Preserve exact midlines between parallel sides and their sharp intersections while retaining real outline fillets.
- Explicit local 0.001 mm Intol / Outtol and zero stock on new boundaries and members; verify saved settings again after Commit.
- Match retained boundaries by member identity rather than temporary NX pointers. Previous V1.01 axes require recreation instead of being silently skipped.
- 122 geometry scenarios, 70 UF adapter checks and 21 chain checks; compilation against DC 2606 SDK for .NET Framework and .NET 10. The user model and generated toolpath still need native NX validation.

## 2026-11-13 — Engraving centerlines for PLANAR_MILL V1.01

- Select engraving bottom faces instead of existing curves; reconstruct medial curves while retaining holes and letter placement.
- Filled round dots use concentric circles of diameter D/2.
- Adjacent faces, same-unit assembly components, repeat-selection checks and a single undo transaction for curves and boundaries.
- Existing working filename retained; new boundaries use Tool Position On and Stock 0.
- .NET Framework/.NET 10 compilation and geometry scenarios checked; the user model and resulting toolpath are not yet validated.

## 2026-11-12 — Smooth GOST A text V1.01

- Fixed .NET 10 journal compilation errors mentioning `IPersist.Interface`, `IHandle<>`, `System.Private.Windows.Core` and `System.Drawing.Common`.
- Creates the dialog through the Windows Forms assembly available in NX without compile-time references to its types. No additional DLL downloads or manual library installation are required.
- Reads Windows-1251 Cyrillic when the host has not registered legacy encodings, using the runtime provider without changing NX process-wide encoding settings.
- Compiled the complete journal against DC 2606 SDKs for .NET Framework and .NET 10. Exercised real dialog inputs, accept/cancel and repeated opening in both runtimes without visible windows.

## 2026-11-12 — Smooth GOST A text V1

- Added `NX_GOST_Smooth_Text.cs` and the updater entry «Гладкий текст ГОСТ А».
- Reads an external vector GOST_A.FON locally. Does not change or redistribute the source font.
- Reconstructs rounded sections as cubic splines with matching tangent directions. Preserves straight strokes and intentional corners; removes retraced source segments within glyphs.
- Accepts one line, height in mm and rotation. Pick the origin on WCS XY; letter slant is 15 degrees from vertical. Creates ordinary curves with one undo step.
- Passed DC 2606 SDK compilation, 10 portable geometry scenarios and a local 180-glyph source-font check. Interactive NX execution remains unconfirmed.

## 2026-11-12 — Curves for PLANAR_MILL V1

- Added `NX_PlanarMill_Select_Curves.cs` and the updater entry «Кривые для PLANAR_MILL».
- Adds Text, sketch or directly selected curves to an existing operation. Skips reference geometry and repeated selections of the same object.
- Builds open and closed chains without connecting disconnected strokes. Does not add curves already assigned to the operation again.
- New boundary members use Tool Position On and stock 0. Preserves existing boundaries and machining parameters; handled errors roll back the current run.
- Checks coplanarity, work-part ownership and the new boundary contents. Nonempty inherited boundaries require switching to local boundaries first.
- Passed compilation against the installed DC 2606 SDK and 21 chain/plane scenarios. Native NX execution is not confirmed: the background process failed before running the journal.

## 2026-11-11 - Save project and archive V1

- Added `NX_Save_Project_And_Archive.py` and its updater entry. Native Save saves the work part and modified components; only the main work-part `.prt` is archived.
- One `<project name>.zip` next to the `.prt` is replaced on subsequent runs. The original path is preserved; no temporary files, INI, logs or additional copies are created.
- Checks NX save status, remaining modifications, CRC, size and SHA-256 of the archived copy. Releases handles and protects unrelated existing ZIP files.
- Keeps the old ZIP in memory for recovery from handled write failures. Replacement without a staging file is not atomic on power loss or forced termination. Archiving never writes to the original `.prt`.
- Added creation, repeated replacement, normal-write alternation, NX failure, locking, memory-exhaustion and ZIP restoration checks.
- Passed 22 dedicated tests and native Designcenter 2606 execution: journal → native Save → journal. The path and Work/Display Part were preserved, ZIP matches the latest `.prt`, and only two files remain in the project folder.

## 2026-11-09 — Setup sheets V2.68

- Operation headings wrap at word boundaries; sizing uses the longest heading word instead of the complete phrase.
- Automatic widths for all columns except Operations follow the longest value across all setup operations. Fixed minima for empty and short columns are removed.
- Operations receives remaining space; when the minimum total exceeds the sheet, the whole table scales to fit. Manual widths, their persistence and Ctrl + wheel scaling are preserved.
- Checked width calculation, hidden columns, long values on later sheets, HTML generation and saving. Browser visual verification was not performed because the environment blocks Chrome launch.

## 2026-11-08 — Clean operation names V1

- Added `NX_Clean_Operation_Names.cs` and the «Очистка названий операций» entry to the updater.
- Removes trailing COPY, INSTANCE, INSTANT, DUPLICATE and Russian equivalents; replaces old numeric suffixes with sequential duplicate counters in Program Order.
- Numeric Z suffixes retain their exact text without calculation or rounding. Duplicate counters precede Z; different Z suffixes form different names.
- Covers all work-part operations, including nested groups and instances. Includes CAM group name collision checks, stable repeat runs, one undo step and verified rollback on errors.
- Checked executable naming, traversal, collision and failure scenarios, 5000 mixed names, compilation against Designcenter 2606 libraries and updater installation. Native execution on a production part has not yet been performed.

## 2026-11-07 — Zmin V1.09

- Z in operation names is rounded to three decimal places: `CHIST_1_Z-2.5375` with a calculated Z of −2.5375 becomes `CHIST_1_Z-2.538`.
- Trailing zeros are omitted and negative zero is displayed as `Z0`. Names that collide after rounding receive a counter before Z under the existing rules.
- Only the standalone journal's Z formatting is changed. Depth calculation, Setup sheets and Postprocessing are unchanged.

## 2026-11-07 — Zmin V1.08

- Fixed a false skip of a constant-Z circular toolpath caused by a small difference between the stored endpoint radii. When the arc plane is perpendicular to the current tool axis, that difference does not affect depth.
- Constant Z is verified from the arc-axis direction, radii and endpoint levels. Tilted arcs retain analytic interior minima and radius validation; helix checks are unchanged.
- This release updates only the standalone Zmin journal. Setup sheets V2.67 and Postprocessing V1.50 retain the V1.07 calculation for now; integration of further fixes is deferred.
- Checked outside NX: reproduced the failure from the complete diagnostic export; the corrected minimum matches reference NC output. Added checks for rotated axes, tilted arcs with equal endpoint Z, tiny tilts at large radii, noncoplanar endpoints and helices. This revision has not been executed inside NX.

## 2026-11-06 — Zmin V1.07, Setup sheets V2.67 and Postprocessing V1.50

- Added `Drill,Deep` support for `SPOT_DRILLING` / `NXHOLECENTER` spot drilling, using local `DEPTH` to the tip.
- Fixed skipped through-hole drilling when local FBM and model entrances lie at different levels along the same axis. Type confirmation requires one uniquely linked model feature on the same axis with the same direction.
- The fix is shared by the standalone journal, the setup-sheet Zmin column and renaming, and postprocessing. Each `Cycle` point must still match the local entrance strictly; model depth is never substituted.
- Checked outside NX: the reported skips were reproduced from the diagnostic export; the corrected Python and compiled production C# calculations agree with reference NC depths for all three operations, to NC output precision. Synthetic tests cover different model levels, rotation, both axis-storage formats, transverse offsets, opposite axes and coaxial ambiguity. Native NX execution has not been performed. COPY cleanup and collision numbering are retained.

## 2026-11-06 — Zmin V1.06, Setup sheets V2.66 and Postprocessing V1.49

- Added cleanup of leading `COPY_` prefixes and trailing COPY / old-Z chains, including repeated copies. Names retain one current Z suffix.
- Colliding final names receive `_1`, `_2`, and so on before Z. Allocation considers occupied names across the project; reruns keep the result.
- The same rule applies to the standalone journal, setup-sheet renaming and postprocessing. Naming, selection scope, reruns and rollback were checked in Python/C# outside NX.
- Depth calculation, rotary handling and drilling support are unchanged. Operation-order numbering retains its existing conflict checks.

## 2026-11-06 — Zmin V1.05, Setup sheets V2.65 and Postprocessing V1.48

- Fixed skipped through-hole drilling when the local entrance differs from its linked model feature by a positive `TopOffset.Distance` along the axis. If no direct match exists, only that exact offset with type `Distance` is accepted; ambiguous links are rejected.
- Each `Cycle` point still matches the local entrance. Calculation uses local `DEPTH`, bottom offset and point length, without adding top offset again or substituting model depth.
- Updated the shared algorithm in the standalone journal, setup-sheet table and renaming, and postprocessing. Working filenames and selection rules are retained.
- Checked outside NX: reproduced the original skip from diagnostic data; corrected Python and C# calculations match reference NC rounding. Synthetic checks cover rotations, both axis storage modes, prior profiles, incorrect offsets and ambiguous links. Native execution inside NX was not performed.

## 2026-11-06 — Delete empty tools V1

- Added `NX_Delete_Empty_Tools.cs` and «Удаление пустых инструментов» (Delete empty tools) to the updater catalog.
- Deletes only empty tools in the current work part: no child nodes and no association with CAM operations. Tools with ungenerated operations and folders are preserved; tree selection does not limit the scope.
- Preview, explicit confirmation, one Ctrl+Z undo step, a second check before deletion, result verification and an attempt to roll back the entire run on failure. Creates no files and does not save the PRT automatically.
- Compiled against the Designcenter 2606 libraries and checked 31 scenarios using an NX API test model: deletion, cancellation, nested associations, errors and rollback. Execution on a real CAM project in NX has not yet been checked.
- Added Russian and English guides and an automated journal check.

## 2026-11-05 — PlanarCHIK V1.01

- First publication of PlanarCHIK in the updater catalog, using the stable filename `PlanarCHIK.cs` and SHA-256 verification.
- The script sets Tool Position On for every boundary member of selected PLANAR_MILL operations. Answering Yes to «Добавить припуск?» (Add stock?) enables Stock for each member and applies one entered value in work-part units; No preserves the Stock settings.
- V1.01 appears in the opening comment and a single version constant used by message titles. The native NX numeric input includes the version in its prompt. Added Russian and English guides; publication does not change script behavior.
- Checked catalog validity, matching versions and SHA-256, and installation and updating through the updater code. Execution inside NX / Designcenter was not performed here.

## 2026-10-23 — ESKD V1.41

- Fixed the NX journal `SHA256` compilation error: the system algorithm is resolved at runtime without an additional assembly reference.
- Retained SHA-256 and CRC32 checks for original and corrected external-font copies, `v110` → `v111` metadata repair and rejection of invalid data. Formatting, chamfer spacing, tolerance precision, paths and INI behavior are unchanged.
- Extended font checks with known SHA-256 values and compilation without cryptographic assembly references. Updated the updater catalog and both documentation languages. This revision was not executed in NX / Designcenter here.

## 2026-10-22 — Setup sheets V2.64

- On additional setup sheets, the Setup row is replaced by MCS and shows the full name of the coordinate system used for that sheet's views.
- This row has no setup counter. Long MCS names wrap, and the name is retained when editing the setup number, saving HTML and printing.

## 2026-10-22 — Setup sheets V2.63

- Additional MCS sheets now include X, Y and Z datum fields with the same suggestions and free-text input as the setup cover.
- XYZ values belong to each setup/MCS pair and are restored from saved HTML when the script runs again, including reordered sheets or a different MCS on the cover.
- Values for a temporarily unchecked MCS remain inside the HTML and return when it is selected again. Deliberately cleared fields stay empty.
- When updating a V2.62 card, existing XYZ values follow the MCS shown on the cover, even when the first operation's MCS was unchecked.

## 2026-10-22 — Setup sheets V2.62

- The multiple-MCS warning now includes «Выводить несколько MCS» (Output multiple MCS).
- A tree lists every selected setup with independent MCS checkboxes. Keep at least one MCS per setup; Select all and Clear all are available.
- Each checked MCS gets top, side and isometric views with paired IPW / NO IPW variants, its own axes and its full name in the image's upper-left corner.
- The first selected view set appears on the setup cover; further sets use additional sheets before operation continuations. Operations and the setup tool list remain shared.
- Additional views support IPW switching, zoom, pan, Original view, HTML saving and printing. Distinct MCS objects are separate choices, including `1` and `1S`.

## 2026-10-21 — Setup sheets V2.61

- Added Ctrl + wheel scaling for the operation table or tool list under the pointer in 5-percentage-point steps, without browser zoom. Table sliders are removed; their 60–300% range is preserved.
- Separate lists and sheets retain independent scales; both columns of the shared tool catalog keep one common scale. HTML saving, undo and Original view preserve these settings.
- The faceted-model wheel and +/− keys change scale by 5 percentage points instead of jumps dependent on wheel-delta magnitude and current scale.
- MCS-arrow, outline and model controls are retained. Regenerate the HTML to apply the new behavior.

## 2026-10-21 — ESKD V1.40 and PDF export V1.13

- ESKD repairs glyph bounds and associated metrics in the exact compatible external `v110` font, installing `v111` with a distinct family name. Outlines, advance widths, hinting and the character map are retained; the original copy is not overwritten.
- Added SHA-256 checks before and after repair, reuse of an already corrected `v111`, and lookup of both names in existing font directories. The font itself is not included in the public package.
- PDF uses `Output Text = Text` with search and copy support; the temporary V1.12 Polylines workaround is removed. Apply ESKD first, then export the PDF again to use the correction.
- Retained ESKD V1.38 formatting, INI behavior, line-weight settings and drawing restoration. Updated the catalog and both languages of the guides, configuration and home pages.
- Setup-sheet Zmin checks locate the column by its header after the column reorder; setup-sheet application code is unchanged.
- Checked syntax, the repair recipe, outline/advance preservation, the font with OpenType Sanitizer and repository checks. A corrected sample PDF opened in Acrobat without the previous error. The new journals were not executed in NX / Designcenter here.

## 2026-10-21 — Setup sheets V2.60

- Fixed saved setup checkmarks appearing unchecked on first opening: checkbox support is enabled before populating the tree and restoring selections.
- The first click clears only the clicked checkmark; other saved setups remain checked.
- Added first-display and first-click regression coverage. Preference storage, component selection and HTML sheets are unchanged.

## 2026-10-21 — Setup sheets V2.59

- Swapped the «СКС» (MCS) and Zmin columns in the operation table: Zmin follows the operation name, and MCS is the last column.
- Headers, values and widths move together. Saved visibility and width settings follow the new order when updating a sheet.

## 2026-10-21 — ESKD V1.38

- Chamfer Dimension now sets separator spacing independently: 1/7 of text height, nominally 0.5 mm at 3.5 mm. Chamfer form, angle and values are retained.
- Added 3-place tolerance precision for primary, dual and angular dimensions. Settings cover existing dimensions on drawing sheets and future dimensions in the current part; stored deviations and the trailing-zero setting are retained.
- Extended read-back verification after application. Updated the updater catalog and both documentation languages. INI handling, the font and other formatting are retained.
- Checked syntax, API properties, catalog consistency and the existing repository test suite. Execution and visual verification in NX / Designcenter remain necessary.

## 2026-10-21 — Numbering V1.05 and updater V1.19

- Numbering now includes deleted folders when restoring tree order. Confirmed unused allocations are reused only in their original project; the unused tail stays reserved with an XLSX note.
- Retained protection of foreign, cancelled and unconfirmed allocations, CAM name checks, manual renames, repeat-run stability and failure rollback.
- Missing/invalid INI and missing Excel no longer block code updates. Data migration follows the code transaction; migration errors cannot roll back the script.
- Added separate setup: connect an existing register, start new numbering with an explicit range, or defer. Empty templates require an explicit choice; existing workbooks are never overwritten.
- Added recovery instructions and the separate V1 journal for available saved PRT attributes. The updater opens the instructions without executing that journal.
- Updated the catalog and Russian/English guides; added real-XLSX, folder-deletion and native Windows setup-dialog checks.

## 2026-10-16 — Setup sheets V2.58

- Removed automatic font fitting to operation and tool table contents, including the two-space reserve. Existing sliders set the scale manually.
- Only the operation name uses a single-line ellipsis when name wrapping is off. Enabling the checkbox wraps only that name; other operation and tool columns stay on one line without wrapping or ellipses.
- The table beside the faceted model continues moving in real time, without automatic font fitting after mouse release.
- Retained Carrier and Tools groups, collapsing and printing, menu memory, D columns and Zmin calculation. Regenerate HTML to apply the new text rules.
- Updated source, updater catalog and documentation in Russian and English.

## 2026-10-16 — Setup sheets V2.57

- First-menu settings are saved separately for each Windows user in `%APPDATA%\NX_Scripts\NX_Setup_Prototype.ini`: preparation checkboxes, Description format, faceted model, coefficient and triangle limit.
- Setup-folder and per-setup component selections are stored in one `NX_Setup_Prototype.project.json` beside the PRT, separately for each user and part.
- Next saves settings and folders; Create sheet saves components. Cancel does not write unconfirmed changes in the current dialog. Menus still appear for review; As in NX uses current NX visibility.
- Retained the adjacent programmer-name INI, V2.56 features, D columns and Zmin calculation. Menu memory needs only the updated script to run; no NX restart is required.
- Updated source, updater catalog and documentation in Russian and English.

## 2026-10-16 — Setup sheets V2.56

- Removed unnecessary operation and image layout work from Carrier toggles; only the relevant tool list is updated.
- Enlarged arrows and click areas while retaining smooth 160 ms rotation, down when expanded and right when collapsed.
- Added an independent «Инструмент» (Tools) block for tools outside Carriers only. Collapsing it neither hides Carriers nor changes their states.
- Retained independent setup-list and project-catalog states, restoration after save/reopen, retention on re-export and expansion through Original view. Collapsed blocks keep their headings on screen and are omitted entirely from print.
- Regenerate HTML to get the new features. Updated source, updater catalog and documentation in Russian and English.

## 2026-10-16 — Setup sheets V2.55

- Added Carrier group collapsing through the arrow in tool lists. A collapsed group's heading stays on screen while its tools and nested groups are hidden.
- Printing omits the entire collapsed group, including its heading. HTML saves and restores the state on reopening, separately for each setup's tool list and the shared project catalog.
- The arrow rotates smoothly down when expanded and right when collapsed.
- Retained D columns, Zmin calculation, manual editing and smooth model/table movement. Updated source, updater catalog and documentation in Russian and English.

## 2026-10-15 — Setup sheets V2.54

- Added a D column for Cutcom registers to every tool list. Each tool occupies one row; used registers are deduplicated and sorted in ascending order, for example `D5 / D12`.
- The operation D column is immediately after T. It reads each operation's own parameter and is populated in every row of that tool number when two or more distinct D values are used across the CAM project. Tool changes and D = T do not affect visibility. With only one D, operation cells remain blank even if D ≠ T.
- Partial re-exports also refresh D in retained setups and the project catalog while preserving the rest of those sections' formatting. Operations require an unambiguous match; a required D cell shows `—` if its value is unavailable or matching is ambiguous.
- Includes changes from the previously delivered V2.53: while the 3D model rotates or pans, the table keeps moving and fitting the available space. Precise shared-font fitting and final layout run after mouse release; both catalog columns scale equally.
- Retained V2.52 Zmin fixes, faceted model quality, manual editing and saving. Regenerate the HTML to get the new columns and improved viewer.
- Updated source, updater catalog, README and guide in Russian and English.

## 2026-10-15 — Zmin V1.04, Setup sheets V2.52 and Postprocessing V1.47

- Extended machine-cycle support in all three scripts. `SPOT_DRILLING` / `NXHOLECENTER` spot drilling accepts `Drill` and `Drill,Csink`; depth to the tip is operation-local FBM `DEPTH`.
- Through-hole drilling now supports `SYS_CL_SHOULDER` with `IgnoreToolTip` enabled and retains `SYS_CL_TIP` with it disabled. Both use full local `DEPTH` + bottom offset + verified standard conical point length. Enabling `IgnoreToolTip` does not exclude point length from Zmin.
- A linked `STEP1HOLE` or `STEP2HOLE` feature confirms the through-hole type. The master feature's first-step `DEPTH_1` does not replace the full operation-local depth.
- Increased the base FBM coordinate matching tolerance to `0.00001` model units to allow small roundoff. Entrance-point and axis matching must still be unique; ambiguous holes are not selected by order.
- The sheet applies the correction to its Zmin column and optional renaming; postprocessing applies it to optional renaming before NC output. Rotary CL projection is retained. HTML, table formatting, layout, preparation, rollback and NC/BIN output are unchanged.
- Blind holes, unsupported depth policies, custom tracking points and pilot/compound tips remain unsupported. Incomplete data does not produce partial minima or substituted Z0. [Full limitations](docs/en/zmin.md#spot-drilling-and-drilling).
- Outside NX, new and previous diagnostic data agreed with all five reference NC Z values at NC output precision. Target NX / Designcenter checks remain necessary. Updated sources, updater catalog, README and guides in both languages.

## 2026-10-15 — Setup sheets V2.51

- Ported the standalone Zmin V1.03 machine-cycle correction to the Zmin column and optional operation renaming. A Cycle entrance point is no longer treated as the hole bottom.
- Supported spot drilling uses operation-local FBM `DEPTH` to the tip; through-hole drilling adds bottom offset and the verified length of a standard conical point. Holes are matched to path coordinates and axes independently of order.
- Unsupported cycles and incomplete data leave Zmin blank and its suffix unchanged. With renaming enabled, the skip reason is shown in the NX result. Partial minima and substituted Z0 are excluded; [supported scope](docs/en/zmin.md#spot-drilling-and-drilling) matches the standalone journal.
- Retained rotary calculation along actual Three/Five ToolAxis vectors relative to the MCS origin, including constant-axis arcs and helices. Selection scope, preparation rollback and reuse of calculated Zmin in the sheet are retained.
- Based on V2.50: a shared maximum fitting font for each table, equal font sizes in both project tool-catalog columns, complete names without ellipses and room for two spaces after the longest operation name in Auto width. Existing layouts and manual widths are retained.
- Updated source, updater catalog, README and guides in both languages. A control run of this revision inside the target NX / Designcenter environment remains necessary.

## 2026-10-15 — Postprocessing V1.46

- Ported the standalone Zmin V1.03 cycle correction to optional Zmin naming before NC output: local spot-drilling depth and through-hole drilling depth with bottom offset and standard conical point length.
- Holes are matched by position and axis. Unsupported cycles or incomplete data skip Zmin for the whole operation with a reason in the result; neither false Z0 nor a partial minimum is written to the name.
- Retained rotary calculation along actual ToolAxis vectors, selection scope, numbering, Description, preparation rollback, paths, INI and NC/BIN output. Zmin defaults are unchanged; its calculation adds no files.
- Updated source, updater catalog, README and guides in both languages. A control run of this revision inside the target NX / Designcenter environment remains necessary.

## 2026-10-14 — Zmin V1.03

- Fixed false Z0 results for supported `HoleDrilling` machine cycles: a saved Cycle entrance point is no longer treated as the hole bottom.
- Spot drilling uses operation-local FBM `DEPTH` to the tip. Through-hole drilling includes local depth, bottom offset and drill point length. Master-model depth and the old name suffix are not used as operation depth.
- Holes are matched to path coordinates and axes regardless of order. Projection uses the actual ToolAxis, including rotary orientations.
- Blind/stepped holes and unsupported cycle settings are skipped with reasons. Incomplete data preserves the operation name instead of writing a partial minimum or substituted Z0. See the [guide](docs/en/zmin.md#spot-drilling-and-drilling) for the exact supported scope.
- Checked outside NX: diagnostic-data replay agrees with final reference NC Z values at NC output precision; regressions cover different depths, rotations, invalid data and unchanged ordinary CL results. Native V1.03 execution in NX / Designcenter is not yet confirmed.
- Source, updater manifest, README and guides are updated in both languages. The cycle-depth correction is published in the standalone journal; setup-sheet V2.49 retains its earlier CL-point calculation.

## 2026-10-09 — ESKD V1.37

- Previous current-sheet borders, title/auxiliary tables and their labels are deleted before the new format is built, including hidden Siemens elements and objects from earlier runs.
- Cleanup respects sheet membership, cascading table/child deletion and the prohibition on cleanup after new formatting starts. A deletion failure rolls back this run.
- Material text now has an explicit break before GOST/OST/TU, followed by wrapping of long entries and font reduction if the block is too tall. Final checks verify that line breaks survived the sheet refresh.
- Retained table sizes, INI handling, the external font and section geometry. Updated the catalog and Russian/English documentation.
- Added executable C# algorithm checks for text placement, border areas, deletion order and failure handling. Checks without NX do not replace object/rendering verification in the target application.

## 2026-10-09 — Setup sheets V2.49

- Added an optional first-sheet faceted 3D model. With it off, NX fits the current orientation and captures a screenshot; camera and display settings are restored afterwards.
- Enabling it reveals an accuracy coefficient (0 < k ≤ 1) and combined triangle limit (default 500,000). Lower coefficients reduce detail; higher triangle limits are supported by both export and the browser.
- Exceeding the limit shows a concise instruction to reduce accuracy or increase the limit. The previous document is retained.
- Re-export switches between screenshot and mesh, including partial setup updates. Updated the updater catalog and Russian/English documentation.
- 30 portable checks and the browser mesh → screenshot → mesh scenario passed. Four native Windows checks were skipped on Linux; target NX / Designcenter execution still requires separate validation.

## 2026-10-09 — Updater V1.18 and numbering V1.04

- Numbering INI/XLSX files are installed beside the script in the selected working folder. Numbering locates its INI using the running NX journal path instead of the former fixed data directory.
- The updater migrates the old settings/history pair, preserving ranges, comments and records. Existing destination files are retained; lost history is never replaced with an empty template.
- Migration sources are checked again and guarded against writes while copying. Ordinary write failures roll back changes; old source files remain untouched.
- The catalog requires updater V1.18: update and restart it before installing the new numbering script. Missing companion files can be restored manually even when the script version is current.
- Added scenarios for installation into a folder with spaces, history migration, custom paths, repeat runs, missing/conflicting history, source changes and write rollback. Execution inside NX / Designcenter remains to be checked in the target environment.

## 2026-10-09 — ESKD V1.36

- Restored the horizontal 70 × 14 mm upper box for all supported formats and both sheet orientations; duplicate-designation text remains rotated by 180°.
- Long title-block text wraps before font reduction. Added wrapping for codes without spaces and full line-height checks while retaining the 185 × 55 mm title block.
- Cells use native Wrap followed by AutoSizeText. After the sheet refresh, the script checks fitting settings and evaluated overflow hashes. It warns if measured text cannot fit even at 2.5 mm and needs further fitting.
- Retained the public external-font workflow, INI handling, menus and section geometry. Updated the catalog and Russian/English documentation.
- Checked C# syntax, box dimensions and catalog consistency. Local full-suite run: 111 tests passed and 20 were skipped; one postprocessor test stopped because the validation environment lacks the .NET SDK. Repository checks do not replace NX execution; rendering in the target NX / Designcenter builds remains unverified.

## 2026-10-07 — English documentation

- Added an English repository overview, installation/configuration instructions and guides for all 13 scripts.
- Added English publishing instructions, release history and third-party notices, with language links to the Russian originals.
- Script source, versions, settings and updater catalog are unchanged.

## 2026-10-07 — Postprocessing V1.45

- Multiple-offset parsing inherits modal state from headers and previous blocks. Known G modes are restored only when needed; coordinates and tool/spindle/coolant commands are not invented. Moves and final modes are checked with optional-block skipping on and off.
- Added separate X and Y blocks before the first separate Z approach with G43/H, numbered O headers, and G80 with G00/G01/G02/G03 in either order. A header-only offset is inserted before every repeated section.
- Explicit post entries support `Auto`, `ISO_G28` and `ISO_G53` in the existing INI. G53 requires `WorkOffsetG53Z` matching the original retract, G49, G90/G00/G40 and one explicit G20/G21 unit mode. The script neither chooses a machine-safe height nor adds retract moves.
- Errors show post, profile, offsets, the failed condition and neighboring original NC lines. Copy error details includes the version; Close/Escape dismisses the window. A native error fallback remains if Windows Forms fails. No report/backup files were added.
- Existing paths, INI editors, selection UI, Description, Zmin, numbering, subprogram calls and BIN behavior were retained. Added numeric ISO and Windows error-window checks. Original O1261/O5555 cases were checked privately. Target NX/controller validation remains necessary.

## 2026-10-07 — Setup sheets V2.48

- Added startup checkboxes matching postprocessor V1.44: operation numbering, tool Description and Zmin naming. All reset to off. Inline Description formats are mutually exclusive and retain the choice when hidden/shown during a run.
- Numbering restarts in each selected Program Order folder, replaces old leading numbers and uses two/three digits at the 100-operation threshold. Description updates both fields for supported tools throughout the part without changing diameter or T/H/D; missing D is not invented.
- Zmin uses selection captured before the menu. Empty selection covers the project; unreliable selection never expands scope. It uses the standalone algorithm, replaces numeric suffixes and reports unsupported paths/conflicts in NX. Calculated values are reused in the sheet.
- Preparation begins after setup/component confirmation. Cancellation or failure before HTML publication restores names/descriptions. Successful export leaves one NX Undo mark; no automatic PRT save.
- Added option-combination, repeat-run, 99/100 boundary, conflict, Description, missing/invalid D, partial-write restoration and HTML-write failure checks. Real Windows dialogs test mouse/keyboard input, double-click, inline formats, repeated show/hide alignment and cancellation.
- Existing selection drawing, HTML editor, paths and INI were retained without extra service files. Target NX validation is still required.

## 2026-10-07 — Setup sheets V2.47

- Checkboxes and text in setup/component dialogs share the actual Windows row center. Tree connectors/expand icons are aligned; state sizes come from the native image list.
- Preserved hit areas, selection, scrolling, double-click, keyboard behavior, parent/descendant exclusion, initially collapsed NONE, disabled suppressed components and independent per-setup component choices.
- Added real Windows-dialog pixel/input checks with normal/enlarged fonts, themes, scrolling, Space and component previews when switching setups.
- A green Sheet saved message appears for three seconds after a confirmed manual save, overlaying the UI without shifting it. It is neither embedded in saved HTML nor printed. Autosave, cancellation, errors and unconfirmed downloads do not show it.
- Calculations, view generation, paths and INI were unchanged; no extra runtime files. Target NX validation remains necessary.

## 2026-10-07 — Postprocessing V1.44

- Replaced the separate Description-format dialog with inline radio buttons below its checkbox: diameter only by default, or diameter plus T/H/D.
- Disabling the option hides its formats and restores the Zmin/tree positions without accumulating offsets. The current format survives show/hide; a new run defaults to diameter. INI is unchanged.
- Retained V1.43 tree drawing and preparation/postprocessing algorithms. Model changes still occur only before output; no extra runtime files.
- Added real Windows Forms checks for selection, absence of a second window, repeated show/hide and scaling. The full journal was compiled as C# 7.3 against NX API models; target NX execution requires separate checking.

## 2026-10-07 — Postprocessing V1.43

- Full tree rows now draw checkboxes, labels, connectors and expansion icons around one vertical center, fixing the native-checkbox offset left by V1.42's text-only adjustment.
- Native TreeView nodes, selection handlers, hit areas, mouse/keyboard behavior, scrolling, collapsed NONE and the operations tree without checkboxes were retained. State sizes use the actual Windows image list; row bounds come from Win32.
- Added a real Windows TreeView regression test covering pixels, normal/enlarged fonts, two themes, scrolling, mouse/Space selection, BeforeCheck cancellation and expansion. It does not launch NX or change desktop DPI.
- Full C# 7.3 compilation against NX API models, Windows/Linux repository checks and 815 real-tree checks per theme passed. Postprocessing, Description, Zmin, numbering, paths and INI were unchanged; no additional runtime files.

## 2026-10-07 — Setup sheets V2.46

- Added direct percentage input for every slider. Click the number; Enter/blur applies it and Esc cancels. Integer steps and existing ranges are retained; the logarithmic model slider accepts an exact percentage without rounding it to a slider position.
- Incomplete input stays in the UI rather than autosaved HTML. Label double-click, independent controls, exact saved values and Undo are retained.
- Original view restores the current sheet's initial layout, deleted original images, sizes/scales/settings and removes added cells. The first setup sheet also restores operation placement/limit; the project sheet restores the initial model view.
- Initial state is embedded in HTML, retaining deleted original images without duplicating remaining ones. Restoration survives save/reopen. Old HTML without the lost original data needs regeneration to restore those elements.
- The reset is one Undo step. Notes, locating information, global column settings and other sheets remain. No extra files, notifications or INI changes.
- Chromium checks covered sheet/setup variations, all percentage inputs and limits, exact model scale, Undo, repeated save, deleted-cell restoration, real autosave and printing. Windows/Linux repository checks passed; target NX execution remains outstanding.

## 2026-10-07 — Postprocessing V1.42

- Tree label alignment now uses the complete row returned by Win32 TVM_GETITEMRECT instead of text-top and configured ItemHeight. Actual scaled/scrolled coordinates are respected; native checkboxes and selection are retained.
- Renamed the Description option to “Add tool parameters to description”. Enabling it opened a choice of diameter only or diameter with T/H/D. Cancel/close/dialog failure unchecked the option while keeping the first window open.
- Passed the chosen format directly into preparation, removing the repeated prompt immediately before output. Re-enabling allowed another choice. Model changes remained deferred until output; algorithms, INI, paths and shared rollback were retained.
- Checked full C# 7.3 compilation against NX API models, format-selection flows, output without another prompt, rollback and alignment at 100/150/200%. Actual NX-window visual validation remained necessary.

## 2026-10-07 — Postprocessing V1.41

- Added initially disabled Description and Zmin-name options. Description format was selected each time the option was enabled: `⌀*_T*_H*_D*` or `⌀*`, filling both description fields from actual tool parameters.
- Retained standalone-script scopes: Description for tools in the work part; Zmin for preselected operations or, only with empty selection, all project operations. Unreliable selection blocks Zmin rather than widening scope.
- Zmin uses actual Three/Five ToolAxis and analytical arc/helix minima. Only terminal numeric Z suffixes are replaced; repeated runs do not duplicate them. Conflicts/unsupported paths are included in the result.
- Preparation runs alongside numbering before output. Cancellation/failure before the first saved file restores descriptions/names; partial publication retains changes.
- Adjusted vertical tree-label alignment while retaining native interaction. Paths, INI and postprocessing code were unchanged; no extra files.
- Checked full C# 7.3 compilation against NX API models, Description, combined numbering, cancellation/errors, 160 arc/helix comparisons and 72 suffix/rounding cases against standalone Zmin. Drawing geometry was checked at 100/150/200%; live NX/window checks remained necessary.

## 2026-10-07 — Updater V1.17

- Available-version cells for newer versions of installed scripts now have green RGB 0, 205, 134 (`#00CD86`) backgrounds and black text, independent of checkbox/row selection.
- Numeric internal versions determine highlighting. Equal/older/unknown versions and new installations are not highlighted. Successful update followed by Check removes the highlight.
- Correct cells remain highlighted through sorting, scrolling and resizing. Native checkboxes, grid, paths, INI, validation and installation order were retained without new service files.
- Added version-comparison/drawing-handler checks and pixel checks on a real Windows list.

## 2026-10-07 — Setup sheets V2.45

- Original view was added to every physical sheet's toolbar: project tool catalog, setup start and operation continuation. It replaces the old model-only button without duplication.
- Reset restores that sheet's table scale to 100%, operation wrapping and manual note height. The first setup sheet also restores tool scale, MCS arrows/outline and image scale/center; the project sheet restores the model/static image view and scale.
- Text, locating information, IPW choices, cell contents/layout, global column settings and other sheets are retained. Operation pagination is recalculated.
- Reset is one Undo step. No additional files/notifications; paths and INI retained.
- Chromium checks covered multiple sheets/setups, input, reset/Undo, data preservation, save/reopen, migration from the old button, static images and printing. Windows/Linux checks passed; target NX execution remained outstanding.

## 2026-10-07 — Postprocessing V1.40

- Added leading zeros: `01_`–`99_` for output programs with at most 99 numbered operations; `001_`, `002_`, … from the first operation when there are 100 or more.
- Width is based on the full numbered set for each output program independently. Selected-operation mode counts only that set. Renumbering replaces old prefixes and recalculates width across 99/100.
- Updated the checkbox tooltip. With numbering disabled, current names remain unchanged. Order, cancellation, restoration, paths, INI and other postprocessing functions were retained.
- Checked C# 7.3 compilation against NX API models; counts 1/9/10/99/100/101/999; mixed-size programs; selected sets; repeated runs; cancelled transition to three digits; earlier error/output scenarios. This revision had not run in target NX. Manifest, README and guide updated.

## 2026-10-06 — Postprocessing V1.39

- Restyled diameter errors with a white background, red Segoe UI text, even spacing and two equal buttons. Added Continue? and removed old labels/icon.
- Green No stops; bright-red Yes continues while bypassing this check's errors for the current run. Original messages/full mismatch list remain. No is initially selected; Escape stops.
- Only this dialog class and version changed. Tool/MCS checks, NC output, paths, INI and other windows were retained without extra files.
- Checked C# 7.3 compilation with NX API models, controls/layout, choices, default state, resource disposal and previous postprocessing/INI-preservation cases. Native appearance/scaling still required NX validation. Manifest, README and guide updated.

## 2026-10-06 — Postprocessing V1.38

- Added Machine folders to destination selection: multiple local/network roots, labels and removal; supports Browse and pasted paths including Windows Copy as path quotes.
- Save updates only `[MachineRoots]` in the existing INI, retaining other sections, post paths, comments, encoding and line endings. Cancel writes nothing; removing an entry does not delete directories/programs.
- Saving refreshes/resizes machine buttons immediately and requires selecting the destination again. BIN, numbering, names, offsets and manual-folder behavior remain. Discovery still uses immediate all-digit subfolders and explicit `[Machines]` entries.
- Invalid/inaccessible new paths, no numbered subfolders, invalid labels or an INI write conflict block saving. Unchanged unavailable roots can remain with warnings. No extra service files.
- Checked C# 7.3 compilation with NX API models, UTF-8/UTF-16 settings, cancellation, multiple roots, refresh/reselection, access errors and write conflicts. Target NX/network validation remained necessary. Manifest, README and guides updated.

## 2026-10-06 — Setup sheets V2.44

- Added shared Undo history of up to 100 actions, top-bar Undo, Ctrl+Z outside text fields and a setup-level Undo action button. Text fields keep normal typing Undo.
- Undo covers text/locating data, arrow/scale parameters, column widths/visibility, operation moves, cells/images and model view. A drag or slider movement becomes one step.
- Deleted original views and pasted images are restored with grid, dimensions, scale, pan and IPW variant. History stays in memory without extra files or per-step copies of model geometry.
- Save/autosave retain open-sheet history; an undone state is saved as a new revision, including after an in-flight save. Reopening clears history.
- Chromium checks covered editor actions, image deletion/restoration, text Ctrl+Z, model, save/reopen and printing; real autosave and Windows/Linux checks passed. Target NX validation remained necessary.

## 2026-10-06 — Postprocessing V1.37

- Added an initially unchecked Number operations option. Without it, original names/prefixes remain and no extra numbering Undo mark is created. It applies to full and selected-operation output and is not stored in INI.
- When enabled, NX operation names received `1_`, `2_`, … in tree order, restarting for each output program. Old leading numbers were replaced; selected-operation mode renamed only selected operations.
- Cancellation/failure before the first saved NC file restores names. Successful/partial publication retains names and an NX Undo mark. NC transformations, output paths, offsets and BIN were unchanged.
- Added Postprocessor paths for common/per-program post selection: named entries, add/remove and TCL/DEF selection with automatic matching DEF.
- Save writes post settings to the main INI and reloads the list, retaining other settings/comments/encoding/line endings. Cancel writes nothing; removal does not delete post files. First save can create a missing INI.
- Checked C# 7.3 compilation with NX API models, both numbering states, repeat/rollback, post settings/INI and different-post output. Target NX validation remained necessary. Manifest/guides updated; stable filename retained.

## 2026-10-06 — Setup sheets V2.43

- Fixed a one-way coupling between global sliders: MCS-arrow size and outline thickness no longer change operation row/text scale. The scale handler accepts only its intended controls.
- Double-clicking a label resets only that parameter to 100%. Independent sheet/setup settings, tool scale and first-sheet image scale are retained.
- Chromium checks covered keyboard adjustment, double-click without text selection, arrow/outline geometry, saved HTML reopened without a draft and printing. Windows/Linux checks passed; target NX validation remained necessary.
- Auto-width allocation to Operation, paths, INI and autosave remain, without extra files.

## 2026-10-06 — Setup sheets V2.42

- Changed operation-table auto-width priority: Tool and other columns get their full content width; Operation receives the remaining space.
- Operation names may use ellipses or wrap when the sheet option is enabled; pagination accounts for wrapped row height.
- The priority also applies to saved tables in automatic mode. Separate tool lists, manual widths, paths, INI, notes, locating data and autosave were retained.
- Chromium checks covered full tool names/numeric values, wrapping, 60–300% scale, column hiding, absence of internal scrolling, save/reopen and printing. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.41

- Changed auto-width to reserve full values first, leaving remaining width for tool names with ellipses. Measurement uses the actual font, units, indentation and repeated group headings.
- Tables fit to the sheet on screen and in print when space is insufficient. Removed internal horizontal scrolling; navigation between sheets remains. Fitting repeats when scale or column visibility changes.
- Numeric values are no longer artificially shortened because of measurement error. Pagination accounts for fitting and rounded row boundaries at screen scale.
- Manual widths, restoring auto width, notes, locating data, paths, INI and autosave retained; no extra files.
- Chromium checks covered 60–300% scales, column switching, long values, save/reopen and printing. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.40

- Added autosave-helper fallback when NX's embedded Python has no working standalone `python.exe`. It uses the already loaded interpreter DLL/libraries in a separate standard Windows PowerShell process.
- The launcher uses in-memory Reflection.Emit, without extra PS1/EXE/DLL/log/INI/temp files, Python installation or changes to PATH, registry or execution policy.
- HTTP service, address/origin/key checks, revision protection and coordination with regeneration remain. If startup is unavailable, completed HTML still opens directly.
- Startup errors now include available helper diagnostics from memory. Added real Windows DLL-launch checks, blocked-primary-interpreter cases, long paths, repeated journal runs and save conflicts.
- Stable filename, output paths, INI, notes, locating data and editor retained. Target NX validation remained necessary.

## 2026-10-06 — Setup sheets V2.39

- Reduced staging-path nesting that caused WinError 206 for long project names. One short staging folder sits beside the PRT while remaining on the required volume for atomic replacement. Final HTML path, PRT path and INI format are unchanged.
- Python file operations, INI, regeneration and autosave use extended Unicode Windows paths. Normal/existing short capture paths are checked before NX model building/capture; excessive paths without 8.3 support stop early. No registry, drive or project-name changes.
- Moved the local helper out of NX Python threads into a separate process of already available Python. An HTTP check precedes browser opening; failure automatically opens HTML directly. No helper installation/files.
- Reading/replacing an old sheet is coordinated with autosave through memory/process channels rather than lock files. Revision checks, notes, locating data, UI and Zmin are retained.
- Added Windows long-path, compact-staging, repeated-run, automatic-opening, concurrent export/autosave and blocked-main-interpreter response checks. A control run in target NX remained necessary.

## 2026-10-06 — Setup sheets V2.38

- Aligned the Zmin column with `NX_Operation_Zmin.py` V1.02, using actual ToolAxis for both Three and Five motions. Operation names were unchanged.
- Added a local helper: browser access through `127.0.0.1` with autosave to the original HTML without first selecting a file. In this release the helper ran in NX process memory, without installation/logs/service files.
- Autosave waits for editing/dragging to pause. Identity/revision checks prevent overwriting another tab or fresh export; retry after a lost response does not create copies.
- The helper accepts writes only from its page to a registered file. Draft/download-copy remain if unavailable; direct HTML opening retains browser saving. No new HTML notifications.
- Checked portable code, Zmin trajectory models, local HTTP protocol and Chromium editor. Target NX execution still required checking.

## 2026-10-06 — Updater V1.16

- All five column headers sort rows; repeated clicks reverse direction and show an arrow. Versions sort numerically.
- Sorting retains checkboxes/selected row; another Check in the same window retains sort order.
- Double-clicking any row cell toggles selection; double-click directly on the checkbox counts once.
- Unavailable-row restrictions and selection lock during installation remain. Added sorting, selection-retention, mouse-event and real Windows-list checks.
- Manifest version/hash updated. V1.15 WinHTTP fix retained; minimum updater remains V1.13.

## 2026-10-06 — Updater V1.15

- Fixed fallback-download error `WinHttpSetOption(TLS): 12018` from V1.14 by passing `WINHTTP_OPTION_SECURE_PROTOCOLS = 84` for the session instead of unrelated option `31`.
- The WinHTTP test model now checks option validity for the handle type: old code reproduces error 12018 and the fix passes.
- Added a real Windows WinHTTP test creating native handles and configuring TLS while intercepting send before network exchange. Skipped on Linux.
- TLS 1.2, certificate/hash checks and existing INI are retained without system changes. Manifest version/hash updated; minimum remains V1.13.

## 2026-10-06 — Updater V1.14

- NX Python `CERTIFICATE_VERIFY_FAILED` triggers a Windows WinHTTP retry using system certificate-chain validation, proxy settings and TLS 1.2. Certificate verification is not disabled.
- Fallback accepts only the original repository raw HTTPS address, rejects redirects and retains size/completeness limits, cancellation and SHA-256 checks. Downloads remain in memory without extra files.
- A Windows certificate failure explains that an administrator should check trust, date and antivirus/proxy HTTPS inspection.
- Added 12 download/handle-release regression tests; 43 tests passed outside NX. The affected PC still required validation.
- Manifest version/hash updated; minimum remains V1.13. Script name and INI format retained.

## 2026-10-05 — Updater V1.13

- Renamed the updater to `NX_Update_Scripts.py` and main settings to `NX_Update_Scripts.ini`. Updated example INI, catalog, links, validators and tests.
- If the new INI is absent, the old `NX_Update_Script_Buttons.ini` is read/written without creating a second copy.
- Raised catalog minimum to V1.13. Old clients need a one-time manual filename migration; installation instructions cover INI and button-path changes.
- Added step-by-step NX button instructions. Create a button once at a stable path; updates replace contents.
- Both updater names are excluded from ordinary folder updates. GitHub self-update of the new name remains deferred until the window closes.

## 2026-10-05 — Updater V1.12

- Changed the default working folder to `C:\ProgramData\NX_SCRIPTS` and updated examples/guides.
- Existing INI paths are retained. Relocation and button-path changes remain manual.
- Updated updater/example-INI hashes and version in the manifest. Minimum remained V1.10.

## 2026-10-05 — User documentation

- README became the catalog/installation entry point, with detailed separate script guides.
- Removed internal bundle notes and private-conversation links; instructions are written for new users.
- Clarified installation, configuration, error reporting and Zmin requirements.
- Script source, versions and settings were unchanged.

## 2026-10-05 — Updater V1.11

- Fixed redraw during rapid resizing: controls move in one batch without copying stale pixels, then background, child controls and borders repaint. Drag end also triggers full redraw.
- Added sibling clipping, minimized-window handling and fallback control movement if batch layout fails.
- Added seven resize-handler checks. Minimum-version testing now models an old client independently of current source version. Total: 29 tests; NX pixel drawing remained a separate check.
- Manifest version/hash updated; minimum remained V1.10 so self-update was available.

## 2026-10-05 — Zmin V1.02

- Fixed rotated-part Z calculations: Three-format paths can carry a tilted ToolAxis, which V1.01 replaced with MCS Z. Both Three and Five now use the actual motion vector.
- The MCS origin must lie on the part's rotation axis. Ordinary three-axis results remain when tool axis aligns with MCS Z. Unavailable axes and varying-axis arcs/helices are skipped with explanations.
- Passed 25 tests outside NX; checked 8,346 saved CL points from 35 operations and replayed 17 full paths including arcs/helices. A control run of the new version inside NX remained necessary.
- Updated catalog version/hash, README and guide; stable filename retained.

## 2026-10-05 — Script guides

- Short descriptions in README/manifest now describe functionality without revision history.
- Added detailed guides for all 13 journals: features, use, settings, output and limitations.
- Clarified postprocessing modes, sheet editing, eight PDF line groups, three DXF modes, numbering file operations and assembly renaming.
- Added an NX walkthrough for updater V1.10; Windows/Linux automated checks passed.
- Source, script versions, INI and register template were unchanged.

## 2026-10-05 — Updater V1.10

- Fixed Windows CI `WinError 5` when replacing an existing script or updater INI: close the updater's own protective handle immediately before atomic replacement of that file.
- Retained pre-write hash checks, protection of other files and ordinary-error rollback. The rollback test now verifies that the first replacement really happened before simulating failure of the second.
- Raised minimum to V1.10. V1.08/V1.09 users needed a one-time manual updater replacement while keeping their INI.

## 2026-10-05 — First public GitHub bundle

Published 13 journals for CAM, setup sheets, drawings, export and file management. Working filenames are stable; versions are inside each source file.

### Updater V1.09

- GitHub uses one manifest for Check, then downloads selected sources for installation.
- Validates schema, paths, SHA-256 and internal versions; blocks downgrades.
- Retains selection UI and working paths. Folder mode is manual, without automatic fallback on errors.
- User INI and numbering history are not updated alongside scripts. Examples are created only for an absent script with absent settings.
- Writes through a checked adjacent file with atomic replacement and in-memory rollback for ordinary errors. Pending files are removed afterwards; leftovers from ended processes are removed next launch.
- Self-update follows window closure/thread completion. V1.08 required one-time manual replacement.
- Blocks a second window in the same Windows session. Does not create NX buttons/New User Commands.
- Added data-preservation tests and Linux/Windows CI; these do not replace NX/Win32-window validation.

### Setup sheets V2.37

- Notes grow vertically without shrinking the font and can be resized from the bottom; height is saved.
- `NX_Setup_Prototype.ini` supplies the programmer name; blank leaves the field empty.
- Clarified INI-read errors.
- Included toolpath Zmin relative to operation MCS.

### Minimum Z in operation names V1.01

- Incorrect Z for some rotated-part paths was discovered in V1.01 and corrected in V1.02.
- Added renaming for selected operations or all project operations with empty selection.
- Adds/replaces a terminal numeric `_Z` suffix. Four-axis calculation uses the current tool direction; the MCS origin must lie on the part's rotation axis.
- Retained path/name-conflict checks and NX Undo without separate settings/service files.

### ESKD V1.35

- Removed embedded ASCON font bytes because redistribution permission was not established.
- Loads a compatible external copy the user is permitted to use, from the script folder or supported NX/Windows locations.
- Retained size, CRC and actual-family checks. No automatic substitution of a similar font; formatting does not run without a compatible file.

### Other journals

Included postprocessing V1.35, operation renaming V1.04, numbering V1.03, tool descriptions V1.06, PDF V1.11, DXF V1.08, assembly renaming V1.05, project folder V1.01 and setup-sheet folder V1.01. Filenames were normalized and text saved as UTF-8 with LF.

Added schema-1 manifest, example INI files, an empty register, guides, third-party notices and publishing checks.
