// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.Memory;

namespace SixLabors.ImageSharp.Formats.Tga
{
    /// <summary>
    /// Image encoder for writing an image to a stream as a truevision targa image.
    /// </summary>
    internal sealed class TgaEncoderCore
    {
        /// <summary>
        /// Used for allocating memory during processing operations.
        /// </summary>
        private readonly MemoryAllocator memoryAllocator;

        /// <summary>
        /// The global configuration.
        /// </summary>
        private Configuration configuration;

        /// <summary>
        /// Reusable buffer for writing data.
        /// </summary>
        private readonly byte[] buffer = new byte[2];

        /// <summary>
        /// The color depth, in number of bits per pixel.
        /// </summary>
        private TgaBitsPerPixel? bitsPerPixel;

        /// <summary>
        /// Indicates if run length compression should be used.
        /// </summary>
        private readonly TgaCompression compression;

        /// <summary>
        /// Initializes a new instance of the <see cref="TgaEncoderCore"/> class.
        /// </summary>
        /// <param name="options">The encoder options.</param>
        /// <param name="memoryAllocator">The memory manager.</param>
        public TgaEncoderCore(ITgaEncoderOptions options, MemoryAllocator memoryAllocator)
        {
            this.memoryAllocator = memoryAllocator;
            this.bitsPerPixel = options.BitsPerPixel;
            this.compression = options.Compression;
        }

        /// <summary>
        /// Encodes the image to the specified stream from the <see cref="ImageFrame{TPixel}"/>.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="image">The <see cref="ImageFrame{TPixel}"/> to encode from.</param>
        /// <param name="stream">The <see cref="Stream"/> to encode the image data to.</param>
        public void Encode<TPixel>(Image<TPixel> image, Stream stream)
            where TPixel : struct, IPixel<TPixel>
        {
            Guard.NotNull(image, nameof(image));
            Guard.NotNull(stream, nameof(stream));

            this.configuration = image.GetConfiguration();
            ImageMetadata metadata = image.Metadata;
            TgaMetadata tgaMetadata = metadata.GetTgaMetadata();
            this.bitsPerPixel = this.bitsPerPixel ?? tgaMetadata.BitsPerPixel;

            TgaImageType imageType = this.compression is TgaCompression.RunLength ? TgaImageType.RleTrueColor : TgaImageType.TrueColor;
            if (this.bitsPerPixel == TgaBitsPerPixel.Pixel8)
            {
                imageType = this.compression is TgaCompression.RunLength ? TgaImageType.RleBlackAndWhite : TgaImageType.BlackAndWhite;
            }

            // If compression is used, set bit 5 of the image descriptor to indicate an left top origin.
            byte imageDescriptor = (byte)(this.compression is TgaCompression.RunLength ? 32 : 0);

            var fileHeader = new TgaFileHeader(
                idLength: 0,
                colorMapType: 0,
                imageType: imageType,
                cMapStart: 0,
                cMapLength: 0,
                cMapDepth: 0,
                xOffset: 0,
                yOffset: this.compression is TgaCompression.RunLength ? (short)image.Height : (short)0, // When run length encoding is used, the origin should be top left instead of the default bottom left.
                width: (short)image.Width,
                height: (short)image.Height,
                pixelDepth: (byte)this.bitsPerPixel.Value,
                imageDescriptor: imageDescriptor);

            Span<byte> buffer = stackalloc byte[TgaFileHeader.Size];
            fileHeader.WriteTo(buffer);

            stream.Write(buffer, 0, TgaFileHeader.Size);

            if (this.compression is TgaCompression.RunLength)
            {
                this.WriteRunLengthEndcodedImage(stream, image.Frames.RootFrame);
            }
            else
            {
                this.WriteImage(stream, image.Frames.RootFrame);
            }

            stream.Flush();
        }

