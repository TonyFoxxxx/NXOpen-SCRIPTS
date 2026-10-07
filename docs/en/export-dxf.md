# Export the current view to DXF

[Русский](../export-dxf.md) | **English**

Script: [NX_Export_Current_View_To_DXF.cs](../../scripts/NX_Export_Current_View_To_DXF.cs). [All scripts](../../README.en.md#scripts).

Exports an orthographic 2D projection of the current view at 1:1 scale.

## Modes

- **Visible edges:** includes visible holes, steps and pockets.
- **Outer outline only:** exports the external contour.
- **Selected curves and edges:** available when appropriate objects were selected before opening the dialog.

The script captures selection before showing the dialog and uses the current view and visibility.

## Use

1. Save the displayed PRT and set the required view and visible geometry.
2. Preselect curves or edges if using selection mode.
3. Run the journal, choose the mode and enter a filename; the default is the project name.
4. Start export and check the DXF in the PRT folder after the NX export job finishes.

The native NX DXF translator and `dxfdwg.def` are required. Export runs as an NX background job, so control can return before it finishes.

## Files and limitations

Temporary geometry, views and sheets are cleaned up. Fully Shaded and the previous Face Edges setting are restored. The script checks the result and two native translator logs; cleanup problems may also be reported on the next launch.

It writes `<filename>.dxf` beside the saved displayed PRT. There is no script INI or separate report folder, although the native translator can create auxiliary files. The PRT is not saved automatically.

The script can restore a previous DXF only before the background export begins; NX controls the write afterwards. Ambiguous or unsupported geometry is reported rather than guessed. Each outline is limited to 402 segments.
