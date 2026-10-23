using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Astria.Desktop.Services;

internal sealed class TileDrag
{
    internal static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("Astria.Tile");
    private Point _origin;
    private string? _id;
    private bool _dragging;

    public bool SuppressClick { get; private set; }

    public void Press(Control control, PointerPressedEventArgs e, string id)
    {
        SuppressClick = false;
        _id = e.GetCurrentPoint(control).Properties.IsLeftButtonPressed ? id : null;
        _origin = e.GetPosition(control);
    }

    public async Task MoveAsync(Control control, PointerEventArgs e)
    {
        if (_dragging || _id is null || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed
            || Math.Abs(e.GetPosition(control).X - _origin.X) + Math.Abs(e.GetPosition(control).Y - _origin.Y) < 7) return;
        _dragging = true;
        SuppressClick = true;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(Format, _id));
        _id = null;
        try { await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move | DragDropEffects.Link); }
        finally { _dragging = false; }
    }

    public static string? Read(DragEventArgs e) => e.DataTransfer.TryGetValue(Format);
}
