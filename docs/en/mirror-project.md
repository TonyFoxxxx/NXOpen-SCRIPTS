# Mirrored CAM project — trial

[Русский](../mirror-project.md) | **English**

Script: [`NX_Mirror_Project.py`](../../scripts/NX_Mirror_Project.py). Version: **V1.30.04**.

<a id="filter-copy-v13004"></a>
## Latest changes — V1.30.04 · 2026-11-16

**V1.30.03 result in DC 2606:** the SleepyEntity guard stopped `ScCollector 608674` for body `181423`, **before AskRulesAndCollectedEntities or UpdateMap was called**. V1.30.03 rejected both enabled filters and non-bool results, but did not print the actual value. This report therefore establishes neither that value nor successful rule reading. Two Arcs again read back with deviation 0.0; the independent body was reflected and 118 faces / 298 edges matched. Local/outer Undo, source restoration, its unchanged checksum and clear modification flag were confirmed. Tags are specific to this run.

V1.30.04 replaces the requirement that all filters be disabled with a check of their actual effect. First read exact filter settings, BodyDumb rules, per-rule objects, array partitions and collector modes. Report values and bool types; unavailable/non-boolean results remain errors showing the actual type/value. Filter settings are part of the saved snapshot and are checked on subsequent reads.

A base wrapper with enabled filters uses a **separate temporary ScCollector inside the test PRT copy**:

- CopyCollector must return exactly one new collector in that part. Verify its new tag, ownership, non-occurrence status, ScCollectors membership, and matching rules/selection/modes/filters. An original, pre-existing or foreign collector may not be changed or destroyed.
- Remove filters **only from the verified new collector**, reading back each change. Do not edit the original collector; recheck its settings and readable data.
- Each unfiltered BodyDumb selection must equal the original exactly. A hidden extra body, missing/unknown object, invalid partition or changed rule stops the trial. GetObjects, available specialized GetData, and exact body-ownership checks remain.
- Destroy the temporary collector and verify ScCollectors membership and original settings. Repeated checks use a fresh copy, not cached evaluated output. A partial creation without a verified returned object is not manually deleted by guesswork: the enclosing candidate Undo must remove it, with explicit checks for leftover collectors. Preserve primary/cleanup errors, codes and tracebacks separately. Active-copy loss prevents further mutations and Undo in another part.

Specialized BodyDumbRule wrappers retain direct reading and need no extra collector. Rules are not rewritten and replacement Commit is not added. Geometry, history, sketch supports/constraints, MCS/RCS, Tooling Box, CAM references, source PRT and rollback retain their previous guards. No extra files are created: the temporary ScCollector is an object in the open test part, not a file or another PRT.