        /// <summary>
        /// Writes the pixel data to the binary stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="image">
        /// The <see cref="ImageFrame{TPixel}"/> containing pixel data.
        /// </param>
        private void WriteImage<TPixel>(Stream stream, ImageFrame<TPixel> image)
            where TPixel : struct, IPixel<TPixel>
        {
            Buffer2D<TPixel> pixels = image.PixelBuffer;
            switch (this.bitsPerPixel)
            {
                case TgaBitsPerPixel.Pixel8:
                    this.Write8Bit(stream, pixels);
                    break;

                case TgaBitsPerPixel.Pixel16:
                    this.Write16Bit(stream, pixels);
                    break;

                case TgaBitsPerPixel.Pixel24:
                    this.Write24Bit(stream, pixels);
                    break;

                case TgaBitsPerPixel.Pixel32:
                    this.Write32Bit(stream, pixels);
                    break;
            }
        }

        /// <summary>
        /// Writes a run length encoded tga image to the stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel type.</typeparam>
        /// <param name="stream">The stream to write the image to.</param>
        /// <param name="image">The image to encode.</param>
        private void WriteRunLengthEndcodedImage<TPixel>(Stream stream, ImageFrame<TPixel> image)
            where TPixel : struct, IPixel<TPixel>
        {
            Rgba32 color = default;
            Buffer2D<TPixel> pixels = image.PixelBuffer;
            Span<TPixel> pixelSpan = pixels.GetSpan();
            int totalPixels = image.Width * image.Height;
            int encodedPixels = 0;
            while (encodedPixels < totalPixels)
            {
                TPixel currentPixel = pixelSpan[encodedPixels];
                currentPixel.ToRgba32(ref color);
                byte equalPixelCount = this.FindEqualPixels(pixelSpan.Slice(encodedPixels));

                // Write the number of equal pixels, with the high bit set, indicating ist a compressed pixel run.
                stream.WriteByte((byte)(equalPixelCount | 128));
                switch (this.bitsPerPixel)
                {
                    case TgaBitsPerPixel.Pixel8:
                        int luminance = GetLuminance(currentPixel);
                        stream.WriteByte((byte)luminance);
                        break;

                    case TgaBitsPerPixel.Pixel16:
                        var bgra5551 = new Bgra5551(color.ToVector4());
                        BinaryPrimitives.TryWriteInt16LittleEndian(this.buffer, (short)bgra5551.PackedValue);
                        stream.WriteByte(this.buffer[0]);
                        stream.WriteByte(this.buffer[1]);

                        break;

                    case TgaBitsPerPixel.Pixel24:
                        stream.WriteByte(color.B);
                        stream.WriteByte(color.G);
                        stream.WriteByte(color.R);
                        break;

                    case TgaBitsPerPixel.Pixel32:
                        stream.WriteByte(color.B);
                        stream.WriteByte(color.G);
                        stream.WriteByte(color.R);
                        stream.WriteByte(color.A);
                        break;
                }

                encodedPixels += equalPixelCount + 1;
            }
        }

        /// <summary>
        /// Finds consecutive pixels, which have the same value starting from the pixel span offset 0.
        /// </summary>
        /// <typeparam name="TPixel">The pixel type.</typeparam>
        /// <param name="pixelSpan">The pixel span to search in.</param>
        /// <returns>The number of equal pixels.</returns>
        private byte FindEqualPixels<TPixel>(Span<TPixel> pixelSpan)
            where TPixel : struct, IPixel<TPixel>
        {
            int idx = 0;
            byte equalPixelCount = 0;
            while (equalPixelCount < 127 && idx < pixelSpan.Length - 1)
            {
                TPixel currentPixel = pixelSpan[idx];
                TPixel nextPixel = pixelSpan[idx + 1];
                if (currentPixel.Equals(nextPixel))
                {
                    equalPixelCount++;
                }
                else
                {
                    return equalPixelCount;
                }

                idx++;
            }

            return equalPixelCount;
        }

