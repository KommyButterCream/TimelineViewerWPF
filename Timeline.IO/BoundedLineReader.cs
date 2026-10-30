using System.Text;

namespace Timeline.IO
{
    internal readonly record struct BoundedLine(string Text, bool IsTooLong);

    internal sealed class BoundedLineReader
    {
        private readonly StreamReader _reader;
        private readonly int _maxLineLength;

        private readonly char[] _buffer = new char[4096];

        private int _position;
        private int _count;
        private bool _skipNextLf;

        public BoundedLineReader(StreamReader reader, int maxLineLength)
        {
            ArgumentNullException.ThrowIfNull(reader);

            if (maxLineLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxLineLength));
            }

            _reader = reader;
            _maxLineLength = maxLineLength;
        }

        public async ValueTask<BoundedLine?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = new StringBuilder(Math.Min(_maxLineLength, 4096));

            bool hasCharacters = false;
            bool isTooLong = false;

            while (true)
            {
                if (_position >= _count)
                {
                    _count = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);

                    _position = 0;

                    if (_count == 0)
                    {
                        if (!hasCharacters)
                        {
                            return null;
                        }

                        return new BoundedLine(text.ToString(), isTooLong);
                    }
                }

                char character = _buffer[_position++];

                if (_skipNextLf)
                {
                    _skipNextLf = false;

                    if (character == '\n')
                    {
                        continue;
                    }
                }

                if (character == '\r' || character == '\n')
                {
                    _skipNextLf = character == '\r';

                    return new BoundedLine(text.ToString(), isTooLong);
                }

                hasCharacters = true;

                if (text.Length < _maxLineLength)
                {
                    text.Append(character);
                }
                else
                {
                    isTooLong = true;
                }
            }
        }
    }
}
