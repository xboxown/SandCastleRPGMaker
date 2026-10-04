namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Single source of truth for the debug build tag shown in every major
/// editor window's title bar (Map Editor, PC Editor, Stat Editor,
/// Variable Editor, Faction Editor, Sprite Editor, Map Event, Book
/// Editor, Game Test...). Update this ONE constant per build instead of
/// hunting down a hardcoded string in each window - that drift is exactly
/// what left the Sprite Editor's title stuck on a stale "DEBUG-BUILD-22"
/// while everything else had moved on. Remove all uses of this (or just
/// stop appending it) once the project reaches final release.
/// </summary>
public static class BuildInfo
{
    public const string CurrentBuild = "DEBUG-BUILD-230";
}