        private IManagedByteBuffer AllocateRow(int width, int bytesPerPixel) => this.memoryAllocator.AllocatePaddedPixelRowBuffer(width, bytesPerPixel, 0);

        /// <summary>
        /// Writes the 8bit pixels uncompressed to the stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="pixels">The <see cref="Buffer2D{TPixel}"/> containing pixel data.</param>
        private void Write8Bit<TPixel>(Stream stream, Buffer2D<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            using (IManagedByteBuffer row = this.AllocateRow(pixels.Width, 1))
            {
                for (int y = pixels.Height - 1; y >= 0; y--)
                {
                    Span<TPixel> pixelSpan = pixels.GetRowSpan(y);
                    PixelOperations<TPixel>.Instance.ToL8Bytes(
                        this.configuration,
                        pixelSpan,
                        row.GetSpan(),
                        pixelSpan.Length);
                    stream.Write(row.Array, 0, row.Length());
                }
            }
        }

        /// <summary>
        /// Writes the 16bit pixels uncompressed to the stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="pixels">The <see cref="Buffer2D{TPixel}"/> containing pixel data.</param>
        private void Write16Bit<TPixel>(Stream stream, Buffer2D<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            using (IManagedByteBuffer row = this.AllocateRow(pixels.Width, 2))
            {
                for (int y = pixels.Height - 1; y >= 0; y--)
                {
                    Span<TPixel> pixelSpan = pixels.GetRowSpan(y);
                    PixelOperations<TPixel>.Instance.ToBgra5551Bytes(
                        this.configuration,
                        pixelSpan,
                        row.GetSpan(),
                        pixelSpan.Length);
                    stream.Write(row.Array, 0, row.Length());
                }
            }
        }

        /// <summary>
        /// Writes the 24bit pixels uncompressed to the stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="pixels">The <see cref="Buffer2D{TPixel}"/> containing pixel data.</param>
        private void Write24Bit<TPixel>(Stream stream, Buffer2D<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            using (IManagedByteBuffer row = this.AllocateRow(pixels.Width, 3))
            {
                for (int y = pixels.Height - 1; y >= 0; y--)
                {
                    Span<TPixel> pixelSpan = pixels.GetRowSpan(y);
                    PixelOperations<TPixel>.Instance.ToBgr24Bytes(
                        this.configuration,
                        pixelSpan,
                        row.GetSpan(),
                        pixelSpan.Length);
                    stream.Write(row.Array, 0, row.Length());
                }
            }
        }

        /// <summary>
        /// Writes the 32bit pixels uncompressed to the stream.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The <see cref="Stream"/> to write to.</param>
        /// <param name="pixels">The <see cref="Buffer2D{TPixel}"/> containing pixel data.</param>
        private void Write32Bit<TPixel>(Stream stream, Buffer2D<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            using (IManagedByteBuffer row = this.AllocateRow(pixels.Width, 4))
            {
                for (int y = pixels.Height - 1; y >= 0; y--)
                {
                    Span<TPixel> pixelSpan = pixels.GetRowSpan(y);
                    PixelOperations<TPixel>.Instance.ToBgra32Bytes(
                        this.configuration,
                        pixelSpan,
                        row.GetSpan(),
                        pixelSpan.Length);
                    stream.Write(row.Array, 0, row.Length());
                }
            }
        }

        /// <summary>
        /// Convert the pixel values to grayscale using ITU-R Recommendation BT.709.
        /// </summary>
        /// <param name="sourcePixel">The pixel to get the luminance from.</param>
        [MethodImpl(InliningOptions.ShortMethod)]
        public static int GetLuminance<TPixel>(TPixel sourcePixel)
            where TPixel : struct, IPixel<TPixel>
        {
            var vector = sourcePixel.ToVector4();
            return ImageMaths.GetBT709Luminance(ref vector, 256);
        }
    }
}
