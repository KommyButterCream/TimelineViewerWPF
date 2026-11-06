using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Rendering
{
    public sealed class ScrollAxis : ObservableObject
    {
        private double _offset;
        public double Extent { get; private set;  }
        public double Viewport { get; private set; }

        public double MaxOffset => Math.Max(0, Extent - Viewport);

        public double Offset 
        {
            get => _offset;
            set => SetProperty(ref _offset, Math.Clamp(value, 0, MaxOffset));
        }

        public void UpdateSize(double extent, double viewport)
        {
            extent = Math.Max(0, extent);
            viewport = Math.Max(0, viewport);

            if(Extent == extent && Viewport == viewport)
            {
                return;
            }

            Extent = extent;
            Viewport = viewport;
            ScrollTo(Offset);

            OnPropertyChanged(nameof(Extent));
            OnPropertyChanged(nameof(Viewport));
            OnPropertyChanged(nameof(MaxOffset));
        }

        public void ScrollTo(double offset)
        {
            Offset = offset;
        }
    }
}
