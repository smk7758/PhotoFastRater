using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Size = System.Windows.Size;

namespace PhotoFastRater.UI.Collections;

/// <summary>Realizes only visible uniform photo rows; decoded images remain separate from bounded catalog data.</summary>
public sealed class VirtualizingPhotoPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemSizeProperty = DependencyProperty.Register(
        nameof(ItemSize), typeof(double), typeof(VirtualizingPhotoPanel),
        new FrameworkPropertyMetadata(200d, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ItemSize { get => (double)GetValue(ItemSizeProperty); set => SetValue(ItemSizeProperty, value); }
    private Size _extent;
    private Size _viewport;
    private double _offset;
    private int _columns = 1;

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            RemoveInternalChildRange(0, InternalChildren.Count);
        else if (args.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Remove or System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
        {
            if (args.ItemUICount > 0 && args.Position.Index >= 0 && args.Position.Index < InternalChildren.Count)
                RemoveInternalChildRange(args.Position.Index, Math.Min(args.ItemUICount, InternalChildren.Count - args.Position.Index));
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        if (owner is null) return availableSize;
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(1, ScrollOwner?.ActualWidth ?? 1);
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : Math.Max(1, ScrollOwner?.ActualHeight ?? 1);
        _viewport = new Size(width, height);
        var cell = Math.Max(1, ItemSize + 8);
        _columns = Math.Max(1, (int)(width / cell));
        _extent = new Size(width, Math.Ceiling((double)owner.Items.Count / _columns) * cell);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - height));
        ScrollOwner?.InvalidateScrollInfo();
        var first = Math.Max(0, ((int)(_offset / cell) - 1) * _columns);
        var last = Math.Min(owner.Items.Count - 1, ((int)Math.Ceiling((_offset + height) / cell) + 1) * _columns - 1);
        var generator = ItemContainerGenerator;
        // Remove offscreen containers before realizing the next range; maximum visuals depend on viewport, not photo count.
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex >= first && itemIndex <= last) continue;
            // Recycle the bounded viewport containers instead of rebuilding native text/layout resources on every scroll.
            if (generator is IRecyclingItemContainerGenerator recycling)
                recycling.Recycle(new GeneratorPosition(childIndex, 0), 1);
            else
                generator.Remove(new GeneratorPosition(childIndex, 0), 1);
            RemoveInternalChildRange(childIndex, 1);
        }
        if (last < first) return _viewport;
        var position = generator.GeneratorPositionFromIndex(first);
        var insertion = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, true))
        {
            for (var index = first; index <= last; index++, insertion++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized || !InternalChildren.Contains(child))
                {
                    if (insertion >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(insertion, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(new Size(cell, cell));
            }
        }
        return _viewport;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cell = Math.Max(1, ItemSize + 8);
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
            InternalChildren[index].Arrange(new Rect((itemIndex % _columns) * cell, (itemIndex / _columns) * cell - _offset, cell, cell));
        }
        return finalSize;
    }

    public void SetVerticalOffset(double offset)
    {
        var next = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (Math.Abs(next - _offset) < .01) return;
        _offset = next; InvalidateMeasure(); ScrollOwner?.InvalidateScrollInfo();
    }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = visual as DependencyObject;
        while (child is not null && VisualTreeHelper.GetParent(child) != this) child = VisualTreeHelper.GetParent(child);
        if (child is not UIElement element) return rectangle;
        var position = InternalChildren.IndexOf(element);
        var index = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(position, 0));
        var top = index / _columns * (ItemSize + 8);
        if (top < _offset) SetVerticalOffset(top);
        else if (top + ItemSize + 8 > _offset + ViewportHeight) SetVerticalOffset(top + ItemSize + 8 - ViewportHeight);
        return rectangle;
    }
    protected override void BringIndexIntoView(int index) => SetVerticalOffset(index / _columns * (ItemSize + 8));
    public void LineUp() => SetVerticalOffset(_offset - ItemSize - 8);
    public void LineDown() => SetVerticalOffset(_offset + ItemSize + 8);
    public void MouseWheelUp() => SetVerticalOffset(_offset - (ItemSize + 8) * 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset + (ItemSize + 8) * 3);
    public void PageUp() => SetVerticalOffset(_offset - ViewportHeight);
    public void PageDown() => SetVerticalOffset(_offset + ViewportHeight);
    public void LineLeft() { }
    public void LineRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void SetHorizontalOffset(double offset) { }
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
}