**Verified outside NX:** 719 focused checks, including the existing 137 using C fixtures. Add 25 regressions for filters, hidden inputs, copy ownership/results, destruction, partial failures, active-part loss and unverified Undo. CopyCollector/filter fixtures do not establish Siemens kernel behavior. Sources are published [NX 2512 Python API files](https://github.com/ugopen/nxopen_lib/blob/96855b43415ac81dcfdc83f8874ea5622c19d2db/NX2512/UGOPEN/pythonStubs/NXOpen/__init__.pyi) and [ScCollector.hxx](https://github.com/ugopen/nxopen_lib/blob/96855b43415ac81dcfdc83f8874ea5622c19d2db/NX2512/UGOPEN/NXOpen/ScCollector.hxx); SDK 2606 is unavailable here.

**Next run:** install **V1.30.04**, open the saved source PRT and select the same plane. Return the full report with filter values, collector reading/comparison, map checks and both rollback results. **Do not save** the experimental copy. AskRulesAndCollectedEntities, CopyCollector, changing filters on its separate result and destroying it remain unconfirmed in DC 2606; historical-support transfer and a complete mirrored CAM project are still later steps. [Separate SDK/API checklist](#replacement-probe-sdk-2606).

<a id="rule-members-v13003"></a>
## V1.30.03: per-rule object reading · 2026-11-16

**V1.30.02 result in DC 2606:** `GetRules()` returned a base SelectionIntentRule wrapper; `Type=34` matched the installed `RuleType.BodyDumb=34`. Python nevertheless rejected `BodyDumbRule.GetData(rule)` with TypeError: the method descriptor requires a BodyDumbRule instance. This establishes a Python binding failure, not an unknown operation type or a Part Boundary curve error. This run did not reach `UpdateMap`. Two standalone Arcs again read back with deviation 0.0; the independent body was reflected and 118 faces / 298 edges matched. The report confirmed local/outer Undo, source restoration, its unchanged checksum and clear modification flag. Collector tag `619472` and source body tag `627114` are specific to this run.

V1.30.03 removes the rejected method call on the base wrapper. It uses the published `ScCollector.AskRulesAndCollectedEntities()` to read rules and each rule's objects separately, returning `(objects, rules, ruleObjectArrayCount)`. The Python contract comes from a [published copy of NX 2512 API files](https://github.com/ugopen/nxopen_lib/blob/96855b43415ac81dcfdc83f8874ea5622c19d2db/NX2512/UGOPEN/pythonStubs/NXOpen/__init__.pyi); [Siemens ScCollector.hxx](https://github.com/ugopen/nxopen_lib/blob/96855b43415ac81dcfdc83f8874ea5622c19d2db/NX2512/UGOPEN/NXOpen/ScCollector.hxx) dates the method to NX 2312. This is **not SDK 2606** or evidence of its binding behavior.

The reader accepts explicit **BodyDumb** selection only. Both rule lists must match the installed NX type; each object count must be a positive integer and their sum must cover the entire object array exactly. All bodies must belong to the copy and equal the selected original body; duplicates, extra/unknown objects and disagreement with GetObjects are rejected. Rules and evaluated membership are read again. If the API supplies a specialized wrapper, its GetData is also cross-checked; its failure is not bypassed. Originally specialized wrappers retain their direct GetData route.

Before the new read, `IsEvaluationFilterEnabled` checks the installed `ScEvaluationFiltertype` members: SleepyEntity, LaminarEdge, MinimumBody, SheetBody, FacetEntity and additional exposed enum members. An enabled or unreadable filter stops the trial. **The script never disables filters**, rewrites rules or casts wrappers/pointers. GetObjects remains an independent comparison, never a substitute for unreadable per-rule objects. Other intent types, incomplete answers, unavailable API and NX errors still prevent mapping.

This checks explicit selection membership for a **map proposal without Commit**. It does not establish all hidden rule settings or other selection intents. Before applying history replacement, DC 2606 must verify the ordering/meaning of `ruleObjectArrayCount`, negated-object/filter semantics, and preservation of rule settings and associations after application. Complete-map, history, sketch, MCS/RCS, Tooling Box, exact CAM-reference, active-copy and Undo checks remain. Part Boundary transfer has not been reached; the standalone Arcs are candidate historical sketch supports.

**Verified outside NX:** 694 focused checks, including the existing 137 using C fixtures. The reader suite now has 29 scenarios covering the reported descriptor refusal, separate rule data, invalid partitions/types/objects, filters and drift, repeated reads, errors and rollback. The GetData fixture now rejects base wrappers as the real report does. These tests do not establish the new API call or its DC 2606 semantics.

**Original V1.30.03 trial:** install **V1.30.03**, open the saved source PRT and select the same plane. Return the full report containing `AskRulesAndCollectedEntities`, map checks and both rollback results, including any filter/API refusal. Do not save the experimental copy: this branch always attempts rollback and does not yet produce a complete mirrored CAM project. [Separate SDK/NX 2606 methods and definitions checklist](#replacement-probe-sdk-2606).

<a id="rule-reader-v13002"></a>
## V1.30.02: base-wrapper trial · 2026-11-16

**V1.30.01 NX result:** two standalone Arcs were created and read back again with deviation 0.0; the independent body was reflected and all 118 faces / 298 edges matched. The trial stopped before `UpdateMap`, while reading the rule of `ScCollector 241212` used by `EXTRACT_BODY 110549`: `GetRules()` returned a **SelectionIntentRule** wrapper with `Type=34`. V1.30.01 required the BodyDumbRule class and never called GetData. Local/outer Undo, source restoration, unchanged source checksum and a clear modification flag were confirmed. Tags are specific to that run.

V1.30.02 distinguishes the **Python wrapper class** from the **NX rule type**. Require a SelectionIntentRule object and Type equal to the installed NX `SelectionIntentRule.RuleType.BodyDumb` before reading. The number `34` is not hardcoded. Specialized wrappers retain `rule.GetData()`; a base wrapper tries the published reader with an explicit receiver, `BodyDumbRule.GetData(rule)`. The report prints both type values, the wrapper and the call route.

This is an **experimental Python API read**, not a confirmed cast in DC 2606. A binding that refuses the base receiver, an NX error, or empty/incorrect data preserves the error/code/traceback and triggers rollback. No alternate calls are tried after failure. Recheck the rule type after reading; body membership must agree exactly with the original definition and GetObjects. GetObjects is never substituted for unreadable rule data. Wrappers, internal pointers and collector rules are not reconstructed or changed.

Complete-map, history, sketch, MCS/RCS, stock, exact CAM-reference and Undo guards remain. Replacement `Commit` is never called in this branch. **679 focused checks outside NX**, including the existing **137 using C fixtures**, pass; 14 new scenarios cover base/specialized wrappers, installed enums, descriptor rejection, reader errors/invalid data, subsequent checks and rollback. The synthetic reader does not establish that Python NX accepts the receiver.

**Original V1.30.02 trial:** install **V1.30.02**, open the saved source PRT and select the same plane. The complete report should include the collector-rule read line with both type values and call route, then the read/map outcome and both rollback results. The experimental copy does not need saving. Sketch-support transfer and a complete mirrored CAM project remain unconfirmed.

The experiment uses older published Siemens contracts: [ScCollector.GetRules](https://www.ugapi.com/doc/nxopen/a05026.html), [SelectionIntentRule.Type](https://www.ugapi.com/doc/nxopen/a05063.html), [BodyDumbRule.GetData](https://www.ugapi.com/doc/NXOpen/a00714.html). They describe rule types/data but do not establish Python 2606 acceptance of the base wrapper. [Separate SDK/NX 2606 checklist](#replacement-probe-sdk-2606).

<a id="collector-map-v13001"></a>
## V1.30.01: collector-input expansion · 2026-11-16

**V1.30 NX result:** two standalone Arcs were created and read back again with deviation 0.0. Source body `580746` produced independent BREP `571964`; all **118 faces and 298 edges** were matched, and only the candidate was reflected. `GetMatchedReferences` then returned `ScCollector 170651`, the body selection of retained `EXTRACT_BODY 162517`. V1.30 stopped before assigning matches: a collector cannot be mapped as a single edge. The report confirms local/outer Undo, source restoration, unchanged source checksum and a clear modification flag. Tags are specific to that run.

V1.30.01 adds a checked transition to `ReplaceFeatureBuilder.DisplayUniqueInputsToMap=True`, requesting mappings of individual parent objects. Original collectors and rules **are not rewritten**. This branch still never calls replacement `Commit`.

- Before the first `UpdateMap`, read collectors using the selected source body. Expansion currently requires a `BodyCollector` row without reversal and fully read `BodyDumbRule.GetData`: the original body must agree with `GetObjects`, rule definitions and the proven geometric map. Collector modes are checked. Unknown rules, other types, empty/unreadable data or different membership stop the experiment. The V1.30 report **does not identify the rule type**; BodyDumb still needs confirmation in NX.
- Original collectors must be present in the first map. After changing the view, require exactly the previous individual parents plus the bodies read from those collectors. Missing collectors/bodies/historical arcs, extra rows and changed types/directions are rejected. Assign the explicit original-body to candidate-body match; do not pass a body as the replacement for a collector to `SetMatchingForParent`.
- Recheck collector membership and definitions after map generation, match assignment, builder `Destroy` and local Undo. Existing history, sketch, MCS/RCS, stock, exact CAM-selection and `Tool Position = On` guards remain. Unconfirmed state is rejected; automatic matching stays disabled.

**Verified outside NX:** 665 focused checks, including 137 using C fixtures; 30 new regressions cover collectors, complete mapping, no Commit, failures and rollback. These exercise our code and synthetic API contracts, not DC 2606 DLLs or its kernel. The V1.30 report does not establish that the new map or association transfer works.

**Original V1.30.01 trial:** install **V1.30.01** through the updater, open the saved source PRT and select the same plane. Send the complete report, including the collector row/rule, unique-parent map and both rollback results. The experimental copy does not need saving. Sketch-support transfer, history replacement and a complete CAM copy remain unconfirmed.

Sources are older published Siemens contracts: [ReplaceFeatureBuilder](https://www.ugapi.com/doc/nxopen/a03024.html), [ScCollector](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.ScCollector.html), [BodyDumbRule.GetData](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.BodyDumbRule.html). They are not SDK 2606; the [separate verification list](#replacement-probe-sdk-2606) is extended below.

<a id="historical-map-v130"></a>
## V1.30: historical-support map · 2026-11-16

**V1.29 NX result:** after three successful historical reads, two standalone reflected Arcs were created. Native readback checked nine parameters per candidate with deviation **0.0**. This run used `145875 → 160132` and `145971 → 160133`; source body `146565`, external contexts `131334`, `131338`, `131342`, and initial current feature `131352`. Tags are specific to this run. History-context restoration, candidate removal through local Undo, outer Undo and source restoration passed their checks; the source PRT checksum was unchanged and its modification flag was clear.

This establishes creation of **two standalone arcs**, not their use in place of Edge references in associative definitions. Sketch supports/constraints, original body history and selected CAM curves were not replaced. A complete mirrored project and toolpath regeneration remain unconfirmed; positive Line/Ellipse/Spline paths were not exercised in NX by this run.

V1.30 extends this branch to a `ReplaceFeatureBuilder` map check, **without calling its Commit**:

1. Retain historical reads and standalone-curve creation/readback. Every historical support must belong to the selected body and have a checked candidate.
2. Use the existing mechanism to extract a separate non-associative body, verify independent history/parameters and remove parameters only from that new candidate. Original body history is retained. `ExtractFaceBuilder.CommitFeature` creates a new body here; it does not apply replacement of original history.
3. Before reflection, build a unique complete final-face/edge map from geometry, topology and incidence. Reflect only the new BREP and check its identifiers and position. Recheck candidate curves and body after map preparation and builder destruction.
4. Select the original features and new BREP in the replacement editor. Disable automatic geometric matching and assign explicit matches. Every used final-topology support and every historical edge must appear. Unknown, missing or ambiguous rows stop the probe.
5. Historical Edge rows require `MatchedReferenceBuilder.Type == Types.Curve` and `ReverseDirection == False`. Do not silently fix direction or force Type/MatchedStatus. After `SetMatchingForParent`, read back the exact candidate tag, unchanged type/direction and a resolved ByUser or BySystem status. Check row membership, both selections and Validate on each row, the reference list and builder. Do not coerce Section/ScCollector or unknown definitions into single curves to pass.
6. Destroy the builder **without Commit**. The existing local mark then undoes both curves and the candidate body. Restoration requires only original bodies/features/curves, the initial current feature, history states/expressions, geometric references, MCS/RCS, Tooling Box and CAM. Remove the owned mark only after checks; retain outer copy rollback.

A verified map and Validate result **do not establish that applying it is valid**. Edge → Curve compatibility for every consumer, sketch-constraint preservation and identifiers after actual replacement remain untested. This branch does not replace the original body, transfer MCS/stock, change Part Boundary/On or delete toolpaths. Unknown numeric CAM settings remain reported as unverified. Other parts/Undo are protected; no automatic saves or additional files are introduced.

**Verified outside NX:** 635 focused checks, including the existing 137 using C fixtures. The 34 new regressions cover complete available maps, absence of Commit, missing/unknown/duplicate rows, type/direction/status, changed selections/candidate geometry, MCS/CAM, cleanup/Undo errors and active-copy loss. A builder fixture does not establish NX kernel behavior. The subsequent run confirmed extraction and topology matching, then stopped at ScCollector as described above.

**Original V1.30 trial:** update to **V1.30**, open the saved source PRT and use the same plane. After two checked arcs, expect “ПРОТОТИП КАРТЫ ЗАМЕНЫ” with extraction, topology matching and historical-row results, followed by local and outer rollback. A failure needs the complete stage/error report. The experimental copy does not need saving.

This experiment uses older Siemens contracts: [ReplaceFeatureBuilder](https://www.ugapi.com/doc/nxopen/a03024.html), [MatchedReferenceBuilder](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.MatchedReferenceBuilder.html), [row types](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.MatchedReferenceBuilder.Types.html) and [statuses](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.MatchedReferenceBuilder.ResolvedStatus.html). SDK 2606 is unavailable; see the [separate verification list](#replacement-probe-sdk-2606).

<a id="native-curves-v129"></a>
## V1.29: standalone curve candidates · 2026-11-16

**V1.28 NX result:** two historical-edge definitions were read across three external sketch contexts. Both supports are Arc with radii near 0.5 mm; nine parameters per reading were checked, with maximum deviation `2.842170943040401e-14` part units. All three reads succeeded. Manager cleanup, local Undo, model restoration, outer Undo and source restoration passed their checks; the source PRT checksum was unchanged and its modification flag was clear. This establishes reading **these arcs**, not Line/Ellipse/Spline execution, native creation or project transfer.

In this run the edges had tags `597080` and `114040`; external contexts were `151110`, `151114`, `151118`, and the body was `171402`. The second edge had identical definitions in two contexts. Tags are specific to this run. These arcs are **historical supports of dependent features**, not the two selected Part Boundary centerlines in SKETCH_000.

V1.29 tests the next step: creating a standalone NX Curve from each captured reflected definition. Creation starts only after **all** historical contexts have closed and restoration has been checked. There is one candidate per unique source edge; any failed history read prevents this stage.

- Line uses `CurveCollection.CreateLine`; Arc/Ellipse use the `CreateArc/CreateEllipse` overloads taking an ABS center, two direction vectors, radii and angle limits. Both curve directions follow reflection R; the MCS axis convention does not apply.
- Spline uses `UF_CURVE_create_spline` with complete knots and homogeneous poles. Explicitly normalize the full parameter domain to `[0, 1]` beforehand, including knots and trim; preserve geometry and weights. Reject every returned state/automatic fix. Release the states array with `UF_free`, including partial error results. Mutation of the supplied definition is also an error.
- Each result must have a new tag, belong to the trial copy and be a Curve rather than an Edge. Read it back through `UF_EVAL_initialize_2`: check type, periodicity, center/directions/radii, limits, spline poles/weights/knots/order and sample points. Matching bounding boxes alone is insufficient; unknown or unreadable data are not skipped.

The stage owns a separate visible Undo mark. A Curves snapshot checks collection membership and previous curve bounds; existing history, topology, reference, sketch, MCS/RCS, Tooling Box and CAM guards remain active. Success or failure triggers an attempted **local candidate Undo**, followed by collection and original-snapshot checks before deleting the owned mark. The existing outer rollback follows. Loss of the active copy never authorizes Undo in another part; unconfirmed restoration is not reported as success.

New curves **are not assigned to sketches or CAM geometry**. A correctly shaped Curve is not yet a valid substitute for an Edge in an associative definition. Definition/constraint transfer, body-history replacement, MCS/stock transfer and CAM regeneration remain subsequent steps. This branch deletes no toolpaths, saves nothing automatically and creates no extra files; rollback removes the candidates.

**Verified outside NX:** 601 focused checks, including 137 using separately compiled C fixtures. The 39 new checks cover measured report arcs, creation after context cleanup, one candidate per edge, strict result readback, failed/partial creation, retained CAM selection, local Undo and the spline C ABI. These test our code and fixtures, not Siemens DLLs. At V1.29 release time, native creation was untested in DC 2606; the subsequent report confirmed two Arcs and rollback, as documented above.

The subsequent V1.29 run is documented above. The current V1.30.04 experiment checks a proposed replacement map without applying it and also requires rollback.

Creation contracts come from older [CurveCollection, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.CurveCollection.html) and the [published copy of Siemens uf_curve.h](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_curve.h). SDK 2606 is unavailable; the [separate method/definition checklist](#replacement-probe-sdk-2606) has been extended.

<a id="curve-definitions-v128"></a>
## V1.28: historical curve definitions · 2026-11-16

**V1.27.02 NX result:** the access experiment completed successfully. Three external sketch contexts `128710`, `128714`, `128718` opened; exact edges `120469` and `251637` of body `134471` were read, the latter in two contexts. The report records three successful reads and zero errors. Manager cleanup, local Undo and restoration checks passed for each context, followed by outer Undo, source restoration, an unchanged checksum and a clear modification flag. This establishes access and return on this PRT, not complete curve definitions or mirroring. Tags identify this run only.

V1.28 extends the experiment: read an edge definition inside each historical context using `UF_EVAL_initialize_2`, then check its reflection **in memory only**. Do not use legacy `UF_EVAL_initialize`: its published header permits spline approximation of procedural edges. A missing new initializer stops before opening a context; there is no approximating fallback.

- **Line:** confirmed type, actual parameter limits and trimmed endpoints; intermediate UF evaluations check linear parameterization.
- **Arc / Ellipse:** ABS center/axes, radius or both semiaxes, periodicity, definition and trim limits. Both curve axes follow reflection; the MCS axis rule does not apply.
- **Spline:** order, periodic/rational flags, complete knots and homogeneous poles `(wx, wy, wz, w)`. Array sizes must match `UF_EVAL_ask_spline`; knots, trim and positive weights are checked. Reflection applies weighted translation to homogeneous coordinates; knots, weights and limits are retained.

An independent evaluation of the definition is compared with `UF_EVAL_evaluate`: nine parameters across the range plus interior samples in every nonempty spline span. Endpoints must match `Edge.GetVertices`, allowing the two vertex points in reverse order. Evaluate the reflected definition against reflected sample points. The tolerance is `1e-5` part units. This checks a curve representation; it is **not an NX replacement map or proof of constraint transfer**. All parameters, not just samples, remain in memory. Different definitions of the same tag across contexts prevent treating it as a shared support.

Unknown types, procedural curves without an available exact representation, full surface definitions, unreadable or inconsistent data fail without approximation or substituted supports. Limits: 20,000 poles, order 2–64, up to 4096 sample parameters. Returned arrays use `UF_free`; evaluators use `UF_EVAL_free`, including partial outputs on failure. Unconfirmed evaluator release prevents further history contexts; manager cleanup and rollback are still attempted. Errors retain their code and traceback.

All **562 focused checks outside NX**, including **122 using C fixtures**, pass. The 44 new checks cover analytic geometry, a rational spline with an independent closed form, oblique reflection, trim/repeated knots, C structures/arrays, partial failures, rejection of approximating fallback and context restoration. These use our fixtures, not Siemens DLLs; new UF_EVAL calls remain untested in DC 2606.

Historical-read results and the current V1.30.04 experiment with mandatory rollback are described above.

Contracts were checked against a [published Siemens `uf_eval.h` copy](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_eval.h) and the [NURBS definition in `uf_curve.h`](https://www.ugapi.com/doc/ufun/uf_curve/UF_CURVE_spline_s.html). These are older contracts; **SDK 2606 is unavailable**. [Outstanding API checks](#replacement-probe-sdk-2606).

<a id="visible-undo-v12702"></a>
## V1.27.02: visible Undo marks

Fix confirmed NX error **690037** at the first `StartEditWithRollbackManager` call: the manager requires **a visible Undo mark**, while V1.27/V1.27.01 supplied an invisible one. Each historical context now receives a separate `Session.SetUndoMark(Session.MarkVisibility.Visible, ...)` mark. The outer copy mark is not reused. The report records the mark kind; a missing Visible enum value stops execution before mark creation and manager start.

Cleanup order is unchanged: UpdateFeature/Stop/Destroy when a manager was obtained, then local Undo, restoration checks and deletion of only that context's own mark. Opening/cleanup errors keep their code/traceback; unverified restoration prevents further contexts. The experimental branch still ends with mandatory outer copy rollback. Geometry/MCS rules, exact selections, stock and no automatic saving remain unchanged.

**Established by the V1.27.01 report:** the previous MCS-position stop is resolved; the original-state check passed. The manager call for `SKETCH_001:Sketch(7)`, feature `109062`, was attempted, but NX rejected the invisible mark. Local Undo and model/current-feature restoration checks passed after that rejection, followed by outer Undo, source restoration, an unchanged checksum and a clear modification flag. No historical context opened and no historical edges were read; UpdateFeature/Stop/Destroy remain untested by this run. The tag identifies this report only.

The manager fixture now rejects invisible marks with code 690037. Add four regressions for rejection/verified return, missing Visible, use of the NX enum value, and separate context marks preserving existing marks. Previous MCS, manager-failure, CAM and rollback scenarios run under this requirement. All **518 focused checks outside NX** pass, including **97 using C fixtures**. These test logic with synthetic data, not successful historical access in DC 2606.

The current trial uses **V1.30.04** from the saved source PRT with the same plane. The experimental result does not need saving. Complete historical-support and mirrored-CAM transfer remain unconfirmed; the [SDK 2606 checklist](#replacement-probe-sdk-2606) is retained.

<a id="mcs-phase-v12701"></a>
## V1.27.01: expected MCS support state

Fix the preliminary MCS check exposed by the V1.27 report. Before the first historical context opened, unchanged Three Planes support `89014` was incorrectly compared with its future reflection. For the X = −79 mirror, the original point near `(0, 0, −12)` was rejected because the validator already expected `(-158, 0, −12)`. This is an incorrect expected-state choice in the journal; the report does not establish that NX moved the plane.

Independent-body preparation, reflection of the separate candidate only, and checks before history access / after Stop / after local Undo now require **original support positions**. After replacing the original body with its mirror and during normal MCS transfer, **reflected support positions** remain required. Live objects, plane methods, exact ordered face selections, definition graphs, CSYS origins and axes remain checked. Actual displacement, a coplanar-face substitution or incomplete restoration still fails. The stage now names the CSYS, X/Y/Z field, support and expected state; general origin/axis checks no longer label other CSYS types as Three Planes.

**Established by the V1.27 run:** manager methods and UF exports were found, feature/expression states and current feature `93080` were read. Two missing edges, `83410` and `83314`, again have six reference links. In this run the three Datum CSYS are internal, while sketches `93062`, `93066`, `93070` are external direct consumers. Three contexts were prepared, but `StartEditWithRollbackManager` was never called: the initial MCS check stopped execution. Historical-edge access, transitions/Stop/local Undo, extraction and reflection are therefore untested by this report. It records outer Undo, source restoration, an unchanged checksum and a clear source modification flag. Tags identify this run only.

Add **15 regressions** using actual MCS validators over synthetic objects: X = −79, an oblique mirror, direct Face and Plane supports, exact selection order, damaged positions/normals/definitions, candidate preparation, and external sketches with restoration checks after Stop/Undo. Two new scenarios reproduce the premature −158 mm expectation against published V1.27 and pass with the fix. All **514 focused checks outside NX** pass, including the existing **97 using C fixtures**. These do not prove manager or CAM operation in DC 2606.

After this revision, checks continued in V1.28; the current trial is **V1.30.04**, described above. The experiment still ends with outer rollback even after successful reads; its result does not need saving. V1.27.01 introduced no new API; the [SDK 2606 checklist](#replacement-probe-sdk-2606) now also lists V1.28's curve readers. Complete project mirroring is still unconfirmed.

<a id="historical-probe-v127"></a>
## V1.27: historical-support access experiment

Following the V1.26.02 result, a separate experiment reads exact intermediate Face/Edge supports in a history context exposed by `EditWithRollbackManager`. This is **an access check with mandatory rollback**, not intermediate-geometry transfer or a completed mirrored CAM project. Both R1 centerlines remain selected sketch curves; body edges are not substituted.

When the initial snapshot finds supports outside final topology, the journal collects their external direct consumers from actual UF references and sorts them by `Feature.Timestamp`. Report tags are not hard-coded. An internal feature (`IsInternal=True`) is not opened separately; its reference remains in the report and restoration checks. Every unique support needs at least one external direct consumer. This does not prove access at an internal sketch's own history state. Suppressed consumers, protected stock, unreadable properties or missing APIs stop the experiment before a history transition.

Each external consumer gets its own visible Undo mark (corrected in V1.27.02 after NX error 690037). `StartEditWithRollbackManager(feature, mark)` opens the context; the journal reads **the same tag**, its UF owner, membership in that body's current topology, ABS bounds and two `Edge.GetVertices` points. The fast `UF_MODL_ask_bounding_box` bounds may be approximate; bounds and endpoints **are not a complete curve/surface definition and are not used to select a substitute object**. No geometry or feature-editing Builder is created.

Context cleanup uses `UpdateFeature(errorDuringFeatureEdit)` → `Stop()` → `Destroy()`, followed by local Undo. After Stop and after Undo, compare the current feature, Timestamp/Suppressed/IsInternal, expression definitions, history/warnings, body/face/edge identifiers and positions, exact UF references, sketch membership, CAM selections/boundary settings, MCS/RCS and Tooling Box. Failed restoration prevents another context from opening. Read errors can be collected in subsequent contexts only after verified restoration; each keeps its stage, text, available code and traceback. If the copy ceases to be Work/Display, Undo is not called in another part; the existing fallback attempts to close only the created copy without saving.

Even successful reading ends this branch and invokes the outer copy rollback. Extraction/replacement, reflection, MCS/stock transfer and DeleteToolPath do not follow it. References fully accessible in final topology retain the previous one-body prototype and its guards. Actual replacement still requires complete historical geometry, a unique reflected correspondence, and verified identifiers and sketch constraints.

**Checked outside NX:** 499 focused checks, including 97 using C fixtures. New scenarios cover the report's six links, external/internal contexts, Start/Update/Stop/Destroy/Undo failures, preserved R1 curves and On, rejection after damage, and exact C outputs. These test the protocol and ABI against our fixtures, not Siemens DLLs or DC 2606 compatibility.

Historical-read results and the current V1.30.04 experiment with mandatory rollback are described above.

The manager sequence comes from [older Siemens C++ documentation](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a56381.html), the start signature from [FeatureCollection, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.FeatureCollection.html), and bounds from [uf_modl_utilities.h](https://www.ugapi.com/doc/UFun/uf_modl/uf_modl_utilities.html). **SDK 2606 is unavailable**: Python signatures, read-only manager use without a feature-editing Builder, intermediate-object visibility and actual restoration require DC 2606 verification. [Separate API checklist](#replacement-probe-sdk-2606).

<a id="result-v12602"></a>
## V1.26.02 NX result: two inaccessible supports

The latest user report localizes the blocker: six unique faces/edges are confirmed in body `80517`'s final topology, while **two other edges have six links from retained definitions**. Neither appears in any body's captured final topology. After `UF_MODL_ask_edge_body`, the owner tag remained zero; the call did not report a nonzero UF error. The journal rejected the unreadable output itself. This differs from the preceding run's GetBody exception 630003: that code must not be attributed to the new failure. The null result does not establish either edge's ownership by the selected body or the reason for its absence.

| Edge in this run | Retained consumers |
| --- | --- |
| `64791` | Datum CSYS(7), feature `69573`; `SKETCH_001:Sketch(7)`, feature `69574` |
| `73591` | Datum CSYS(9), feature `69576`; `SKETCH_002:Sketch(9)`, feature `69578`; Datum CSYS(10), feature `69579`; `SKETCH_003:Sketch(10)`, feature `69582` |

SKETCH_001 returns empty geometry/expression lists but still has support references. This does not authorize deleting or skipping it. SKETCH_002 and SKETCH_003 are nonempty. Another object on the same line/plane does not prove preservation of the original selection. These tags identify this run only; they are not permanent script settings.

Both selected R1 centerlines are accessible Arc objects belonging to **SKETCH_000**, feature `69571`, Sketch object `69495`. Its coordinate-system supports and external edge occur in final topology. Initial CAM data contains:

| Operations | Selected arc | Member Tool Position |
| --- | --- | --- |
| `R1_CHERN_Z-0.97`, `R1_CHIST_Z-1` | `65349` | `has_tool_position=1`, `On` |
| `R1_CHERN_Z-1.149`, `R1_CHIST_Z-1.179` | `65353` | `has_tool_position=1`, `On` |

Candidate creation, reflection and history replacement had not started. The report records Undo, source restoration, an unchanged checksum and a clear modification flag. CAM preservation after transformation, final curve positions and toolpath generation are **not tested** by this run.

**The current prototype cannot transfer these intermediate supports.** A map of final faces/edges alone is insufficient for this PRT. Further implementation requires reading each support's geometry at its applicable history state, a verified reflected correspondence, and preservation checks for the original selections, sketch definitions/constraints, projections and MCS after replacement. Replacement must remain blocked until this map exists.

The older Siemens contract describes `UF_MODL_ask_alive_edge` as resolving edge labels after a merge; a null owner does not establish that case. Published API exposes `ExtractFaceBuilder.FixAtCurrentTimestamp` / `FrecAtTimeStamp`, but the available descriptions do not establish extraction of these supports with preserved identifiers in 2606. V1.27 uses `UF_MODL_ask_current_feature` only to read the current feature for restoration checks. `UF_MODL_set_current_feature` and `Feature.MakeCurrentFeature` are unused: changing the insertion point alone does not establish historical-geometry access. The separate EditWithRollbackManager route above tests that access. [Older uf_modl.h](https://www.ugapi.com/doc/UFun/uf_modl/uf_modl.html), [ExtractFaceBuilder, published NX 2022.1 API](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.ExtractFaceBuilder.html).

A report-shaped regression uses separately compiled C to return success with null owners for two edges. It checks all six links, including the empty sketch, absence of an invented 630003 code, and rejection before candidate creation/MCS/toolpaths. All **464 focused checks outside NX** pass, including **92 using C fixtures**. This tests rejection with synthetic data, not reproduction or repair of the NX kernel. The V1.26.02 executable and catalog SHA-256 were unchanged when this analysis was published. The next experiment is V1.27 above; intermediate-support transfer remains unconfirmed.

<a id="reference-ownership-v12602"></a>
## V1.26.02: geometric reference ownership

The V1.26.01 report confirms lookup of the previous revision's ten UF exports, successful nonempty reference/name-array reads, and membership reads for four sketches. Then `Face/Edge.GetBody()` failed with **630003 — Operation forbidden on suppressed object**. The failing face or edge was not recorded: the stale stage named the last inspected `CAM_MAIN`, so that group cannot be blamed. Candidate creation, reflection and history replacement had not started. The report records successful Undo, source restoration, an unchanged checksum and a clear modification flag.

Ownership is now checked first by exact tags and types from the captured `Body.GetFaces()` / `GetEdges()` topology of **all bodies in the copy**. A match needs no repeated `GetBody()` call. Ambiguous ownership or a Face/Edge type mismatch is an error. References absent from the index use `UF_MODL_ask_face_body` / `UF_MODL_ask_edge_body`; these exports join the preflight before body changes. The older published C contract specifies `int (tag_t, tag_t*)` but **does not guarantee access to suppressed geometry in DC 2606**. UF errors, including 630003, and null/unwritten outputs remain failures.

Inspect all readable direct Face/Edge references of retained features. Query each repeated tag once, but list every consumer and role in the failure. Distinguish unknown bodies, owner-read failures, and references to the selected body absent from its final topology. The latter require a separate exact intermediate-support map; a final-body copy does not provide it. Readable references to another known body retain the previous allowance without remapping and remain subject to subsequent exact comparison. V1.26.02 introduced no automatic unsuppression, history-context change or coplanar face/edge substitution; the separate historical-context experiment was added in V1.27. Complete indirect Section/ScCollector definitions and sketch constraints remain unproven.

The stage now identifies the actual consumer, tag and role; the aggregated failure preserves the primary UF error, code and traceback. These ownership problems are reported **before candidate creation**. This repairs ownership reading and diagnostics; **it does not establish reconstruction of missing intermediate supports or a completed CAM copy**. The prototype still requires rollback, with no subsequent MCS/stock transfer or toolpath deletion.

All **463 focused checks outside NX** pass, including **91 using separately compiled C fixtures**. The 16 new regressions cover exact ownership without `GetBody()`, complete failure enumeration, preserved error codes/causes, distinct Face/Edge C exports, unsigned tags and unreadable outputs. They neither reproduce the NX kernel nor prove 2606 compatibility. The actual run and the final-body mapping limitation are analyzed above in the [V1.26.02 report](#result-v12602). [SDK verification checklist](#replacement-probe-sdk-2606).

New C-signature source: [older Siemens uf_modl.h](https://www.ugapi.com/doc/UFun/uf_modl/uf_modl.html). Intermediate-object correspondence and owner-query behavior on suppressed objects require actual NX 2606 checks.

<a id="uf-api-v12601"></a>
## V1.26.01: UF C API replaces unavailable Python methods

The V1.26 report (`MIRROR_TEST_012.prt`, 2026-11-15) confirms an API-selection error: `NXOpen.UF.Modl` has no `AskReferencesOfFeatures`. The prototype selected a body with eight direct retained consumers, then stopped on the first BREP reference read, **before creating, reflecting or replacing the candidate body**. This report does not test history replacement. It records successful Undo/source restoration, an unchanged source checksum and a clear NX modification flag.

The initial snapshot read both Arc objects with `has_tool_position=1 / Tool Position=On` in all four operations: `R1_CHERN_Z-0.97`, `R1_CHERN_Z-1.149`, `R1_CHIST_Z-1`, `R1_CHIST_Z-1.179`. This establishes the initial parameter read, not preservation after transformation. `material_side` values are recorded separately and are not substituted for Tool Position.

V1.26.01 calls the DLL export `UF_MODL_ask_references_of_features` through the existing ctypes adapter. Free the tag array with `UF_free` and the separate string array with `UF_free_string_array`. Preserve order, duplicates and raw name bytes for comparison; diagnostic decoding does not define reference identity. Invalid counts, missing arrays/names and UF errors never become empty selections. An invalid count cannot drive a string-array traversal: only the outer allocation is released; safe cleanup of an unknown number of inner strings is not established.

The other UF operations introduced by the V1.26 prototype also use C contracts: `UF_MODL_compare`, `UF_MODL_compare_topology`, `UF_MODL_delete_object_parms`. The latter receives an actual `uf_list`; verify its single entry is the new independent body's tag and release the list itself, not geometry. Existing independence and external-consumer guards remain mandatory before parameter removal. Check all ten required exports **before changing bodies**. Missing exports stop the experiment; unknown data is never accepted. This does not claim the other Python methods are absent on the user's installation; they are no longer required.

The prototype still requires rollback, with no subsequent MCS/stock transfer or toolpath deletion. **447 focused checks outside NX** pass, including **86 using C fixtures**. New cases cover pointer levels, ownership, errors/partial outputs, comparisons and the candidate list. These do not test DC 2606 DLLs or its kernel. The subsequent run is analyzed above under V1.26.02. SDK 2606, body extraction/replacement and final dependent-sketch preservation remain unconfirmed.

C signature sources: [older Siemens uf_modl.h](https://www.ugapi.com/doc/UFun/uf_modl/uf_modl.html), [uf_modl_utilities.h](https://www.ugapi.com/doc/UFun/uf_modl/uf_modl_utilities.html), [uf.h / UF_free_string_array](https://www.ugapi.com/doc/UFun/uf/uf.html). A published .NET method is not evidence of Python availability.

<a id="replacement-probe-v126"></a>
## V1.26: one-body replacement prototype

**This experiment always rolls back; it is not a completed mirrored CAM project.** If ordinary body history has retained consumers other than protected Tooling Boxes, the journal automatically selects one body with the largest number of these direct dependencies. An isolated replacement experiment substitutes for the common transformation of original bodies. Without such dependencies, the previous full workflow and its guards remain in place.

1. Before changing the model, read feature references through `UF_MODL_ask_references_of_features`, single-body history ownership, external expression consumers, sketch geometry/expression membership and selected CAM objects. The report links each SKETCH feature tag to its Sketch object and curve tags. Intermediate-history references absent from the final body's faces/edges block replacement. V1.27 instead enters the separate access experiment with rollback; no coplanar substitute is selected.
2. `ExtractFaceBuilder` creates a separate non-associative body copy. After independence checks, remove parameters **only from this new body**. Recheck original objects inside the copied PRT after every preparation change.
3. Before reflection, build a complete face/edge bijection using position, underlying surfaces/curves, trimmed topology and face-edge incidence. Missing or ambiguous matches stop processing. Only the new independent body is passed to the raw reflection call.
4. `ReplaceFeatureBuilder` uses the checked explicit map, disabled automatic matching and `MapOnlyObjectsWithModelingDependencies`. After Commit, check original body/face/edge tags, geometric references, retained features and expressions, sketch membership, dependent-curve positions, new warnings, CAM selections, stock settings and MCS reference selections. The API documentation **does not guarantee tag preservation**; changed tags cause rejection.
5. Success and failure both enter the existing copy-only rollback path, restore the source and verify its integrity. This branch does not transfer MCS/stock or delete toolpaths. Even a successful prototype **is not left for saving or CAM regeneration**. If Undo fails, retain the existing attempt to close only this invocation's copy without saving and report the actual outcome.

Curves upstream of the replaced body may remain at their original positions in this limited experiment. Complete sketch constraints, all associative definitions and whole-project transfer remain unproven. The V1.26.02 run found inaccessible intermediate supports before candidate creation; the next run uses the V1.27 route above. The experimental result is not intended for saving. The disk file remains the initial PRT copy.

### Part Boundary: groove centerlines

For `R1_CHERN_Z-0.97`, the user confirmed selecting sketch curves drawn along groove centerlines. The Part Boundaries screenshot shows `Select Curve (1)`, `Boundary Type = Closed`, `Tool Side = Outside`, `Plane = Specify`, and a separate `Tool Position = On` for `Member 1`. Substituting model edges would change machining. The successful body-edge selection in `FASKA_1 / 3 Axis Deburring / Specify Edges` on a test part uses a different definition and does not establish preservation of this Planar Mill boundary on the working PRT.

V1.26 reads application data for the boundary and **each member**, using `UF_CAMBND_ask_boundary_app_data` / `UF_CAMBND_ask_item_app_data`. Compare flags and active stock, tolerance, feed, blank-distance and tool-position values separately; `On` is never inferred from `material_side`. Inactive storage becomes `None` only after reading an explicit zero flag. API errors, unknown codes and unwritten flags stop the trial. Existing order/duplicate-reference, six boundary-field and plane checks remain. This is still not a complete boundary, selection-rule or UDE snapshot; the earlier report's unknown `boundary_type=2` is not guessed to mean Closed.

<a id="replacement-probe-sdk-2606"></a>
### Items requiring SDK and NX 2606 verification

| Methods / definitions | Outstanding verification |
| --- | --- |
| Base `SelectionIntentRule`, `BodyDumbRule.GetData(rule)` | V1.30.02 in real DC 2606: Type matched installed BodyDumb, but the descriptor rejected the base wrapper with TypeError. This route is no longer called. |
| `ScCollector.CopyCollector/RemoveEvaluationFilter/Destroy`, `Part.ScCollectors` iteration | V1.30.04: establish a separate copy preserving rules/settings, new tag/ownership, filter removal affecting only the clone, full selection reading, destruction, repeated checks and no temporary objects after Undo. Real V1.30.03 did not reach AskRulesAndCollectedEntities; its exact SleepyEntity value was not printed. |
| `ScCollector.AskRulesAndCollectedEntities`, `ScEvaluationFiltertype`, `IsEvaluationFilterEnabled` | V1.30.03: verify availability and Python result `(objects, rules, ruleObjectArrayCount)`, segment ordering, no original-body loss through negation/filters, filter enum members and stability after UpdateMap/Destroy/Undo. Uses NX 2312/2512 contracts, not SDK 2606. Commit remains prohibited pending verification of semantics and rule settings. |
| `DisplayUniqueInputsToMap`, `ScCollector.GetObjects/GetRules/GetMultiComponent/GetNonFeatureMode`, `BodyDumbRule.GetData`, `SelectionIntentRule.RuleType.BodyDumb`, `MatchedReferenceBuilder.Types.BodyCollector` | V1.30.01: Python signatures/returns and actual EXTRACT_BODY rule in 2606; complete expansion into individual parents, unchanged definitions before/after UpdateMap, Destroy and Undo. Other rules are not declared supported. |
| `MatchedReferenceBuilder.Type/Types.Curve`, `MatchedStatus/ResolvedStatus.ByUser/BySystem/Initial/Unresolved`, `ReverseDirection`, `ReplaceFeatureBuilder.UpdateMap/SetMatchingForParent`, `FeatureReferences.GetMatchedReferences/Validate`, builder `Validate/Destroy` | V1.30: Python 2606 members, single-edge rows versus Section/collectors, coverage of intermediate supports, acceptance of standalone Arc targets, direction and status semantics, no-Commit cleanup and actual later replacement/constraints. Do not set type/status just to pass; Validate is not proof of transfer. |
| `Part.Curves`, `CreateLine(Point3d, Point3d)`, `CreateArc(Point3d, Vector3d, Vector3d, radius, startAngle, endAngle)`, `CreateEllipse(Point3d, Vector3d, Vector3d, major, minor, startAngle, endAngle)` | V1.29: Python overloads in DC 2606, collection coverage, ABS directions, angle/trim semantics and actual periodicity, independent new tags and Undo removal. V1.29 confirmed CreateArc, native readback and Undo for two arcs on this PRT; the other positive creation paths remain untested. |
| `UF_CURVE_create_spline(UF_CURVE_spline_t*, tag_t*, int*, UF_CURVE_state_t**)`, `UF_free` | V1.29: Windows C ABI, int rational flag, double-pointer states output, pole[4]/knot ownership, trim/periodicity and normalized domain. Every returned state is rejected; real kernel behavior and readback remain unverified. |
| Sketch/support association definitions, complete constraints and Edge → Curve compatibility | V1.30.01 only prepares a proposed map without Commit; actual transfer is not implemented in this branch. Native curve creation does not establish a valid replacement reference; never silently convert the definition or substitute a geometrically similar object. |
| `UF_EVAL_initialize_2`, `UF_EVAL_is_line/is_arc/is_ellipse/is_spline/is_periodic`, `UF_EVAL_ask_limits/ask_arc/ask_ellipse/ask_spline/ask_spline_control_pts/ask_spline_knots/evaluate/free`, `UF_free`; `UF_EVAL_arc_t/ellipse_t/spline_t` | V1.28 read two Arc supports on this PRT; Line/Ellipse/Spline and all variants remain untested. Verify: Windows 2606 exports/C ABI, struct/logical sizes and alignment, exact procedural-edge representations, ABS/parameterization/trim/periodicity, weights/knots, allocation ownership and release. Older contracts and C fixtures do not establish compatibility. |
| `StartEditWithRollbackManager(feature, mark)`, `EditWithRollbackManager.UpdateFeature/Stop/Destroy` | V1.27.02 passed Start/UpdateFeature/Stop/Destroy and checks after local Undo in three external sketch contexts on this PRT. Other consumer types and definition methods remain unverified. |
| `Feature.Timestamp/Suppressed/IsInternal`, `UF_MODL_ask_current_feature` | Property types, eligible external consumers, current-feature values inside/after the manager, and complete history-state preservation. |
| `UF_MODL_ask_bounding_box`, `Edge.GetVertices` | C ABI, reading intermediate/suppressed objects in the open context, ABS coordinates. Approximate bounds/endpoints do not define complete geometry. |
| `Part.Expressions`, `Expression.Name/Type/RightHandSide/Units`, `Session.MarkVisibility.Visible`, `Session.SetUndoMark/UndoToMark/DeleteUndoMark` | Error 690037 confirms the visible-mark requirement; V1.27.02 subsequently opened and completed three contexts successfully. Complete expression definitions, restoration after each transition, local mark interaction with the manager and outer rollback. |
| `UF_MODL_ask_alive_edge`; `ExtractFaceBuilder.FixAtCurrentTimestamp/FrecAtTimeStamp`; `UF_MODL_set_current_feature`, `Feature.MakeCurrentFeature` | Research options, not executable paths. Null ownership does not imply merged labels; changing the insertion point does not prove historical-geometry access. |
| `Body.GetFaces/GetEdges`, `UF_MODL_ask_face_body`, `UF_MODL_ask_edge_body` | Completeness of current topology and C owner-query behavior for intermediate/suppressed references. An exact indexed tag replaces a redundant GetBody; an absent tag requires separate evidence, not substituted geometry. |
| `UF_MODL_ask_references_of_features`, `UF_free`, `UF_free_string_array` | Windows 2606 ABI, complete intermediate-history references, part ownership and names/allocator ownership. The missing Python method is no longer required. |
| `SketchFeature.Sketch`, `Feature.GetEntities`, `Sketch.GetAllGeometry/GetAllExpressions` | Actual classes/results, associative geometry and complete dependencies; this snapshot does not establish every sketch constraint. |
| `CreateExtractFaceBuilder(None)`, `CreateRuleBodyDumb`, `ExtractBodyCollector.ReplaceRules`, `CommitFeature` | Non-associative extraction without changing originals, independent history, solid/sheet support. |
| `UF_MODL_delete_object_parms`, `UF_MODL_create_list/put_list_item/ask_list_count/ask_list_item/delete_list` | C signatures and opaque list ownership; only the new independent body may lose parameters. Confirm the resulting single BREP in NX. |
| `UF_MODL_compare`, `UF_MODL_compare_topology`, `UFConstants.COMPARE_*` | Signatures, return values, units and accuracy; final face/edge correspondence must be unique. |
| `CreateReplaceFeatureBuilder()`, `SelectFeature/ReplacementFeature.GetArray`, `UpdateMap`, `FeatureReferences.GetMatchedReferences`, `SetMatchingForParent`, `MatchedReferenceBuilder.ReverseDirection`, `Validate/Commit/Destroy` | Complete mapping, original geometry tags/selections, sketch constraints/dependencies and MCS references, behavior of `CopyReplacementFeature=True`. |
| `UF_CAMBND_app_data_t`, both `ask_*_app_data`, `UF_CAM_tool_position_t` | Windows 2606 ABI, flags and applicability to operation families/channels. The older contract has On=1 and Tanto=2; correspondence to this UI requires an actual read. |

Sources are older published Siemens contracts: [ExtractFaceBuilder, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.ExtractFaceBuilder.html), [ReplaceFeatureBuilder](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.ReplaceFeatureBuilder.html), [UFModl](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.UF.UFModl.html), [uf_cambnd.h](https://www.ugapi.com/doc/UFun/uf_cam/uf_cambnd.html), [uf_cam.h](https://www.ugapi.com/doc/UFun/uf_cam/uf_cam.html). These are not SDK 2606. **427 focused checks outside NX**, including **67 using separately compiled C fixtures**, exercise the code and rejection scenarios. Real NX prototype execution, full-copy save/reopen and toolpath regeneration remain unconfirmed.

## V1.25 result in NX

The **V1.25.02** report dated 2026-11-15 (`MIRROR_TEST_011.prt`) confirms that target-selection changes **did not fix the failure**. Exactly **three bodies and 23 points** were passed to `UF_MODL_transform_entities`. Every point returned no modeling parents; both selected sketch arcs were excluded from direct input. The common call still damaged the same eight retained features. The new parent query successfully exercised its no-parents branch for these points; returning and freeing a nonempty parent array in actual NX was not tested by this report.

The preceding **V1.25.01** report (`MIRROR_TEST_010.prt`) showed the same failure. Release .01 corrected update detection and .02 addressed a target-selection gap; neither made this PRT work. This is not rejection of an unknown operation or modeling-command type. The individual effects of the 26 inputs have not been isolated: the common call does not prove that one body alone caused the failure or that all points are harmless.

An added regression uses these target categories/counts with the existing history-damage scenario: **395 focused checks outside NX**, including the previous 61 using C fixtures. It verifies rejection before MCS/stock transfer, toolpath deletion and subsequent CAM writes. The scenario uses synthetic objects and does not reproduce the NX kernel. Journal and catalog were unchanged for that analysis. V1.26 now adds the isolated prototype described above. Dependency transfer with exact geometric references still needs implementation; another command allowlist extension or permission to lose dependencies would not solve it.

**The user PRT still fails.** In the 15 November 2026 report, both selected `Arc` objects were recognized as members of one sketch and excluded from separate transformation. The common `UF_MODL_transform_entities` call nevertheless changed eight retained features: four Datum CSYS parent references, three sketch definitions and one CPROJ definition. Two sketches lost expressions; other parent dependencies were removed. New warnings include lost point parents, under-constrained sketches and failed associative curves. One sketch acquired a warning without a structural snapshot change.

The early history guard stopped execution before MCS/stock transfer and toolpath deletion. The report records successful Undo, return to the source, an unchanged source-file checksum and a clear source modification flag. The final CAM-boundary comparison was not reached, so this run does not establish whether the four boundaries lost in V1.24 were retained or lost again. It confirms damage during the common model transformation; the responsible individual target within that call has not been isolated.

Merely accepting the four Datum CSYS parent remaps would leave three damaged sketches and a damaged projection. These changes are outside the authorized body-history-to-BREP conversion. The current transfer mechanism remains unsuitable for this dependency graph. A replacement must preserve geometry identifiers, selected objects and sketch dependencies; full successful mirroring has not been established.

Three additional regressions reproduce this failure pattern with synthetic objects, including both excluded arcs and the warning-only sketch. All **371 focused checks outside NX** pass, including 53 using test C libraries. These are failure-guard checks, not reproduction or repair of the NX kernel. The initial analysis was published without changing the journal. **V1.25.01** subsequently updates the version and checksum for updater detection; the algorithm and reported failure remain unchanged.

## Investigated transformation alternatives

This investigation uses available older NXOpen/UF contracts, **not the 2606 SDK**. The analysis below predates V1.26; only the isolated builder experiment above has now been added:

| API / approach | Established behavior | Why it is not a ready fix |
| --- | --- | --- |
| `UF_MODL_transform_entities` | Transforms input objects and their parents. In the V1.25 report, retained history is damaged even with both arcs excluded. | A different dependency-transfer mechanism is needed; weakening checks does not repair it. |
| `UF_MODL_replace_body_data` | Replaces an unparameterized feature's body with a copy of another body. Both must be single-face sheet bodies. | It is not a general BREP editor for a solid part. |
| `UF_MODL_replace_features` | Provides an edge/face correspondence map and reparents child features. With `return_map=true`, it returns downstream references needing mapping without performing replacement. | This map does not promise CAM coverage. The description also does not guarantee preservation of body/face/edge tags. |
| `ReplaceFeatureBuilder` | Provides explicit reference matching. The older reference marks `MapAllObjects` as unsupported. | Modeling dependencies cannot be treated as a complete map of selected CAM geometry. Verification against 2606 is required. |
| `MirrorBodyBuilder`, `UF_BREP_make_body` | Create a mirrored/new body. | Creation alone does not preserve existing identifiers or sketch references. |

The V1.26 prototype follows **explicit dependency capture and matching before changing the body**. First, obtain every used reference, including intermediate-history entities, and match each to its exact reflected counterpart. A reference absent from the final body cannot be assumed unnecessary; another coplanar face is not an equivalent selection, and ambiguous matches must not be chosen silently. Then verify preservation of identifiers and sketch/projection definitions through history replacement. Only after those checks may the existing MCS, stock and CAM stages proceed. Reparenting one reference to a BREP without checking the rest of the sketch definition is insufficient.

Open requirements before full-project use include the actual DC 2606 API behavior, identifiers after replacement, access to intermediate references, and complete transfer of sketch constraints/associative geometry. The experimental call is guarded by explicit mapping, post-change checks and mandatory rollback; it does not establish a production transfer. There is no ready fix established by the available offline checks; further development does not require the user to provide a manual mirroring method.

Sources: [older Siemens `uf_modl.h` header](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_modl.h), [ReplaceFeatureBuilder](https://www.ugapi.com/doc/nxopen/a03024.html), [MirrorBodyBuilder, NX 2022.1](https://nxopencsdocumentation.thescriptingengineer.com/NX2022_1/NXOpen.Features.MirrorBodyBuilder.html). These are published copies of contracts, not proof of kernel behavior or 2606 compatibility.

## Latest release

Installation requires updater **V1.20**. If yours is older, update and restart it; then check the catalog and install **V1.30.04**. [Updater upgrade](updater.md#upgrading-to-v120).

**V1.30.04 · 2026-11-16** — Check filter effects on a separate temporary ScCollector while preserving original settings and verifying cleanup/rollback. No Commit added. [Result and next run](#filter-copy-v13004).

**V1.30.03 · 2026-11-16** — Read per-rule BodyDumb objects via AskRulesAndCollectedEntities instead of the rejected base-wrapper call; strict partition/filter checks and still no Commit. [Report and next trial](#rule-members-v13003).

**V1.30.02 · 2026-11-16** — Read BodyDumb by NX rule type when GetRules returns a base SelectionIntentRule wrapper; explicit GetData invocation requires Python DC 2606 verification. [V1.30.01 result and next trial](#rule-reader-v13002).

**V1.30.01 · 2026-11-16** — Checked BodyDumb-collector expansion into a unique-parent map without Commit; retain original rules. [V1.30 result and next trial](#collector-map-v13001).

**V1.30 · 2026-11-16** — Prepare a reflected BREP and complete available map including historical curves; validate without replacement Commit, then local/outer Undo. V1.29 established two native Arc candidates. [Result and next stage](#historical-map-v130).

**V1.29 · 2026-11-16** — Create standalone reflected Curves after history restoration, read them back and perform mandatory local/outer Undo. Sketch/CAM references are not transferred yet. V1.28 established two Arc definitions. [Result and next stage](#native-curves-v129).

**V1.28 · 2026-11-16** — Read Line/Arc/Ellipse/Spline through UF_EVAL_initialize_2 and check reflection in memory; mandatory rollback. V1.27.02 confirmed two-edge access across three NX contexts. [Definitions and result](#curve-definitions-v128).

**V1.27.02 · 2026-11-16** — Fix error 690037: each EditWithRollbackManager receives its own visible Undo mark. Preserve restoration checks and outer rollback. [Report and correction](#visible-undo-v12702).

**V1.27.01 · 2026-11-16** — Fix premature reflected-support validation before history access. Original/reflected states are checked at their applicable stages; exact selections and rollback remain protected. [Report and correction](#mcs-phase-v12701).

**V1.27 · 2026-11-16** — Separate EditWithRollbackManager access experiment for intermediate supports, with verified local restoration and mandatory outer rollback. Complete geometry and reflected correspondence are not yet built. [Sequence and limits](#historical-probe-v127).

**V1.26.02 · 2026-11-16** — Resolve reference ownership by exact final-topology tags without redundant GetBody calls. Other faces/edges use UF owner queries; report all failures with consumers. Do not substitute intermediate supports; preserve guards and mandatory rollback. [Error 630003 and correction limits](#reference-ownership-v12602).

**V1.26.01 · 2026-11-15** — Replace the unavailable Python UF reference reader with its C export. Preflight all UF prototype exports; use C comparison and candidate-list parameter removal as well. Guards and mandatory rollback remain. [Report and correction](#uf-api-v12601).

**V1.26 · 2026-11-15** — Add an isolated one-body history replacement prototype with an explicit reference map and mandatory rollback. Independently check boundary/member application data, including Tool Position On. This is an experimental replacement of the failing stage, not a confirmed fix for the working PRT. [Prototype workflow and limits](#replacement-probe-v126).

**V1.25.02 · 2026-11-15** — Address curve/point selection outside sketches and UF Smart Objects: query modeling parents through `UF_MODL_ask_entity_parents` before separate transformation. Exclude dependent geometry from direct input while retaining position, parent and CAM-reference checks. Selected Smart curves/points now also require reflected-position verification. Report the exact direct transformation targets. All **394 focused checks outside NX** pass, including 61 using test C libraries; 23 scenarios were added. This is a limited target-selection correction, not a confirmed fix for the history damage reported in V1.25.01.

### V1.25.01

**V1.25.01 · 2026-11-15** — Release V1.25.01 so the updater recognizes an update from V1.25. Align the journal header and version constant, catalog and SHA-256. The mirroring algorithm is unchanged; the known sketch/projection dependency damage reported for V1.25 is not fixed. Small publications within the same main revision use .01, .02 and subsequent suffixes; the working filename remains NX_Mirror_Project.py.

### V1.25

**V1.25 · 2026-11-15** — Fix reflection target selection: query curve/point sketch membership independently of UF Smart Object status. Exclude sketch geometry from separate transformation, then verify its reflected bounds, original sketch membership and CAM references. Edges move with their bodies. Add an early stop for damaged retained history and checks for new NX warnings. All 368 focused checks outside NX passed, including 53 using test C libraries and 40 new scenarios. The fix remains unverified on the user PRT; safe transfer of every dependent sketch is not claimed. [Details](#sketch-curves-and-nx-warnings).

### V1.24

**V1.24 · 2026-11-15** — Diagnostic revision after V1.23 stopped while comparing CAM references. Replace the generic error and last-read operation label with the actual changed node, channel, field and before/after values. Keep the existing exact snapshot comparisons; add no tolerance, normalization or skipped differences. All 328 focused checks outside NX passed, including 50 using test C libraries and 21 new diagnostic scenarios. The cause of the user PRT’s CAM-data change remains unknown; a V1.24 report is needed. [Diagnostic details](#cam-difference-diagnostics).

### V1.23

**V1.23 · 2026-11-15** — Add transfer for a recognized associative plane–X/Y direction–point CSYS after V1.22 stopped at MCS “2”. Retain the existing CSYS, Plane/Direction/Point objects and their selections; set orientation flags on a new Xform. Verify the candidate before replacement and the installed definition after replacement, model update and CAM reads. Keep Y′ = R(Y), Z′ = R(Z), X′ = Y′ × Z′. All 307 focused checks outside NX passed, including 50 using test C libraries; 22 new scenarios exercise the definition and protection failures. SDK 2606 compatibility and a complete NX result remain unconfirmed. [Definition and limits](#plane-direction-point-mcs).

### V1.22

**V1.22 · 2026-11-15** — Fix V1.21's false stop on an unused custom-point depth field. For Planar/Cavity, check upper_depth/lower_depth for Cut Region Start and depth for Predrill Engage; Face and valid Surface Contour points use no depth fields. Unused values are neither loaded as numbers nor compared as settings. Active nonzero depths, however small, still stop the trial. All 285 checks outside NX passed, including 50 using test C libraries. [Field applicability table](#custom-cam-points).

### V1.21

**V1.21 · 2026-11-15** — Add limited custom CAM point transfer after V1.20 stopped on a PlanarMilling Cut Region Start point. Point references retain their selection; numerical points reflect in ABS while preserving order. Check coordinates after model updates and final CAM editor reads. Nonzero depths and unverified cases stop the trial with recorded data. All 275 checks outside NX passed, including 43 using test C libraries. [Supported cases and limits](#custom-cam-points).

### V1.20

**V1.20 · 2026-11-15** — Remove both model-feature name catalogs after V1.19 stopped on `DELETE FACE`. All history features undergo common validation. Body history may become BREP based on `NXOpen.Features.BodyFeature` membership, single-body ownership, retained topology and expression dependencies. Modeling command names no longer determine admission. Retain Tooling Box, MCS, sketch and CAM protection. All 244 checks outside NX passed, including 16 new tests. This does not establish that every modeling command works in DC 2606; [methods and definitions needing SDK verification](mirror-operations.md) are listed separately.

### V1.19

**V1.19 · 2026-11-15** — Check Builder classes for specialized factories as well, including CavityMilling. Changed editors, lost fields and read errors stop the trial. Include operations missing from the Geometry traversal. Distinguish editor recognition, accessible geometry reads and unverified definitions in the report. Flag complete hole/FBM data regardless of dispatch route. Preserve NX/API codes, tracebacks and source-integrity diagnostics on failure. All 228 checks outside NX passed; retain the 31-Builder catalog. [Family coverage, sources and separate SDK 2606 verification list](mirror-operations.md).

### V1.18

**V1.18 · 15.11.2026** — Expand NXOpen milling and hole-making families. Use specialized editors for known classes, otherwise OperationCollection.CreateBuilder with an explicit concrete-editor catalog. Do not automatically accept turning, WEDM, additive, inspection or user-defined motion. Check available geometry, Validate and custom points; report complete hole/FBM definitions separately as unverified. Retain the V1.17 axis convention. All 210 checks outside NX passed; actual projects still require verification and regenerated toolpaths.

### V1.17

**V1.17 · 2026-11-14** — change MCS/RCS orientation after the NX test: V1.16 retained selected faces but introduced an additional Y reversal. Y and Z now follow the geometric reflection, and X completes a right-handed frame: `Y′ = R(Y)`, `Z′ = R(Z)`, `X′ = Y′ × Z′ = −R(X)`.

For reflection across X=0 and setups aligned with ABS, the original X/Y/Z directions remain unchanged, including a lower setup with negative Y and Z. Reflect the origin: for example, `(60, −25, 16)` becomes `(−60, −25, 16)`. For an oblique plane, Y and Z change according to that plane's reflection. Relative to V1.16, both transverse axes X/Y reverse while Z keeps its direction.

Apply the same rule to Three Planes, numerical CSYS/Xform, the root MCS and final verification. Retain Three Planes faces and construction methods, including V1.16's checks of active objects after old Plane cleanup. The report shows the transfer rule and original/verified final ABS axes. Boundary planes keep their separate material-side transformation rule.

An existing Plane now uses `CopyPlane()` to copy its definition with the same construction method and directly selected geometry. No additional new-plane-to-old-plane link is introduced. When the original input is directly a Face or DatumPlane, one coincident plane references that object directly. Intentionally selected intermediate planes are not flattened to their underlying faces.

Freeze the method and ordered direct selections before reflection. Check them during creation and orientation, after installing the definition, after model update and at final verification. Missing selections, a different face on the same geometric plane, or a changed construction method fail the trial even when MCS coordinates match. The report lists direct X, Y and Z selections separately. A copied definition must be a separate object in the same part; the original Plane is not used as a mutable copy.

Stock/MCS dependency ordering remains: evaluate original linked supports from parents to children and verify their placement before creating the CSYS. Dependency cycles stop before reflection.

If Undo fails, attempt to restore the original project and close without saving only the copy created by this invocation. Verify its path, separate identity, absence of components and actual closure. Use CloseRetainUndoMarks to retain other undo marks. If safe closure is unavailable or unconfirmed, report that the copy must be closed manually. Do not report failed Undo as successful rollback; no files are deleted.

Geometry and CAM mode is retained: a model body's history in the copy may become one non-parametric BREP. This requires verified ownership by one body and unchanged body, face and edge identifiers. Parameters with users outside the replaced history may not be removed. A protected Tooling Box may only remap parents to the same body's BREP while retaining its type and expression membership. Other history changes remain failures.

Tooling Box solids are excluded from the general transformation. When needed, edit the existing parametric stock feature and verify its type, offsets and references. Before and after reflection, create API editors for Workpiece, MCS and supported operations, reload available geometry and run Validate. Compare CAM modes and flags exactly; lengths use an absolute tolerance of 1e-8 part units. An error fails the trial and triggers rollback.

MCS transfer retains the existing CSYS. Recognized Three Planes definitions retain the original plane methods and direct selections. Direction correction uses copies of existing Plane definitions; direct Face/DatumPlane inputs use coincident planes. Y/Z are reflected and X completes a right-handed frame. Unknown associative definitions are not replaced by Dynamic. Verify position, axes, inheritance, the dependency graph and direct selections separately. Implicit NX defaults still receive explicit bindings in the copy.

**This is a trial release. V1.30.04 has been checked outside NX. The user confirmed V1.16's face selections but reported an unwanted Y reversal. The new orientation, saving/reopening and toolpath generation require an NX / Designcenter 2606 test. Run V1.30.04 from a healthy original project. The experimental branches always roll back. The copy's body history may become BREP; the script does not save the source part. Other numerical directions and coordinates inside operation settings remain unchanged.**

## Workflow

1. Open a simple test part without components. It must be fully loaded, be both work and displayed part, and have a normal `.prt` file on disk.
2. Save it with the normal NX command. The journal stops if the source has unsaved changes; it does not save the source itself.
3. Use **Tools → Journal → Play** to run the script. Updater entry: **«Зеркальная копия CAM-проекта — тест» V1.30.04**.
4. In the geometry picker, click one **planar face** or **datum plane**, then click **OK**. No name or coordinate entry is required. Oblique planes are supported. The surface defines the plane, not the cursor position; reversing its normal does not change the reflection. Curved surfaces and objects from another component are rejected. Cancelling selection exits without creating a copy.
5. After selecting the plane, the first unused sibling filename such as `name_MIRROR_TEST_001.prt` is created. Existing files and names of parts still open in NX are not reused. To retest the original model, activate it first: the journal copies the work part at launch, even if that is a previous test copy. Collapsing the suffix changes only the output name, not the source data.
6. **If the historical-access or one-body experiment ran, it must roll back; do not save the experimental result.** The following inspection/save steps apply only to a successful full-project branch. Read the report; its text is available through **«Копировать всё» (Copy all)**. If checks pass, inspect bodies, MCS frames and the geometry of every operation. In Three Planes, check the original face choices for each X/Y/Z field, including opening the corresponding plane definition; the Three Planes type and correct coordinates alone are insufficient. Check Y and Z directions in both setups against the model and the report's ABS-axis lines. Then save the open test copy using the normal NX command and regenerate operations.

**Until manually saved, the reflected result exists only in the open NX session. The file on disk remains the initial copy of the source PRT.**

## Behavior

The full-project branch is described below. The V1.27 historical-access experiment and one-body prototype finish earlier and always roll back; see [historical access](#historical-probe-v127) and [one-body replacement](#replacement-probe-v126).

- Copies the saved PRT byte for byte, verifies checksums and opens a separate part. Source CAM data is not processed.
- Reads operations, MCS/RCS and Part, Blank, Check, Trim, Cut Area, Wall and Drive geometry references in the copy, including collector members and supported boundaries.
- Reflects ordinary bodies and supported referenced geometry through a model-parent transformation. Parametric stock is updated through its existing Tooling Box. Body identifiers and all selected references must remain stable. All faces/edges are checked on ordinary bodies; the body and explicitly selected faces/edges are checked on regenerated stock. Unselected internal stock topology may be regenerated.
- Transfers existing MCS/RCS while retaining supported Three Planes and numerical-Xform CSYS definitions and verifying associativity. Y and Z are reflected; X completes a right-handed frame. Only implicit NX defaults receive new numerical bindings. Resolve inheritance providers through UF; do not overwrite inherited nodes. Verify coordinates, parameter status/provider and the available RCS-to-MCS link flag.
- Transfers user-defined boundary planes without recreating contours. Plane X/Y axes are reflected and Z is reconstructed as the negative of that plane's reflected original Z. MCS Z is transferred separately. The geometric plane and material side should follow the reflection. Parameters and references are checked after update. The copy's WCS is restored to its original position.
- Removes calculated toolpaths in the copy. Does not create mirrored CAM operations, generate paths or postprocess.
- Checks exact oriented object bounds in the selected-plane frame, geometry identifiers, available selected CAM references, operation membership and geometry parents, missing toolpaths and transferred MCS/RCS frames. Finally checks the source file checksum and modification flag.
- On an exception after the Undo mark is created, attempts to undo changes in the copy and reports the stage, error and rollback outcome. After successful rollback, restore the source as work and displayed part. If Undo fails, attempt to restore the original and close only this invocation's copy without saving while retaining other undo marks. If closure is unavailable or unconfirmed, ask the user to close the copy manually. Restoring the source does not delete files or close other parts.

## Plane-direction-point MCS

V1.23 recognizes the direct `CSYS → Xform → Plane/Face/DatumPlane + Direction + Point + Scalar` structure. Require exactly one numerical Scalar equal to 1 and unit Xform scale. The original projected direction must unambiguously match the original CSYS X or Y axis, the plane normal must match Z up to sign, and the projected point must match the origin. Also check the original Xform origin and axes. This adapter does not authorize ambiguous, additional or parameterized inputs.

Update supports in dependency order, including stock, before rebuilding. Check the reflected plane, full direction vector and point coordinates. Build a candidate CSYS through `CreateXformByPlaneXDirPoint` or `CreateXformByPlaneYDirPoint` using **the same** Plane/Direction/Point. Do not reverse shared directions or planes; change only the new Xform's axis flags. Retain the Plane method and ordered direct selection, Direction/Point graphs and selected faces/edges. A different coplanar selected face is rejected.

The candidate must confirm the expected origin and all three axes before `ReplaceParameters`. Retain the existing CSYS tag and CAM inheritance after replacement; check its installed Xform, direct supports, ownership by the copy and numerical scale. Repeat checks after model update and final CAM reads. Late drift, missing supports or changed selections stop the trial. Old Xform/Scalar objects may be retired normally; Plane/Direction/Point must remain live. There is no Dynamic fallback.

This is a limited adapter for a recognized structure, not every CSYS definition. A numerical or other direction that does not follow reflection cannot silently count as transferred. An available factory and passing API-model tests do not establish its Python DC 2606 behavior. [Methods requiring SDK verification](mirror-operations.md#items-to-verify-against-sdk-dc-2606).

## Custom CAM points

For `PlanarMilling`, `CavityMilling`, `FaceMilling` and `SurfaceContour`, read point references, ABS coordinates and applicable depth fields. Retain a selected `Point` and include it in checked geometry; transform a shared point once. Associative points must follow their defining geometry. Reflect numerical points without references in ABS by replacing the same channel's list, preserving order and other entries' references. Do not reflect coordinates twice if NX already moved them; unexpected motion is an error.

Determine applicable fields from the operation class and channel using the available older `uf_camgeom.h`, rather than the numerical magnitude:

| Operation class | Channel | Used depth fields |
| --- | --- | --- |
| PlanarMilling / CavityMilling | 0 — Predrill Engage | `depth` |
| PlanarMilling / CavityMilling | 1 — Cut Region Start | `upper_depth`, `lower_depth` |
| FaceMilling | 0 and 1 | None |
| SurfaceContour | 1 — Cut Region Start | None |

Do not interpret other fields in these records as numbers or include them in the settings snapshot; the report marks them `<не используется>` (unused). Unknown applicability is marked `<не проверено>` (unverified) and does not authorize transfer of a detected nonempty list. Unused storage may contain nonzero values or NaN; this is no reason to round active parameters. For writing, initialize a new C structure and assign only applicable fields; do not zero meaningful depths. Unknown families and unverified combinations are not automatically admitted.

Active Planar/Cavity fields still require exact zero; even a nonzero value around `1e-310` stops the trial. Missing or nonfinite active values also fail. The plane must additionally be parallel to ABS Z: zero is not assumed to disable a depth constraint. Face and valid Surface Contour points also support oblique planes. The meaning/reference frame of nonzero depths in SDK 2606 remains unverified.

After transfer, verify record count/order, original references/active depths and each point's expected coordinates. Repeat verification after final CAM editor reads; do not silently repair late motion. Write failures, including append failure after list deletion, propagate to the existing copy rollback. Channels unavailable with code 1345005 remain unverified rather than empty.

## Model-feature handling

The `Part.Features` traversal covers every type, including `DELETE FACE`, without an allowlist of `FeatureType` names. Read each feature's errors, parents and expressions before reflection; detected external references stop the trial. Compare the entire history after transformation, including features outside body histories.

For a permitted BREP conversion, original features must be `NXOpen.Features.BodyFeature` instances, occur in `Body.GetFeatures()`, and belong exclusively to the checked body according to `GetBodies()`. Individual command names are not used for admission. Freeze expression inventories before reflection; ownership or parameter-consumer read errors are not empty data. The existing NX transformation must retain the same body, faces and edges and leave one new BREP without parents or expressions. The journal does not invoke face-deletion editors or separately remove history.

Other features remain monitored and must retain their checked definitions. Sketches, datum/MCS features, groups and protected Tooling Box features receive no permission to disappear as body history. Partial replacement, shared ownership across bodies and parameter consumers outside the replaced history still stop the trial. Tooling Box retains only the existing permission to remap its parents exactly to that same body's BREP.

This is a general validation mechanism, not confirmed support for every NX command. SDK 2606 must still verify the Python `BodyFeature` class, concrete classes returned by `Body.GetFeatures()`, and completeness of `GetBodies()`, `GetParents()`, `GetExpressions()`, `GetUsingFeatures()` and `GetReferencingExpressions()`, including boolean, multi-body, suppressed and user-defined features. Error reporting through `GetFeatureErrorMessages()` and history replacement by `UF_MODL_transform_entities` also need verification in real NX.

## First-trial limits

- A model body's history in the copy may be replaced with one BREP based on common BodyFeature class, ownership and dependency checks, without a catalog of command names. Shared ownership by multiple bodies, partial replacement or external users of a removed parameter stop the trial. Stock, sketch and MCS definitions are not covered by this permission.
- Remaining history validation checks feature identities/types, parents and expression identifiers. Formulas are diagnostic; changing a coordinate formula under the same identifier is not treated as parameter loss. This is not complete validation of all CAD values and dependencies. Replacing expression identifiers outside the permitted body history is rejected even when formulas match.
- Three Planes rebuilding supports recognized mutually perpendicular support planes with unit scale. A CSYS backed by one numerical Xform is also supported with unit scale and matching initial origin/axes. An unconfirmed definition or damaged support stops the trial. Other associative CSYS definitions are accepted only if native updates retain their definition and produce the correct reflected frame. A limited [Plane / Direction / Point adapter](#plane-direction-point-mcs) is also provided.
- Some parametric stock cannot reach the reflected position without changing its settings. If original orientation, asymmetric offsets or dependencies prevent exact reflection, verification stops the trial. The journal does not switch stock to From Geometry or zero its offsets.
- All model features undergo validation without a name filter. This does not guarantee that every feature reflects successfully: an unknown class receives no permission to lose its history; unavailable data and detected external parents stop the trial.
- Components and non-associative boundaries without geometry references remain unsupported. Custom points have [separate limits](#custom-cam-points); other numerical spatial operation parameters still remain unreflected.
- Other numerical spatial operation parameters are not reflected. The report lists detected local nonzero 3D parameters for inspection; this is not a complete inventory of spatial settings. The journal does not edit feeds, allowances or strategies.
- Preserving references and bounds does not prove machining correctness. Selected regions, directions, engage/retract moves and regenerated paths must be checked before use on production projects.
- Requires Windows and the installed NX UF libraries. Geometry channels unavailable with code 1345005 are listed as unverified. Their contents are not confirmed; successful checks on available references do not establish full coverage of all regions. Other API errors or a failed transformation stop the trial.

## Sketch curves and NX warnings

### Modeling parents of curves and points — V1.25.02

Absence of sketch membership and UF Smart Object status is insufficient to authorize a separate transformation. Such curves/points now use `UF_MODL_ask_entity_parents`. Objects with returned parents are expected to follow model updates and are excluded from direct input to `UF_MODL_transform_entities`. An unavailable function, API error or invalid result stops the trial; it never means no parents. Modeling-command names do not control this decision.

Every selected curve/point, including Smart Objects, remains in reflected-bounds verification. For objects read by the new query, parent tags are checked again after final CAM/MCS reads; repetitions matter, while array order is not treated as an axis selection. Lost/replaced parents and newly acquired parents on previously independent objects fail. Parents are not automatically remapped to a new BREP. Existing exact CAM-object and ordered MCS-selection checks remain.

Immediately before the common transformation, the report lists the exact input tags and NXOpen classes. These are call inputs, not a complete list of affected modeling parents. Without them, the previous report cannot isolate the object responsible for the history damage. Excluding a dependent curve does not establish that transforming its body is safe: the early stop for the eight changes reported in V1.25/V1.25.01 remains. Geometry that fails to move through the model also fails verification.

**Items requiring the SDK and actual DC 2606:**

| Method / definition | Required verification |
| --- | --- |
| `UF_MODL_ask_entity_parents` in `uf_trns.h` | Export, 32-bit `tag_t`, one-byte `logical`, `int` count and `tag_t*` array. Meaning and completeness of parents for projected curves, points and intermediate-feature geometry. |
| `found_parent` / `UF_free` | The available contract only defines the count/array and transfers ownership when `found_parent=true`. On successful false, neither is read or freed. The C fixture tests this contract implementation, not the installed NX ABI. |
| `UF_SO_is_so`, `UF_SKET_ask_sketch_of_geom` | Combined coverage of this PRT's geometry; absence of one dependency kind does not exclude another. |
| `UF_MODL_transform_entities`, dependency update | Transformation of remaining direct targets while retaining history, warnings, CAM boundaries and reflected positions of every excluded curve/point. The new query does not reconstruct sketch definitions. |

New-query source: [copy of Siemens `uf_trns.h`](https://github.com/feifeiwang/NXOPEN/blob/main/%E5%AE%98%E6%96%B9%E6%96%87%E6%A1%A3/%E7%A8%8B%E5%BA%8F%E4%B8%8E%E5%AE%98%E6%96%B9%E7%A4%BA%E4%BE%8B/uf_trns.h). This is an older contract; the development environment lacks SDK 2606. No `RemoveParametersBuilder`, sketch-associativity break or replacement of selected curves with new ones is introduced.

### Retained V1.25 checks

The V1.24 report showed lost Part boundaries in four PlanarMilling operations. The user confirmed sketch-curve selections; model-edge selections remain an intended case too. History snapshots immediately after reflection showed lost sketch parents and expressions, and the NX update report warned about lost point parents and an under-constrained sketch. These are unacceptable retained-history changes. V1.25 confirmed exclusion of both selected arcs, but this was insufficient: the common transformation still damaged dependencies. The individual input object responsible for that damage has not been isolated.

`UF_SKET_ask_sketch_of_geom` is queried for selected curves and points before the general transform. A successful `NULL_TAG` means no sketch; an unavailable function or API error stops the trial. `UF_SO_is_so` alone does not establish independence. A sketch curve/point is expected to follow model updates, retains its tag and remains in reflected-bounds verification. Its original sketch membership is checked after final CAM/MCS reads. Unmoved geometry, lost membership or transfer to another sketch fails. Edges are excluded from separate targets; existing body/face/edge and exact CAM-selection checks remain.

Immediately after reflection, check original features outside the one-body history-conversion candidates. A lost feature or changed type, parents or expression identifiers stops work before MCS/stock transfer and toolpath deletion. This uses ownership and class, without modeling-command name lists. Protected stock completes its existing update and validation stages. The early check does not authorize BREP conversion: full ownership, topology and external expression-consumer checks remain mandatory.

Read `Feature.GetFeatureWarningMessages()` at history checkpoints. Report baseline warnings separately; record added/removed messages with their stage and feature. Message order is insignificant; multiplicity is retained. Unavailable/unread data never means an empty list. Warnings new relative to the baseline stop the trial before reflection, after model update and during final history verification. A transient warning that clears before validation stays in the report without being declared a persistent failure. These queries introduce no additional model update.

For each original CAM boundary, report every selected object's NXOpen class and tag in the original order, including duplicates, along with the numerical boundary fields. Do not invent a meaning for the user report's `boundary_type=2`; it requires SDK 2606 verification. Lost boundaries are not recreated from an incomplete snapshot.

This is a limited target-selection correction and stronger validation, not a universal sketch adapter. No sketch dependency/constraint reconstruction, parameter removal or replacement with new curves is added. If body reflection itself damages sketches, the trial stops through the existing rollback path. These checks do not certify the complete sketch constraint definition.

## CAM difference diagnostics

V1.24 retains exact comparisons of the initial and repeated snapshots. On mismatch, report the name/class and tag of the actual changed CAM node, the Part/Blank/Check/Trim/Cut Area/Wall/Drive or Predrill Engage/Cut Region Start channel, and the boundary number where relevant. The stage identifies the comparison and first changed node, rather than the last completed geometry query.

Differences include read state (`read`, `provider`, `unavailable`), provider tag, counts and changed reference positions, ordered boundary members, and named `plane_type`, `boundary_type`, `material_side`, `ignore_holes`, `ignore_islands`, `ignore_chamfers` parameters. For custom points, show the record number, `Point tag`, `upper_depth`, `lower_depth`, `depth`. Missing records, unavailable channels and readable empty lists stay distinct. Tiny nonzero numbers are not rounded; depth-snapshot `None` remains a separate unused/unverified-field value.

Compare at the same four checkpoints: before and after custom-point transfer, before boundary-plane transfer, and final verification after CAM reads. Never replace the initial snapshot with the current one. Reflected point coordinates retain their separate check; expected coordinate motion is not a reference change. A read failure keeps its original NX/API code, traceback and read stage.

Build diagnostics in memory from captured snapshots and names, without querying NX objects again while describing differences. For readability, show the first 80 changed records and first 16 changed positions per sequence; explicitly report the full counts when abbreviated. Only text is limited: every record and position still participates in verification. Do not sort selections or remove duplicate references. A mismatch follows the existing failure/rollback path; failed preliminary checks prevent point or boundary writes.

The V1.23 report passed the previous MCS “2” stop and both Three Planes rebuild checks, then found a CAM-snapshot mismatch. It contains no actual differences, so the affected operation cannot be established and the last deburring-operation Drive read cannot be blamed. V1.24 repairs this diagnostic gap; **it does not establish that the CAM-data change itself is fixed**. Retain geometry/MCS transfer, stock/history protection, rollback rules and the absence of automatic saving.

## Files and validation

V1.30.04: **719 focused checks outside NX**, including **137 using C fixtures**. V1.30.03 stopped at the SleepyEntity guard before rule reading; its report reconfirmed arcs, body, topology and rollback. Unfiltered temporary-collector comparison, map expansion and association transfer still need DC 2606 verification. A complete mirrored CAM project remains unconfirmed.

Only the requested PRT copy is created. The journal writes no reports, INI files, logs, archives, temporary files or `.dlx` resources. It does not call Save, SaveAs or SaveAll. The report is held in memory and includes names, coordinates and the copy path. Nothing is sent automatically. A newly created PRT copy may remain on disk after a failed trial.

The V1.25 release passed 368 focused checks outside NX, including 53 using separately compiled test C libraries. Its 40 new scenarios cover sketch target selection, independent position/membership guards, retained-history loss, baseline/new/transient warnings, unreadable API results and boundary-member diagnostics. Three later report regressions bring the count to 371. The user V1.25 report confirms that the current transfer still fails on retained history; successful mirroring, manual save/reopen and toolpath regeneration remain unconfirmed. API-model tests and C fixtures do not establish NX compatibility.

## Later stages — not implemented yet

- Select two planes and automatically construct the mirror plane between them. Parallel planes use their midplane; behavior for nonparallel planes needs a separate decision.
- Component checkboxes: for example, mirror fixtures while leaving vises unmirrored. Component copying, placement and dependencies need separate validation.
- Reuse the Setup Card component-selection window behavior. These features are deferred until the trial without components works correctly.

## Milling and hole-making editor catalog

These are API classes, not operation-template names in the NX menu. This catalog does not establish complete coverage of new Designcenter 2606 templates. Unknown editors stop the trial. Numerical spatial operation settings still remain unchanged.

`CavityMillingBuilder`, `ChamferMillingBuilder`, `CylinderMillingBuilder`, `EdgeChamferBuilder`, `EngravingBuilder`, `FaceMillingBuilder`, `FacingBuilder`, `FacingZigZagBuilder`, `FeatureMillingBuilder`, `GenericFeatureOperationBuilder`, `GrooveMillingBuilder`, `HoleDrillingBuilder`, `HoleMakingBuilder`, `MultiAxisDeburringBuilder`, `MultiAxisRoughingBuilder`, `MultiAxisWallFinishingBuilder`, `MwBLDeburringOperationBuilder`, `PlanarMillingBuilder`, `PlanarRoughingBuilder`, `PlungeMillingBuilder`, `PointToPointBuilder`, `RadialGrooveMillingBuilder`, `RotaryRoughingBuilder`, `SurfaceContourBuilder`, `SwarfBuilder`, `ThreadMillingBuilder`, `VazlMillingBuilder`, `VolumeBased25DMillingOperationBuilder`, `WallProfilingBuilder`, `ZLevelMillingBuilder`, `ZlevelUndercutBuilder`.

[NXOpen OperationCollection reference (Siemens reference reproduced by Tech PLM)](https://docs.tech-plm.ru/documentation/external/PL20230425447184281/en-US/custom_api/nx_open_c_plus_plus_ref/a50101.html).

Detailed [coverage table and methods requiring SDK 2606](mirror-operations.md). No family is claimed to have all its settings fully transferred.
