using System.IO;
#if NETFRAMEWORK
using System;
#endif

namespace NMaier.BlockStream;

public abstract class ReaderEnhancedStream : Stream
{
#if NETFRAMEWORK
    /// <summary>
    ///   See <see cref="Stream.Read" />
    /// </summary>
    /// <param name="buffer">Target for the read</param>
    /// <returns>Number of bytes read</returns>
    public abstract int Read(Span<byte> buffer);
#endif
}
