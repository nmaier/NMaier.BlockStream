using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public abstract class BlockStream : ReaderEnhancedStream
  {
    public const short BLOCK_SIZE = 16384;
    protected internal readonly short BlockSize;
    protected readonly IBlockCache? Cache;
    protected readonly Dictionary<long, Extent> Extents = new();
    protected readonly IBlockTransformer Transformer;
    protected readonly Stream WrappedStream;
    protected long CurrentFooterLength;
    protected long CurrentLength;
    protected long CurrentPosition;

    protected BlockStream(Stream wrappedStream, IBlockTransformer transformer,
      short blockSize = BLOCK_SIZE, IBlockCache? cache = null)
    {
      if (blockSize is < 512 or > 28671) {
        ThrowHelpers.ThrowArgumentOutOfRangeException(
          nameof(blockSize),
          blockSize,
          "Block size has to be >= 512 and <= 28671");
      }

      WrappedStream = wrappedStream;
      Transformer = transformer;
      BlockSize = blockSize;
      Cache = cache;
    }

    protected override void Dispose(bool disposing)
    {
      Flush();
      Extents.Clear();
      Cache?.Dispose();
      WrappedStream.Dispose();

      base.Dispose(disposing);
    }

    protected void WriteFooter()
    {
      var totalLength = 0L;
      using var ms = new MemoryStream();
      using (var writer = new BinaryWriter(ms, Encoding.ASCII, true)) {
        foreach (var extent in Extents.Values) {
          writer.Write(extent.Offset);
          writer.Write(extent.Length);
          totalLength += extent.Length;
        }

        writer.Write(ms.Length);
        writer.Write(CurrentLength);
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      _ = WrappedStream.Seek(totalLength, SeekOrigin.Begin);
      ms.CopyTo(WrappedStream);
      WrappedStream.SetLength(WrappedStream.Position);
      CurrentFooterLength = CurrentLength;
    }

    protected void WriteFooterLength()
    {
      const int LEN = sizeof(long);
      Span<byte> buf = stackalloc byte[LEN];
      BinaryPrimitives.WriteInt64LittleEndian(buf, CurrentLength);

      _ = WrappedStream.Seek(-LEN, SeekOrigin.End);
      WrappedStream.Write(buf);
      CurrentFooterLength = CurrentLength;
    }
  }
}
