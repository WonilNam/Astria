using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Astria.Core.Models;

namespace Astria.Desktop.Views;

internal static class ApplicationPicker
{
    public static void Open(Control anchor, Func<bool, Task> select, Func<string, Task> addLocation)
    {
        var menu = new ContextMenu();
        var files = new MenuItem { Header = "应用 / 快捷方式 / 文件…" };
        var folders = new MenuItem { Header = "文件夹…" };
        files.Click += async (_, _) => await select(false);
        folders.Click += async (_, _) => await select(true);
        menu.Items.Add(files); menu.Items.Add(folders);
        menu.Items.Add(new Separator());
        foreach (var target in new[] { ShellLocations.ThisComputer, ShellLocations.RecycleBin })
        {
            var location = new MenuItem { Header = ShellLocations.Name(target) };
            location.Click += async (_, _) => await addLocation(target);
            menu.Items.Add(location);
        }
        anchor.ContextMenu = menu;
        menu.Open(anchor);
    }

    public static async Task<IReadOnlyList<string>> SelectAsync(IStorageProvider storage, bool folders)
    {
        IEnumerable<IStorageItem> items = folders
            ? await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "添加文件夹（点击卡片打开原目录）", AllowMultiple = true })
            : await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "添加应用、快捷方式或文件", AllowMultiple = true });
        return items.Select(item => item.TryGetLocalPath()).OfType<string>().ToList();
    }
}
