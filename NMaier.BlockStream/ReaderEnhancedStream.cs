using System.IO;

namespace NMaier.BlockStream
{
  public abstract class ReaderEnhancedStream : Stream
  {
#if NET48
    /// <summary>
    /// See <see cref="Stream.Read"/>
    /// </summary>
    /// <param name="buffer">Target for the read</param>
    /// <returns>Number of bytes read</returns>
    public abstract int Read(System.Span<byte> buffer);
#endif
  }
}