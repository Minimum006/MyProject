using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Shapes;

namespace MinControl.Model
{
    class VirtualEllipse : IVirtualChild
    {
        private Rect _bounds;
        public Rect Bounds{ get { return _bounds; } set { _bounds = value; } }

        private UIElement _visual;
        public UIElement Visual { get { return _visual; } set { _visual = value; } }

        public event EventHandler BoundsChanged;

        public UIElement CreateVisual(VirtualCanvas parent)
        {
            if (_visual == null)
            {
                Ellipse e = new Ellipse();
                e.Width = _bounds.Width;
                e.Height = _bounds.Height;
                e.Style = parent.Resources["MyEllipseStyle"] as Style;
                _visual = e;
            }
            return _visual;
        }

        public void DisposeVisual()
        {
            _visual = null;
        }
    }
}
