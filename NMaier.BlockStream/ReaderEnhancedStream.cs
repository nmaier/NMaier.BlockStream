using System.IO;

namespace NMaier.BlockStream
{
  public abstract class ReaderEnhancedStream : Stream {
#if NET48
    public abstract int Read(System.Span<byte> buffer);
#endif
  }
}