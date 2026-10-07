# Open the setup-sheet folder

[Русский](../open-setup-cards-folder.md) | **English**

Script: [NX_Open_Setup_Cards_Folder.py](../../scripts/NX_Open_Setup_Cards_Folder.py). [All scripts](../../README.en.md#scripts).

Opens the existing setup-sheet folder for the saved work part in Windows Explorer:

```text
<PRT folder>\Карты Наладки\<project name without .prt>
```

The actual folder name remains `Карты Наладки`. Invalid Windows characters in the project name are replaced using the same rules as the [setup-sheet generator](setup-card.md).

Open the saved project and run the journal or its NX button. If no folder exists, the message shows the expected location. This script does not generate a setup sheet or create the folder.

Windows and a saved work part are required. There is no INI and no file creation, model modification or automatic PRT save.
