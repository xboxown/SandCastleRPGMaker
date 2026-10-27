using System.Collections;
using System.Reflection;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Everything in the project that uses a sprite names it by its folder and its name (a pair of
/// properties "...SpriteGroup" / "...SpriteName", or a SpriteLink's SpriteGroup / SpriteName).
/// When the Sprite Editor moves a sprite to another folder (cut and paste), renames it, or renames
/// a folder, those uses are pointed at its new place - so nothing loses its look.
/// </summary>
internal static class SpriteReferences
{
    /// <summary>Every database a sprite can be used from.</summary>
    private static IEnumerable<object?> Roots() => new object?[]
    {
        ProjectSession.ObjectsProvider?.Invoke(), ProjectSession.CharactersProvider?.Invoke(), ProjectSession.BooksProvider?.Invoke(),
        ProjectSession.ContainersProvider?.Invoke(), ProjectSession.HousesProvider?.Invoke(), ProjectSession.SettingsProvider?.Invoke(),
        ProjectSession.PosesProvider?.Invoke(), ProjectSession.WorldsProvider?.Invoke()
    };

    /// <summary>A sprite moved or renamed: every use of (oldGroup, oldName) now (newGroup, newName). How many were changed.</summary>
    public static int Move(string oldGroup, string oldName, string newGroup, string newName) =>
        Walk((group, name) => group == oldGroup && name == oldName || group.Length == 0 && name == oldName, (newGroup, newName));

    /// <summary>A folder renamed: every use of a sprite in it now names the new folder.</summary>
    public static int RenameFolder(string oldGroup, string newGroup) =>
        Walk((group, _) => group == oldGroup, null, newGroup);

    private static int Walk(Func<string, string, bool> matches, (string Group, string Name)? to, string? newGroupOnly = null)
    {
        int changed = 0;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Visit(object? node, int depth)
        {
            if (node is null || depth > 40 || node is string || node.GetType().IsPrimitive || node.GetType().IsEnum || !seen.Add(node)) return;
            var type = node.GetType();
            if (node is IDictionary dictionary) { foreach (var value in dictionary.Values) Visit(value, depth + 1); return; }
            if (node is IEnumerable list)
            {
                if (type.IsGenericType && type.GetGenericArguments()[0] is { } item && (item.IsPrimitive || item.IsEnum || item == typeof(string))) return; // tile numbers, ids
                foreach (var element in list) Visit(element, depth + 1);
                return;
            }
            if (type.Namespace?.StartsWith("SandcastleRpgMaker") != true) return;
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).ToList();
            foreach (var groupProperty in properties.Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("SpriteGroup") && p.CanWrite))
            {
                var nameProperty = properties.FirstOrDefault(p => p.Name == groupProperty.Name[..^"Group".Length] + "Name" && p.PropertyType == typeof(string) && p.CanWrite);
                if (nameProperty is null) continue;
                string group = groupProperty.GetValue(node) as string ?? "", name = nameProperty.GetValue(node) as string ?? "";
                if (name.Length == 0 && newGroupOnly is null || !matches(group, name)) continue;
                if (to is { } target) { groupProperty.SetValue(node, target.Group); nameProperty.SetValue(node, target.Name); }
                else if (newGroupOnly is not null) groupProperty.SetValue(node, newGroupOnly);
                changed++;
            }
            foreach (var property in properties)
            {
                if (property.PropertyType == typeof(string) || property.PropertyType.IsPrimitive || property.PropertyType.IsEnum) continue;
                object? value;
                try { value = property.GetValue(node); } catch { continue; }
                Visit(value, depth + 1);
            }
        }
        foreach (var root in Roots()) Visit(root, 0);
        return changed;
    }
}
