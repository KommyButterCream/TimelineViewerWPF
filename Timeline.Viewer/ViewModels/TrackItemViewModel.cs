using System.Collections.ObjectModel;

namespace Timeline.Viewer.ViewModels
{
    public sealed class TrackItemViewModel : ObservableObject
    {
        private bool _isExpanded = true;

        public TrackItemViewModel(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ObservableCollection<SpanItemViewModel> Spans { get; } =
            new ObservableCollection<SpanItemViewModel>();
    }
}
