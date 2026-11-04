using System.Collections.ObjectModel;

namespace Timeline.Viewer.ViewModels
{
    public sealed class TrackItemViewModel
    {
        public TrackItemViewModel(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }

        public ObservableCollection<SpanItemViewModel> Spans { get; } =
            new ObservableCollection<SpanItemViewModel>();
    }
}
