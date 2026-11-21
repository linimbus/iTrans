using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Itrans.Audio;

internal sealed class PcmBufferStream : Stream
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _buffers = new();
    private byte[]? _current;
    private int _currentOffset;
    private bool _completed;
    private bool _disposed;
    private long _position;

    public void Enqueue(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        lock (_gate)
        {
            if (_completed || _disposed) return;
            _buffers.Enqueue(data.ToArray());
            Monitor.PulseAll(_gate);
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var written = 0;
            while (written < count)
            {
                if (_disposed) return written;

                if (_current != null && _currentOffset < _current.Length)
                {
                    var length = Math.Min(count - written, _current.Length - _currentOffset);
                    Array.Copy(_current, _currentOffset, buffer, offset + written, length);
                    _currentOffset += length;
                    _position += length;
                    written += length;
                    if (_currentOffset >= _current.Length) _current = null;
                    continue;
                }

                if (_buffers.Count > 0)
                {
                    _current = _buffers.Dequeue();
                    _currentOffset = 0;
                    continue;
                }

                if (_completed) return written;

                Monitor.Wait(_gate, 100);
            }

            return written;
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => long.MaxValue;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_gate)
        {
            switch (origin)
            {
                case SeekOrigin.Begin when offset == 0:
                    if (_position == 0) return 0;
                    return _position;
                case SeekOrigin.Current when offset == 0:
                    return _position;
                default:
                    return _position;
            }
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        lock (_gate)
        {
            _disposed = true;
            _completed = true;
            _buffers.Clear();
            _current = null;
            Monitor.PulseAll(_gate);
        }

        base.Dispose(disposing);
    }
}
