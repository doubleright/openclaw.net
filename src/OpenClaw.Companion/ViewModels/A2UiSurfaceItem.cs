using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenClaw.Companion.ViewModels;

public sealed partial class A2UiSurfaceItem : ObservableObject
{
    public A2UiSurfaceItem(string surfaceId, string catalogId, string title)
    {
        SurfaceId = surfaceId;
        CatalogId = catalogId;
        Title = title;
    }

    public string SurfaceId { get; }

    [ObservableProperty]
    private string _catalogId;

    [ObservableProperty]
    private string _title;

    public ObservableCollection<A2UiFrameItem> Components { get; } = new();
    public ObservableCollection<string> Diagnostics { get; } = new();

    [ObservableProperty]
    private string? _dataModelJson;

    [ObservableProperty]
    private bool _isActive;
}
