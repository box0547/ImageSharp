// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.Formats.Jpeg.Components;
using SixLabors.ImageSharp.Formats.Jpeg.Components.Encoder;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Colorspaces.Conversion;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Jpg
{
    public class RgbToYCbCrConverterTests
    {
        public RgbToYCbCrConverterTests(ITestOutputHelper output)
        {
            this.Output = output;
        }

        private ITestOutputHelper Output { get; }

        [Fact]
        public void TestConverterLut444()
        {
            int dataSize = 8 * 8;
            Rgb24[] data = CreateTestData(dataSize);
            var target = RgbToYCbCrConverterLut.Create();

            Block8x8F y = default;
            Block8x8F cb = default;
            Block8x8F cr = default;

            target.Convert444(data.AsSpan(), ref y, ref cb, ref cr);

            Verify444(data, ref y, ref cb, ref cr, new ApproximateColorSpaceComparer(1F));
        }

        [Fact]
        public void TestConverterVectorized444()
        {
            if (!RgbToYCbCrConverterVectorized.IsSupported)
            {
                this.Output.WriteLine("No AVX and/or FMA present, skipping test!");
                return;
            }

            int dataSize = 8 * 8;
            Rgb24[] data = CreateTestData(dataSize);

            Block8x8F y = default;
            Block8x8F cb = default;
            Block8x8F cr = default;

            RgbToYCbCrConverterVectorized.Convert444(data.AsSpan(), ref y, ref cb, ref cr);

            Verify444(data, ref y, ref cb, ref cr, new ApproximateColorSpaceComparer(0.0001F));
        }

        [Fact]
        public void TestConverterLut420()
        {
            int dataSize = 16 * 16;
            Span<Rgb24> data = CreateTestData(dataSize).AsSpan();
            var target = RgbToYCbCrConverterLut.Create();

            var yBlocks = new Block8x8F[4];
            var cb = default(Block8x8F);
            var cr = default(Block8x8F);

            target.Convert420(data, ref yBlocks[0], ref yBlocks[1], ref cb, ref cr, 0);
            target.Convert420(data.Slice(16 * 8), ref yBlocks[2], ref yBlocks[3], ref cb, ref cr, 1);

            Verify420(data, yBlocks, ref cb, ref cr, new ApproximateFloatComparer(1F));
        }

        [Fact]
        public void TestConverterVectorized420()
        {
            if (!RgbToYCbCrConverterVectorized.IsSupported)
            {
                this.Output.WriteLine("No AVX and/or FMA present, skipping test!");
                return;
            }

            int dataSize = 16 * 16;
            Span<Rgb24> data = CreateTestData(dataSize).AsSpan();

            var yBlocks = new Block8x8F[4];
            var cb = default(Block8x8F);
            var cr = default(Block8x8F);

            RgbToYCbCrConverterVectorized.Convert420_16x8(data, ref yBlocks[0], ref yBlocks[1], ref cb, ref cr, 0);
            RgbToYCbCrConverterVectorized.Convert420_16x8(data.Slice(16 * 8), ref yBlocks[2], ref yBlocks[3], ref cb, ref cr, 1);

            Verify420(data, yBlocks, ref cb, ref cr, new ApproximateFloatComparer(1F));
        }


        private static void Verify444(
            ReadOnlySpan<Rgb24> data,
            ref Block8x8F yResult,
            ref Block8x8F cbResult,
            ref Block8x8F crResult,
            ApproximateColorSpaceComparer comparer)
        {
            Block8x8F y = default;
            Block8x8F cb = default;
            Block8x8F cr = default;

            RgbToYCbCr(data, ref y, ref cb, ref cr);

            for (int i = 0; i < Block8x8F.Size; i++)
            {
                Assert.True(comparer.Equals(new YCbCr(y[i], cb[i], cr[i]), new YCbCr(yResult[i], cbResult[i], crResult[i])), $"Pos {i}, Expected {y[i]} == {yResult[i]}, {cb[i]} == {cbResult[i]}, {cr[i]} == {crResult[i]}");
            }
        }

        private static void Verify420(
            ReadOnlySpan<Rgb24> data,
            Block8x8F[] yResult,
            ref Block8x8F cbResult,
            ref Block8x8F crResult,
            ApproximateFloatComparer comparer)
        {
            var tempBlock = default(Block8x8F);
            var cbTrue = new Block8x8F[4];
            var crTrue = new Block8x8F[4];

            Span<Rgb24> tempData = new Rgb24[8 * 8].AsSpan();

            // top left
            Copy8x8(data, tempData);
            RgbToYCbCr(tempData, ref tempBlock, ref cbTrue[0], ref crTrue[0]);
            VerifyBlock(ref yResult[0], ref tempBlock, comparer);

            // top right
            Copy8x8(data.Slice(8), tempData);
            RgbToYCbCr(tempData, ref tempBlock, ref cbTrue[1], ref crTrue[1]);
            VerifyBlock(ref yResult[1], ref tempBlock, comparer);

            // bottom left
            Copy8x8(data.Slice(8 * 16), tempData);
            RgbToYCbCr(tempData, ref tempBlock, ref cbTrue[2], ref crTrue[2]);
            VerifyBlock(ref yResult[2], ref tempBlock, comparer);

            // bottom right
            Copy8x8(data.Slice((8 * 16) + 8), tempData);
            RgbToYCbCr(tempData, ref tempBlock, ref cbTrue[3], ref crTrue[3]);
            VerifyBlock(ref yResult[3], ref tempBlock, comparer);

            // verify Cb
            Scale16X16To8X8(ref tempBlock, cbTrue);
            VerifyBlock(ref cbResult, ref tempBlock, comparer);

            // verify Cr
            Scale16X16To8X8(ref tempBlock, crTrue);
            VerifyBlock(ref crResult, ref tempBlock, comparer);


            // extracts 8x8 blocks from 16x8 memory region
            static void Copy8x8(ReadOnlySpan<Rgb24> source, Span<Rgb24> dest)
            {
                for (int i = 0; i < 8; i++)
                {
                    source.Slice(i * 16, 8).CopyTo(dest.Slice(i * 8));
                }
            }

            // scales 16x16 to 8x8, used in chroma subsampling tests
            static void Scale16X16To8X8(ref Block8x8F dest, ReadOnlySpan<Block8x8F> source)
            {
                for (int i = 0; i < 4; i++)
                {
                    int dstOff = ((i & 2) << 4) | ((i & 1) << 2);
                    Block8x8F iSource = source[i];

                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int j = (16 * y) + (2 * x);
                            float sum = iSource[j] + iSource[j + 1] + iSource[j + 8] + iSource[j + 9];
                            dest[(8 * y) + x + dstOff] = (sum + 2) * .25F;
                        }
                    }
                }
            }
        }

        private static void RgbToYCbCr(ReadOnlySpan<Rgb24> data, ref Block8x8F y, ref Block8x8F cb, ref Block8x8F cr)
        {
            for (int i = 0; i < data.Length; i++)
            {
                int r = data[i].R;
                int g = data[i].G;
                int b = data[i].B;

                y[i] = (0.299F * r) + (0.587F * g) + (0.114F * b);
                cb[i] = 128F + ((-0.168736F * r) - (0.331264F * g) + (0.5F * b));
                cr[i] = 128F + ((0.5F * r) - (0.418688F * g) - (0.081312F * b));
            }
        }

        private static void VerifyBlock(ref Block8x8F res, ref Block8x8F target, ApproximateFloatComparer comparer)
        {
            for (int i = 0; i < Block8x8F.Size; i++)
            {
                Assert.True(comparer.Equals(res[i], target[i]), $"Pos {i}, Expected: {target[i]}, Got: {res[i]}");
            }
        }

        private static Rgb24[] CreateTestData(int size)
        {
            var data = new Rgb24[size];
            var r = new Random();

            var random = new byte[3];
            for (int i = 0; i < data.Length; i++)
            {
                r.NextBytes(random);
                data[i] = new Rgb24(random[0], random[1], random[2]);
            }

            return data;
        }
    }
}
