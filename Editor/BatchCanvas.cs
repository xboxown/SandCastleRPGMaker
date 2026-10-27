using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Map Editor's canvas. A WPF Panel sorts all its children by Z index again every time one is added while
/// their Z indexes differ - with a big map's hundreds of thousands of tiles and objects that is minutes of work
/// (each add costs as much as all the children so far). While a batch is open (building the whole map) the
/// sorting waits, and is done once when the batch ends.
/// </summary>
public sealed class BatchCanvas : Canvas
{
    private int _batches;
    private bool _changed;

    /// <summary>Opens a batch; dispose it when done adding.</summary>
    public IDisposable Batch()
    {
        _batches++;
        return new Closer(this);
    }

    /// <summary>
    /// The map's tiles and things aren't offered to accessibility tools one by one: walking hundreds of thousands of
    /// them after every layout (each animated sprite's frame) kept a core busy while the editor sat idle.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new MapPeer(this);

    private sealed class MapPeer : FrameworkElementAutomationPeer
    {
        public MapPeer(FrameworkElement owner) : base(owner) { }
        protected override List<AutomationPeer>? GetChildrenCore() => null;
        protected override string GetClassNameCore() => "MapCanvas";
    }

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        if (_batches > 0) { _changed = true; return; } // sorted once, at the end of the batch
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
    }

    private void EndBatch()
    {
        if (--_batches > 0 || !_changed) return;
        _changed = false;
        // One child more with a Z index of its own (and taken off again): the Panel sorts its children once.
        var probe = new UIElement();
        SetZIndex(probe, int.MinValue);
        Children.Add(probe);
        Children.Remove(probe);
        InvalidateVisual();
    }

    private sealed class Closer : IDisposable
    {
        private BatchCanvas? _canvas;
        public Closer(BatchCanvas canvas) => _canvas = canvas;
        public void Dispose() { _canvas?.EndBatch(); _canvas = null; }
    }
}
