using System.Text;

namespace CivDoom.Engine;

/// <summary>Reads newline-separated UTF-8 messages from a network stream, refusing any longer than a limit.</summary>
internal sealed class LineReader
{
    private readonly Stream _stream;
    private readonly int _maxLine;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly MemoryStream _line = new();
    private int _start, _end;

    public LineReader(Stream stream, int maxLine)
    {
        _stream = stream;
        _maxLine = maxLine;
    }

    /// <returns>The next line (without the newline), or null when the other end has closed.</returns>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            int nl = Array.IndexOf(_buffer, (byte)'\n', _start, _end - _start);
            if (nl >= 0)
            {
                _line.Write(_buffer, _start, nl - _start);
                _start = nl + 1;
                string text = Encoding.UTF8.GetString(_line.GetBuffer(), 0, (int)_line.Length);
                _line.SetLength(0);
                return text;
            }
            _line.Write(_buffer, _start, _end - _start);
            if (_line.Length > _maxLine) throw new IOException("Message too long");
            _start = _end = 0;
            int n = await _stream.ReadAsync(_buffer, ct).ConfigureAwait(false);
            if (n <= 0) return null;
            _end = n;
        }
    }
}
