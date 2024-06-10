> [!WARNING]
> **Deprecation Notice**  
> We are migrating this component into AvaUtils, a package bundle contains miscellaneous tools for manipulating an avatar within Unity.
> While download links are still valid in this repository, they will no longer updated for any bugs or new features.  
> https://github.com/JLChnToZ/avautils

> [!NOTE]
> The upstream repository was no longer maintained for years. This is my own fork of it.

Unity Animation Hierarchy Editor
================================

This utility will aid you in refactoring your Unity animations.

Place the AnimationHierarchyEditor.cs file in the `[project folder]/Editors/` folder to make it work. Then you'll be able to open the Animation Hierarchy Editor window by navigating to Window > Animation Hierarchy Editor.

The editor should appear once you've selected the animation clip you want to edit.

## Difference to the Original Version

This forked version has been enhanced to include these features:

- Supports batch refactoring by selecting multiple animation controllers, animation clips and/or nested blendtrees.
- Lock button to locks current selection
- Auto clone modified clips
- Auto updates selected animation clips on modifying hierarchy when toggled

## License

The original was released into public domain though, this modified version is licensed under [MIT](LICENSE).
