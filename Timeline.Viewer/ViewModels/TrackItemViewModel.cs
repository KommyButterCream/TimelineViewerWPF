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
    }
}
