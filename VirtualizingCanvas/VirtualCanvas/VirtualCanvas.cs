﻿#define ANIMATE_FEEDBACK
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
 

namespace MinControl
{

#if ANIMATE_FEEDBACK
    public class VisualChangeEventArgs : EventArgs
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public VisualChangeEventArgs(int added, int removed)
        {
            Added = added;
            Removed = removed;
        }
    }
#endif
    public class VirtualCanvas : System.Windows.Controls.VirtualizingPanel, IScrollInfo
    {
        #region 成员变量
        ScrollViewer _owner;
        Size _viewPortSize;
        bool _canHScroll = false;
        bool _canVScroll = false;
        QuadTree<IVirtualChild> _index;
        ObservableCollection<IVirtualChild> _children;
        Size _smallScrollIncrement = new Size(10, 10);
        Canvas _content;
        Border _backdrop;
        TranslateTransform _translate;
        ScaleTransform _scale;
        Size _extent;
        IList<Rect> _dirtyRegions = new List<Rect>();
        IList<Rect> _visibleRegions = new List<Rect>();
        IDictionary<IVirtualChild, int> _visualPositions;
        int _nodeCollectCycle;
        bool _done = true;
        MapZoom _zoom;

        DispatcherTimer _timer;

        int _createQuanta = 1000;
        int _removeQuanta = 2000;
        int _gcQuanta = 5000;
        int _idealDuration = 50; // 50 milliseconds.
        int _added;
        int _removed;
        Rect _visible = Rect.Empty;
        delegate int QuantizedWorkHandler(int quantum);

#if ANIMATE_FEEDBACK
        public event EventHandler<VisualChangeEventArgs> VisualsChanged;
#endif
        #endregion

        #region 属性
        /// <summary>
        /// Get/Set the MapZoom object used for manipulating the scale and translation on this canvas.
        /// </summary>
        internal MapZoom Zoom
        {
            get { return _zoom; }
            set { _zoom = value; }
        }

        /// <summary>
        /// Returns true if all Visuals have been created for the current scroll position
        /// and there is no more idle processing needed.
        /// </summary>
        public bool IsDone
        {
            get { return _done; }
        }

        /// <summary>
        /// The current zoom transform.
        /// </summary>
        public ScaleTransform Scale
        {
            get { return _scale; }
        }

        /// <summary>
        /// The current translate transform.
        /// </summary>
        public TranslateTransform Translate
        {
            get { return _translate; }
        }

        /// <summary>
        /// Get/Set the IVirtualChild collection.  The VirtualCanvas will call CreateVisual on them
        /// when the Bounds of your child intersects the current visible view port.
        /// </summary>
        public ObservableCollection<IVirtualChild> VirtualChildren
        {
            get { return _children; }
            set
            {
                if (_children != null)
                {
                    _children.CollectionChanged -= new System.Collections.Specialized.NotifyCollectionChangedEventHandler(OnChildrenCollectionChanged);
                }
                _children = value ?? throw new ArgumentNullException("value");
                _children.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(OnChildrenCollectionChanged);
                RebuildVisuals();
            }
        }

        /// <summary>
        /// Set the scroll amount for the scroll bar arrows.
        /// </summary>
        public Size SmallScrollIncrement
        {
            get { return _smallScrollIncrement; }
            set { _smallScrollIncrement = value; }
        }

        /// <summary>
        /// The ContentCanvas that is actually the parent of all the VirtualChildren Visuals.
        /// </summary>
        public Canvas ContentCanvas
        {
            get { return _content; }
        }

        /// <summary>
        /// The backgrop is the back most child of the ContentCanvas used for drawing any sort
        /// of background that is guarenteed to fill the ViewPort.
        /// </summary>
        public Border Backdrop
        {
            get { return _backdrop; }
        }

        /// <summary>
        /// Return the full size of this canvas.
        /// </summary>
        public Size Extent
        {
            get { return _extent; }
        }
        #endregion

        #region 自定义依赖属性
        public static DependencyProperty VirtualChildProperty = DependencyProperty.Register("VirtualChild", typeof(IVirtualChild), typeof(VirtualCanvas));
        #endregion

        #region 构造函数
        /// <summary>
        /// Construct empty virtual canvas.
        /// </summary>
        public VirtualCanvas()
        {
            _index = new QuadTree<IVirtualChild>();
            _children = new ObservableCollection<IVirtualChild>();
            _children.CollectionChanged += new System.Collections.Specialized.NotifyCollectionChangedEventHandler(OnChildrenCollectionChanged);
            _content = new Canvas();
            _backdrop = new Border();
            _content.Children.Add(_backdrop);

            TransformGroup g = new TransformGroup();
            _scale = new ScaleTransform();
            _translate = new TranslateTransform();
            g.Children.Add(_scale);
            g.Children.Add(_translate);
            _content.RenderTransform = g;

            _translate.Changed += new EventHandler(OnTranslateChanged);
            _scale.Changed += new EventHandler(OnScaleChanged);
            this.Children.Add(_content);
        }
        #endregion

        #region 事件处理
        /// <summary>
        /// Callback when _children collection is changed.
        /// </summary>
        /// <param name="sender">This</param>
        /// <param name="e">noop</param>
        void OnChildrenCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RebuildVisuals();
        }

        /// <summary>
        /// Callback whenever the current TranslateTransform is changed.
        /// </summary>
        /// <param name="sender">TranslateTransform</param>
        /// <param name="e">noop</param>
        void OnTranslateChanged(object sender, EventArgs e)
        {
            OnScrollChanged();
        }

        /// <summary>
        /// Callback whenever the current ScaleTransform is changed.
        /// </summary>
        /// <param name="sender">ScaleTransform</param>
        /// <param name="e">noop</param>
        void OnScaleChanged(object sender, EventArgs e)
        {
            OnScrollChanged();
        }
        #endregion

        #region 方法
        /// <summary>
        /// Add a new IVirtualChild.  The VirtualCanvas will call CreateVisual on them
        /// when the Bounds of your child intersects the current visible view port.
        /// </summary>
        /// <param name="c"></param>
        public void AddVirtualChild(IVirtualChild child)
        {
            _children.Add(child);
        }

        /// <summary>
        /// Return the list of virtual children that intersect the given bounds.
        /// </summary>
        /// <param name="bounds">The bounds to test</param>
        /// <returns>The list of virtual children found or null if there are none</returns>
        public IEnumerable<IVirtualChild> GetChildrenIntersecting(Rect bounds)
        {
            if (_index != null)
            {
                return _index.GetNodesInside(bounds);
            }
            return null;
        }

        /// <summary>
        /// Return true if there are any virtual children inside the given bounds.
        /// </summary>
        /// <param name="bounds">The bounds to test</param>
        /// <returns>True if a node is found whose bounds intersect the given bounds</returns>
        public bool HasChildrenIntersecting(Rect bounds)
        {
            if (_index != null)
            {
                return _index.HasNodesInside(bounds);
            }
            return false;
        }

        /// <summary>
        /// The number of visual children that are visible right now.
        /// </summary>
        public int LiveVisualCount
        {
            get { return _content.Children.Count - 1; }
        }

        /// <summary>
        /// 
        /// Resets the state so there is no Visuals associated with this canvas.
        /// </summary>
        private void RebuildVisuals()
        {
            // need to rebuild the index.
            _index = null;
            _visualPositions = null;
            _visible = Rect.Empty;
            _done = false;
            foreach (UIElement e in _content.Children)
            {
                IVirtualChild n = e.GetValue(VirtualChildProperty) as IVirtualChild;
                if (n != null)
                {
                    e.ClearValue(VirtualChildProperty);
                    n.DisposeVisual();
                }
            }
            _content.Children.Clear();
            _content.Children.Add(_backdrop);
            InvalidateArrange();
            StartLazyUpdate();
        }

        /// <summary>
        /// Calculate the size needed to display all the virtual children.
        /// </summary>
        void CalculateExtent()
        {
            bool rebuild = false;
            if (_index == null || _extent.Width == 0 || _extent.Height == 0 ||
                double.IsNaN(_extent.Width) || double.IsNaN(_extent.Height))
            {
                rebuild = true;
                bool first = true;
                Rect extent = new Rect();
                _visualPositions = new Dictionary<IVirtualChild, int>();
                int index = 0;
                foreach (IVirtualChild c in _children)
                {
                    _visualPositions[c] = index++;

                    Rect childBounds = c.Bounds;
                    if (childBounds.Width != 0 && childBounds.Height != 0)
                    {
                        if (double.IsNaN(childBounds.Width) || double.IsNaN(childBounds.Height))
                        {
                            throw new InvalidOperationException(string.Format(System.Globalization.CultureInfo.CurrentUICulture,
                                "Child type '{0}' returned NaN bounds", c.GetType().Name));
                        }
                        if (first)
                        {
                            extent = childBounds;
                            first = false;
                        }
                        else
                        {
                            extent = Rect.Union(extent, childBounds);
                        }
                    }
                }
                _extent = extent.Size;
                // Ok, now we know the size we can create the index.
                _index = new QuadTree<IVirtualChild>();
                _index.Bounds = new Rect(0, 0, extent.Width, extent.Height);
                foreach (IVirtualChild n in _children)
                {
                    if (n.Bounds.Width > 0 && n.Bounds.Height > 0)
                    {
                        _index.Insert(n, n.Bounds);
                    }
                }
            }

            // Make sure we honor the min width & height.
            double w = Math.Max(_content.MinWidth, _extent.Width);
            double h = Math.Max(_content.MinHeight, _extent.Height);
            _content.Width = w;
            _content.Height = h;

            // Make sure the backdrop covers the ViewPort bounds.
            double zoom = _scale.ScaleX;
            if (!double.IsInfinity(this.ViewportHeight) &&
                !double.IsInfinity(this.ViewportWidth))
            {
                w = Math.Max(w, this.ViewportWidth / zoom);
                h = Math.Max(h, this.ViewportHeight / zoom);
                _backdrop.Width = w;
                _backdrop.Height = h;
            }

            if (_owner != null)
            {
                _owner.InvalidateScrollInfo();
            }

            if (rebuild)
            {
                AddVisibleRegion();
            }
        }

        /// <summary>
        /// Get the currently visible rectangle according to current scroll position and zoom factor and
        /// size of scroller viewport.
        /// </summary>
        /// <returns>A rectangle</returns>
        Rect GetVisibleRect()
        {
            // Add a bit of extra around the edges so we are sure to create nodes that have a tiny bit showing.
            double xstart = (this.HorizontalOffset - _smallScrollIncrement.Width) / _scale.ScaleX;
            double ystart = (this.VerticalOffset - _smallScrollIncrement.Height) / _scale.ScaleY;
            double xend = (this.HorizontalOffset + (_viewPortSize.Width + (2 * _smallScrollIncrement.Width))) / _scale.ScaleX;
            double yend = (this.VerticalOffset + (_viewPortSize.Height + (2 * _smallScrollIncrement.Height))) / _scale.ScaleY;
            return new Rect(xstart, ystart, xend - xstart, yend - ystart);
        }

        /// <summary>
        /// The visible region has changed, so we need to queue up work for dirty regions and new visible regions
        /// then start asynchronously building new visuals via StartLazyUpdate.
        /// </summary>
        void OnScrollChanged()
        {
            Rect dirty = _visible;
            AddVisibleRegion();
            _nodeCollectCycle = 0;
            _done = false;

            Rect intersection = Rect.Intersect(dirty, _visible);
            if (intersection == Rect.Empty)
            {
                _dirtyRegions.Add(dirty); // the whole thing is dirty
            }
            else
            {
                // Add left stripe
                if (dirty.Left < intersection.Left)
                {
                    _dirtyRegions.Add(new Rect(dirty.Left, dirty.Top, intersection.Left - dirty.Left, dirty.Height));
                }
                // Add right stripe
                if (dirty.Right > intersection.Right)
                {
                    _dirtyRegions.Add(new Rect(intersection.Right, dirty.Top, dirty.Right - intersection.Right, dirty.Height));
                }
                // Add top stripe
                if (dirty.Top < intersection.Top)
                {
                    _dirtyRegions.Add(new Rect(dirty.Left, dirty.Top, dirty.Width, intersection.Top - dirty.Top));
                }
                // Add right stripe
                if (dirty.Bottom > intersection.Bottom)
                {
                    _dirtyRegions.Add(new Rect(dirty.Left, intersection.Bottom, dirty.Width, dirty.Bottom - intersection.Bottom));
                }
            }

            StartLazyUpdate();
            InvalidateScrollInfo();
        }

        /// <summary>
        /// Tell the ScrollViewer to update the scrollbars because, extent, zoom or translate has changed.
        /// </summary>
        public void InvalidateScrollInfo()
        {
            if (_owner != null)
            {
                _owner.InvalidateScrollInfo();
            }
        }

        /// <summary>
        /// Add the current visible rect to the list of regions to process
        /// </summary>
        private void AddVisibleRegion()
        {
            _visible = GetVisibleRect();
            _visibleRegions.Clear();
            _visibleRegions.Add(_visible);
        }

        /// <summary>
        /// WPF Measure override for measuring the control
        /// </summary>
        /// <param name="availableSize">Available size will be the viewport size in the scroll viewer</param>
        /// <returns></returns>
        protected override Size MeasureOverride(Size availableSize)
        {
            base.MeasureOverride(availableSize);

            // We will be given the visible size in the scroll viewer here.
            CalculateExtent();

            if (availableSize != _viewPortSize)
            {
                SetViewportSize(availableSize);
            }

            foreach (UIElement child in this.InternalChildren)
            {
                IVirtualChild n = child.GetValue(VirtualChildProperty) as IVirtualChild;
                if (n != null)
                {
                    Rect bounds = n.Bounds;
                    child.Measure(bounds.Size);
                }
            }
            if (double.IsInfinity(availableSize.Width))
            {
                return _extent;
            }
            else
            {
                return availableSize;
            }
        }

        /// <summary>
        /// WPF ArrangeOverride for laying out the control
        /// </summary>
        /// <param name="finalSize">The size allocated by parents</param>
        /// <returns>finalSize</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            base.ArrangeOverride(finalSize);

            CalculateExtent();

            if (finalSize != _viewPortSize)
            {
                SetViewportSize(finalSize);
            }

            _content.Arrange(new Rect(0, 0, _content.Width, _content.Height));

            if (_index == null)
            {
                StartLazyUpdate();
            }

            return finalSize;
        }

        /// <summary>
        /// Set the viewport size and raize a scroll changed event.
        /// </summary>
        /// <param name="s">The new size</param>
        void SetViewportSize(Size s)
        {
            if (s != _viewPortSize)
            {
                _viewPortSize = s;
                OnScrollChanged();
            }
        }

        /// <summary>
        /// Begin a timer for lazily creating IVirtualChildren visuals
        /// </summary>
        void StartLazyUpdate()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Normal,
                    new EventHandler(OnStartLazyUpdate), this.Dispatcher);
            }
            _timer.Start();
        }

        /// <summary>
        /// Callback from the DispatchTimer
        /// </summary>
        /// <param name="sender">DispatchTimer </param>
        /// <param name="args">noop</param>
        void OnStartLazyUpdate(object sender, EventArgs args)
        {
            _timer.Stop();
            this.LazyUpdateVisuals();
        }

        /// <summary>
        /// 做一个量化的工作单元来创建新的可见的视觉效果，并清理不再需要的视觉效果
        /// Do a quantized unit of work for creating newly visible visuals, and cleaning up visuals that are no
        /// longer needed.
        /// </summary>
        void LazyUpdateVisuals()
        {
            if (_index == null)
            {
                this.CalculateExtent();
            }

            _done = true;
            _added = 0;
            _removed = 0;

            _createQuanta = SelfThrottlingWorker(_createQuanta, _idealDuration, new QuantizedWorkHandler(LazyCreateNodes));
            _removeQuanta = SelfThrottlingWorker(_removeQuanta, _idealDuration, new QuantizedWorkHandler(LazyRemoveNodes));
            _gcQuanta = SelfThrottlingWorker(_gcQuanta, _idealDuration, new QuantizedWorkHandler(LazyGarbageCollectNodes));

#if ANIMATE_FEEDBACK
            if (VisualsChanged != null)
            {
                VisualsChanged(this, new VisualChangeEventArgs(_added, _removed));
            }
#endif
            if (_added > 0)
            {
                InvalidateArrange();
            }
            if (!_done)
            {
                StartLazyUpdate();
                //this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new UpdateHandler(LazyUpdateVisuals));
            }
            this.InvalidateVisual();
        }

        /// <summary>
        /// Helper method for self-tuning how much time is allocated to the given handler.
        /// </summary>
        /// <param name="quantum">The current quantum allocation</param>
        /// <param name="idealDuration">The time in milliseconds we want to take</param>
        /// <param name="handler">The handler to call that does the work being throttled</param>
        /// <returns>Returns the new quantum to use next time that will more likely hit the ideal time</returns>
        private static int SelfThrottlingWorker(int quantum, int idealDuration, QuantizedWorkHandler handler)
        {
            PerfTimer timer = new PerfTimer();
            timer.Start();
            int count = handler(quantum);

            timer.Stop();
            long duration = timer.GetDuration();

            if (duration > 0 && count > 0)
            {
                long estimatedFullDuration = duration * (quantum / count);
                long newQuanta = (quantum * idealDuration) / estimatedFullDuration;
                quantum = Math.Max(100, (int)Math.Min(newQuanta, int.MaxValue));
            }

            return quantum;
        }

        /// <summary>
        /// Create visuals for the nodes that are now visible.
        /// </summary>
        /// <param name="quantum">Amount of work we can do here</param>
        /// <returns>Amount of work we did</returns>
        private int LazyCreateNodes(int quantum)
        {

            if (_visible == Rect.Empty)
            {
                _visible = GetVisibleRect();
                _visibleRegions.Add(_visible);
                _done = false;
            }

            int count = 0;
            int regionCount = 0;
            while (_visibleRegions.Count > 0 && count < quantum)
            {
                Rect r = _visibleRegions[0];
                _visibleRegions.RemoveAt(0);
                regionCount++;

                // Iterate over the visible range of nodes and make sure they have visuals.
                foreach (IVirtualChild n in _index.GetNodesInside(r))
                {
                    if (n.Visual == null)
                    {
                        EnsureVisual(n);
                        _added++;
                    }

                    count++;

                    if (count >= quantum)
                    {
                        // This region is too big, so subdivide it into smaller slices.
                        if (regionCount == 1)
                        {
                            // We didn't even complete 1 region, so we better split it.
                            SplitRegion(r, _visibleRegions);
                        }
                        else
                        {
                            _visibleRegions.Add(r); // put it back since we're not done!
                        }
                        _done = false;
                        break;
                    }
                }

            }
            return count;
        }

        /// <summary>
        /// Insert the visual for the child in the same order as is is defined in the 
        /// VirtualChildren collection so the visuals draw on top of each other in the expected order.
        /// The trick is that GetNodesIntersecting returns the nodes in pretty much random order depending 
        /// on how the QuadTree decides to break up the canvas.
        /// 根据在VirtualChildren集合中的顺序，依次插入visual，使得它们能够按照我们期待的顺序依次绘制叠盖。
        /// 其中小难点是GetNodesIntersecting方法返回的nodes顺序是相当随机的，因为这取决于QuadTree如何划分canvas
        /// 
        /// The thing we should avoid is a linear search through the potentially large collection of 
        /// IVirtualChildren to compute its visible index which is why we have the _visualPositions map.  
        /// We should also avoid a N*M algorithm where N is the number of nodes returned from GetNodesIntersecting 
        /// and M is the number of children already visible.  For example, Page down in a zoomed out situation 
        /// gives potentially high N and and M which would basically be an O(n2) algorithm.  
        /// 我们需要避免的是对有可能是很大集合的VirtualChildren进行线性查找，以此获得它们显示的顺序。因此我们有了_visualPosition
        /// map。我们也应该避免一个N*M的算法，其中N是通过GetNodesIntersecting方法返回的nodes数量，M是已经显示的children数量。
        /// 举例来说，通过Page down来缩小视图就会是潜在的高N和M的算法复杂度为O(n2)的操作
        /// 
        /// So the solution is to use the _visualPositions map to get the expected visual position index
        /// of a given IVirtualChild, then do a binary search through existing visible children to find the
        /// insertion point of the new child.  So this is O(Log M).  
        /// 所以解决方法就是使用_visualPositions map，根据传入的IVirtualChild获取对应的显示位置索引，
        /// 然后通过二分搜索已显示的children，找到新child的插入点。这是一个算法复杂度为O(Log M)的算法
        /// 
        /// </summary>
        /// <param name="child">The IVirtualChild to add visual for</param>
        public void EnsureVisual(IVirtualChild child)
        {
            if (child.Visual != null)
            {
                return;
            }

            UIElement e = child.CreateVisual(this);
            e.SetValue(VirtualChildProperty, child);
            Rect bounds = child.Bounds;
            Canvas.SetLeft(e, bounds.Left);
            Canvas.SetTop(e, bounds.Top);

            // Get the correct absolute position of this child.
            int position = _visualPositions[child];

            // Now do a binary search for the correct insertion position based
            // on the visual positions of the existing visible children.
            UIElementCollection c = _content.Children;
            int min = 0;
            int max = c.Count - 1;
            while (max > min + 1)
            {
                int i = (min + max) / 2;
                UIElement v = _content.Children[i];
                IVirtualChild n = v.GetValue(VirtualChildProperty) as IVirtualChild;
                if (n != null)
                {
                    int index = _visualPositions[n];
                    if (index > position)
                    {
                        // search from min to i.
                        max = i;
                    }
                    else
                    {
                        // search from i to max.
                        min = i;
                    }
                }
                else
                {
                    // Any nodes without IVirtualChild should be behind the
                    // IVirtualChildren by definition (like the Backdrop).
                    min = i;
                }
            }

            // If 'max' is the last child in the collection, then we need to see
            // if we have a new last child.
            if (max == c.Count - 1)
            {
                UIElement v = c[max];
                IVirtualChild maxchild = v.GetValue(VirtualChildProperty) as IVirtualChild;
                int maxpos = position;
                if (maxchild == null || position > _visualPositions[maxchild])
                {
                    // Then we have a new last child!
                    max++;
                }
            }

            c.Insert(max, e);

        }

        /// <summary>
        /// Split a rectangle into 2 and add them to the regions list.
        /// </summary>
        /// <param name="r">Rectangle to split</param>
        /// <param name="regions">List to add to</param>
        private void SplitRegion(Rect r, IList<Rect> regions)
        {
            double minWidth = this.SmallScrollIncrement.Width * 2;
            double minHeight = this.SmallScrollIncrement.Height * 2;

            if (r.Width > r.Height && r.Height > minHeight)
            {
                // horizontal slices
                double h = r.Height / 2;
                regions.Add(new Rect(r.Left, r.Top, r.Width, h + 10));
                regions.Add(new Rect(r.Left, r.Top + h, r.Width, h + 10));
            }
            else if (r.Width < r.Height && r.Width > minWidth)
            {
                // vertical slices
                double w = r.Width / 2;
                regions.Add(new Rect(r.Left, r.Top, w + 10, r.Height));
                regions.Add(new Rect(r.Left + w, r.Top, w + 10, r.Height));
            }
            else
            {
                regions.Add(r); // put it back since we're not done!
            }
        }

        /// <summary>
        /// Remove visuals for nodes that are no longer visible.
        /// </summary>
        /// <param name="quantum">Amount of work we can do here</param>
        /// <returns>Amount of work we did</returns>
        private int LazyRemoveNodes(int quantum)
        {
            Rect visible = GetVisibleRect();
            int count = 0;

            // Also remove nodes that are no longer visible.
            int regionCount = 0;
            while (_dirtyRegions.Count > 0 && count < quantum)
            {
                int last = _dirtyRegions.Count - 1;
                Rect dirty = _dirtyRegions[last];
                _dirtyRegions.RemoveAt(last);
                regionCount++;

                // Iterate over the visible range of nodes and make sure they have visuals.
                foreach (IVirtualChild n in _index.GetNodesInside(dirty))
                {
                    UIElement e = n.Visual;
                    if (e != null)
                    {
                        Rect nrect = n.Bounds;
                        if (!nrect.IntersectsWith(visible))
                        {
                            e.ClearValue(VirtualChildProperty);
                            _content.Children.Remove(e);
                            n.DisposeVisual();
                            _removed++;
                        }
                    }

                    count++;
                    if (count >= quantum)
                    {
                        if (regionCount == 1)
                        {
                            // We didn't even complete 1 region, so we better split it.
                            SplitRegion(dirty, _dirtyRegions);
                        }
                        else
                        {
                            _dirtyRegions.Add(dirty); // put it back since we're not done!
                        }
                        _done = false;
                        break;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Check all child nodes to see if any leaked from LazyRemoveNodes and remove their visuals.
        /// </summary>
        /// <param name="quantum">Amount of work we can do here</param>
        /// <returns>The amount of work we did</returns>
        int LazyGarbageCollectNodes(int quantum)
        {

            int count = 0;
            // Now after every update also do a full incremental scan over all the children
            // to make sure we didn't leak any nodes that need to be removed.
            while (count < quantum && _nodeCollectCycle < _content.Children.Count)
            {
                UIElement e = _content.Children[_nodeCollectCycle++];
                IVirtualChild n = e.GetValue(VirtualChildProperty) as IVirtualChild;
                if (n != null)
                {
                    Rect nrect = n.Bounds;
                    if (!nrect.IntersectsWith(_visible))
                    {
                        e.ClearValue(VirtualChildProperty);
                        _content.Children.Remove(e);
                        n.DisposeVisual();
                        _removed++;
                    }
                    count++;
                }
                _nodeCollectCycle++;
            }

            if (_nodeCollectCycle < _content.Children.Count)
            {
                _done = false;
            }

            return count;
        }

#if DEBUG_DUMP
        public void ShowQuadTree(bool show)
        {
            if (show)
            {
                _index.ShowQuadTree(_content);
            }
            else
            {
                RebuildVisuals();
            }
        }

        public void Dump(string fileName)
        {
            using (StreamWriter w = new StreamWriter(fileName))
            {
                using (LogWriter log = new LogWriter(w))
                {
                    log.Open("QuadTree");
                    _index.Dump(log);
                    log.Open("Other");
                    log.WriteAttribute("MaxDepth", log.MaxDepth.ToString(CultureInfo.CurrentUICulture));
                    log.Close();
                    log.Close();
                }
            }
        }
#endif

#if DEBUG_QUAD_TREE
        public void ShowQuadTree(bool show)
        {
            if (show)
            {
                _index.ShowQuadTree(_content);
            }
            else
            {
                RebuildVisuals();
            }
        }

        public void Dump(string filename)
        {
            using (StreamWriter w = new StreamWriter(filename))
            {
                using (LogWriter log = new LogWriter(w))
                {
                    log.Open("QuadTree");
                    _index.Dump(log);
                    log.Open("Other");
                    log.WriteAttribute("MaxDepth", log.MaxDepth.ToString());
                    log.Close();
                    log.Close();
                }
            }
        }
#endif
        #endregion

        #region IScrollInfo
        public bool CanVerticallyScroll { get { return _canVScroll; }  set { _canVScroll = value; } }
        public bool CanHorizontallyScroll { get { return _canHScroll; } set { _canHScroll = value; } } 

        public double ExtentWidth { get { return _extent.Height * _scale.ScaleY; } }

        public double ExtentHeight { get { return _extent.Width * _scale.ScaleX; } }

        public double ViewportWidth { get { return _viewPortSize.Width; } }

        public double ViewportHeight { get { return _viewPortSize.Height; } }

        public double HorizontalOffset { get { return -_translate.X; } }

        public double VerticalOffset { get { return -_translate.Y; } }

        public ScrollViewer ScrollOwner { get { return _owner; } set { _owner = value; } }

        /// <summary>
        /// Scroll down one small scroll increment.
        /// </summary>
        public void LineDown()
        {
            SetVerticalOffset(VerticalOffset + (_smallScrollIncrement.Height * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll left by one small scroll increment.
        /// </summary>
        public void LineLeft()
        {
            SetHorizontalOffset(HorizontalOffset - (_smallScrollIncrement.Width * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll right by one small scroll increment
        /// </summary>
        public void LineRight()
        {
            SetHorizontalOffset(HorizontalOffset + (_smallScrollIncrement.Width * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll up by one small scroll increment
        /// </summary>
        public void LineUp()
        {
            SetVerticalOffset(VerticalOffset - (_smallScrollIncrement.Height * _scale.ScaleX));
        }

        /// <summary>
        /// Make the given visual at the given bounds visible.
        /// </summary>
        /// <param name="visual">The visual that will become visible</param>
        /// <param name="rectangle">The bounds of that visual</param>
        /// <returns>The bounds that is actually visible.</returns>
        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (_zoom != null && visual != this)
            {
                return _zoom.ScrollIntoView(visual as FrameworkElement);
            }
            return rectangle;
        }

        /// <summary>
        /// Scroll down by one mouse wheel increment.
        /// </summary>
        public void MouseWheelDown()
        {
            SetVerticalOffset(VerticalOffset + (_smallScrollIncrement.Height * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll left by one mouse wheel increment.
        /// </summary>
        public void MouseWheelLeft()
        {
            SetHorizontalOffset(HorizontalOffset - (_smallScrollIncrement.Width * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll right by one mouse wheel increment.
        /// </summary>
        public void MouseWheelRight()
        {
            SetHorizontalOffset(HorizontalOffset + (_smallScrollIncrement.Width * _scale.ScaleX));
        }

        /// <summary>
        /// Scroll up by one mouse wheel increment.
        /// </summary>
        public void MouseWheelUp()
        {
            SetVerticalOffset(VerticalOffset - (_smallScrollIncrement.Height * _scale.ScaleX));
        }

        /// <summary>
        /// Page down by one view port height amount.
        /// </summary>
        public void PageDown()
        {
            SetVerticalOffset(VerticalOffset + _viewPortSize.Height);
        }

        /// <summary>
        /// Page left by one view port width amount.
        /// </summary>
        public void PageLeft()
        {
            SetHorizontalOffset(HorizontalOffset - _viewPortSize.Width);
        }

        /// <summary>
        /// Page right by one view port width amount.
        /// </summary>
        public void PageRight()
        {
            SetHorizontalOffset(HorizontalOffset + _viewPortSize.Width);
        }

        /// <summary>
        /// Page up by one view port height amount.
        /// </summary>
        public void PageUp()
        {
            SetVerticalOffset(VerticalOffset - _viewPortSize.Height);
        }

        /// <summary>
        /// Scroll to the given absolute horizontal scroll position.
        /// </summary>
        /// <param name="offset">The horizontal position to scroll to</param>
        public void SetHorizontalOffset(double offset)
        {
            double xoffset = Math.Max(Math.Min(offset, ExtentWidth - ViewportWidth), 0);
            _translate.X = -xoffset;
            OnScrollChanged();
        }

        /// <summary>
        /// Scroll to the given absolute vertical scroll position.
        /// </summary>
        /// <param name="offset">The vertical position to scroll to</param>
        public void SetVerticalOffset(double offset)
        {
            double yoffset = Math.Max(Math.Min(offset, ExtentHeight - ViewportHeight), 0);
            _translate.Y = -yoffset;
            OnScrollChanged();
        }
        #endregion
    }
}
