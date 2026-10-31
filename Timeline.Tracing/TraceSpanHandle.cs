namespace Timeline.Tracing
{
    public readonly record struct TraceSpanHandle
    {
        public string? Id { get; }

        internal Guid SessionId { get; }

        public bool IsValid => SessionId != Guid.Empty && !string.IsNullOrWhiteSpace(Id);

        internal TraceSpanHandle(Guid sessionId, string id)
        {
            if (sessionId == Guid.Empty)
            {
                throw new ArgumentException("세션 ID는 비어 있을 수 없습니다.", nameof(sessionId));
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            SessionId = sessionId;
            Id = id;
        }
    }
}
