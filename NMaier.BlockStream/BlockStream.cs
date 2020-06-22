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
    protected readonly Dictionary<long, Extent> Extents = new Dictionary<long, Extent>();
    protected readonly IBlockTransformer Transformer;
    protected readonly Stream WrappedStream;
    protected long CurrentFooterLength;
    protected long CurrentLength;
    protected long CurrentPosition;

    protected BlockStream(Stream wrappedStream, IBlockTransformer transformer, short blockSize = BLOCK_SIZE,
      IBlockCache? cache = null)
    {
      if (blockSize < 512 || blockSize > 28671) {
        throw new ArgumentOutOfRangeException(nameof(blockSize));
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

      if (disposing) {
        GC.SuppressFinalize(this);
      }

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

      ms.Seek(0, SeekOrigin.Begin);
      WrappedStream.Seek(totalLength, SeekOrigin.Begin);
      ms.CopyTo(WrappedStream);
      WrappedStream.SetLength(WrappedStream.Position);
      CurrentFooterLength = CurrentLength;
    }

    protected void WriteFooterLength()
    {
      const int LEN = sizeof(long);
      Span<byte> buf = stackalloc byte[LEN];
      BinaryPrimitives.WriteInt64LittleEndian(buf, CurrentLength);

      WrappedStream.Seek(-LEN, SeekOrigin.End);
      WrappedStream.Write(buf);
      CurrentFooterLength = CurrentLength;
    }

    protected void WriteFooterLengthAndLast(long offset, short count)
    {
      const int LEN = sizeof(long) * 2 + sizeof(short);
      Span<byte> buf = stackalloc byte[LEN];
      BinaryPrimitives.WriteInt64LittleEndian(buf, offset);
      BinaryPrimitives.WriteInt16LittleEndian(buf.Slice(sizeof(long)), count);
      BinaryPrimitives.WriteInt64LittleEndian(buf.Slice(sizeof(long) + sizeof(short)), CurrentLength);

      WrappedStream.Seek(-LEN, SeekOrigin.End);
      WrappedStream.Write(buf);
      CurrentFooterLength = CurrentLength;
    }
  }
}