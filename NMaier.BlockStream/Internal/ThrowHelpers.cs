using System;
using System.IO;
using System.Runtime.CompilerServices;
#if !NETFRAMEWORK
using System.Diagnostics.CodeAnalysis;
#endif

namespace NMaier.BlockStream.Internal
{
  internal static class ThrowHelpers
  {
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidBlock()
    {
      throw new IOException("Invalid block");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentException(string message, string name)
    {
      throw new ArgumentException(message, name);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentOutOfRangeException(string name, object value,
      string msg)
    {
      throw new ArgumentOutOfRangeException(name, value, msg);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowBadExtent()
    {
      throw new IOException("Cannot write to bad extent");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowBlockTooLarge()
    {
      throw new IOException("Transformed block too large!");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowCorruptBlock()
    {
      throw new IOException("Corrupt transformed block");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowCorruptCurrentIndex()
    {
      throw new IOException("Corrupt currentIndex");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowCorruptData()
    {
      throw new IOException("Corrupt data");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidExtent()
    {
      throw new IOException("Invalid extent");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidLZ4Data()
    {
      throw new IOException("Invalid LZ4 data");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowInvalidMAC()
    {
      throw new IOException("Invalid MAC");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowLargeBlock()
    {
      throw new IOException(
        "Cannot write new compress-able block larger than on disk block");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowMismatchedChecksum()
    {
      throw new IOException("Mismatched checksum");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowNoRandom()
    {
      throw new IOException("Cannot random write to compress-able stream");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowSeekFailed()
    {
      throw new IOException("Seek failed");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowTruncatedRead()
    {
      throw new EndOfStreamException("Truncated read");
    }

#if NETFRAMEWORK
    internal sealed class DoesNotReturnAttribute : Attribute
    {
    }
#endif
  }
}
