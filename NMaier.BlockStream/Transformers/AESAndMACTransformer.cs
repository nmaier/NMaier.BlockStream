using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class AESAndMACTransformer : IBlockTransformer
  {
    private readonly byte[] cryptoKey;
    private readonly byte[] macKey;

    public AESAndMACTransformer(string key) : this(Encoding.UTF8.GetBytes(key))
    {
    }

    public AESAndMACTransformer(byte[] key)
    {
      cryptoKey = key.DeriveKeyBytesReasonablySafeNotForStorage(24);
      macKey = key.DeriveKeyBytesReasonablySafeNotForStorage(64);
    }

    public bool MayChangeSize => true;

    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      using var hmac = new HMACSHA256(macKey);
      var rv = new byte[block.Length + 32];
      var inputBuffer = block.ToArray();
      using var aes = new AesCounterMode();
      using var enc = aes.CreateEncryptor(cryptoKey, Array.Empty<byte>());
      var encrypted = enc.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
      var hash = hmac.ComputeHash(encrypted);
      hash.CopyTo(rv.AsSpan());
      encrypted.CopyTo(rv.AsSpan(hash.Length));
      return rv;
    }

    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      using var hmac = new HMACSHA256(macKey);
      var hash = hmac.ComputeHash(input.Slice(32).ToArray());
      if (!input.Slice(0, 32).SequenceEqual(hash)) {
        throw new IOException("Invalid MAC");
      }

      using var aes = new AesCounterMode();
      using var enc = aes.CreateDecryptor(cryptoKey, Array.Empty<byte>());
      var dec = enc.TransformFinalBlock(input.Slice(32).ToArray(), 0, input.Length - 32);
      dec.CopyTo(block);
      return dec.Length;
    }

    internal sealed class AesCounterMode : SymmetricAlgorithm
    {
      private readonly AesManaged aes;
      private readonly byte[] counter;

      public AesCounterMode(byte[]? counter = null)
      {
        this.counter = counter ?? new byte[16];
        aes = new AesManaged { BlockSize = 128, Mode = CipherMode.ECB, Padding = PaddingMode.None, KeySize = 192 };
      }

      public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[] _)
      {
        return new CounterModeCryptoTransform(aes, rgbKey, counter);
      }

      public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[] _)
      {
        return new CounterModeCryptoTransform(aes, rgbKey, counter);
      }

      public override void GenerateIV()
      {
        // IV not needed in Counter Mode
      }

      public override void GenerateKey()
      {
        aes.GenerateKey();
      }

      private sealed class CounterModeCryptoTransform : ICryptoTransform
      {
        private static readonly byte[] rgbIV = new byte[16];
        private readonly byte[] counter;
        private readonly ICryptoTransform counterEncryptor;
        private readonly SymmetricAlgorithm symmetricAlgorithm;
        private readonly Queue<byte> xorMask = new Queue<byte>();

        public CounterModeCryptoTransform(SymmetricAlgorithm symmetricAlgorithm, byte[] key, byte[] counter)
        {
          this.symmetricAlgorithm = symmetricAlgorithm;
          this.counter = counter;
          counterEncryptor = symmetricAlgorithm.CreateEncryptor(key, rgbIV);
        }

        public bool CanReuseTransform { get { return false; } }
        public bool CanTransformMultipleBlocks { get { return true; } }

        public int InputBlockSize { get { return symmetricAlgorithm.BlockSize / 8; } }
        public int OutputBlockSize { get { return symmetricAlgorithm.BlockSize / 8; } }

        public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer,
          int outputOffset)
        {
          for (var i = 0; i < inputCount; i++) {
            if (NeedMoreXorMaskBytes()) {
              EncryptCounterThenIncrement();
            }

            var mask = xorMask.Dequeue();
            outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ mask);
          }

          return inputCount;
        }

        public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
        {
          var output = new byte[inputCount];
          TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
          return output;
        }

        public void Dispose()
        {
        }

        private void EncryptCounterThenIncrement()
        {
          var counterModeBlock = new byte[symmetricAlgorithm.BlockSize / 8];

          counterEncryptor.TransformBlock(counter, 0, counter.Length, counterModeBlock, 0);
          IncrementCounter();

          foreach (var b in counterModeBlock) {
            xorMask.Enqueue(b);
          }
        }

        private void IncrementCounter()
        {
          for (var i = counter.Length - 1; i >= 0; i--) {
            if (++counter[i] != 0) {
              break;
            }
          }
        }

        private bool NeedMoreXorMaskBytes()
        {
          return xorMask.Count == 0;
        }
      }
    }
  }
}