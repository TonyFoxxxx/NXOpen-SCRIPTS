# Open the project folder

[Русский](../open-project-folder.md) | **English**

Script: [NX_Open_Project_Folder.cs](../../scripts/NX_Open_Project_Folder.cs). [All scripts](../../README.en.md#scripts).

Opens the folder containing the current PRT in Windows Explorer. It uses the work part, falling back to the displayed part if there is no work part. If an assembly component is the work part, its own folder opens.

Open a saved part and run the journal or its NX button. The part must have a valid saved path and the folder must exist; otherwise the script reports the problem.

It uses the Windows shell. It has no INI, creates no files or folders, and neither modifies nor saves the model.
