﻿// <copyright file="ColorConversionTests.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp.Tests.Drawing
{
    using System.IO;
    using Xunit;

    using System.Numerics;
    using ImageSharp.Drawing.Shapes;

    public class SolidComplexPolygonTests : FileTestBase
    {
        [Fact]
        public void ImageShouldBeOverlayedByPolygonOutline()
        {
            string path = this.CreateOutputDirectory("Drawing", "ComplexPolygon");
            LinearPolygon simplePath = new LinearPolygon(
                            new Vector2(10, 10),
                            new Vector2(200, 150),
                            new Vector2(50, 300));

            LinearPolygon hole1 = new LinearPolygon(
                            new Vector2(37, 85),
                            new Vector2(93, 85),
                            new Vector2(65, 137));

            using (Image image = new Image(500, 500))
            {
                using (FileStream output = File.OpenWrite($"{path}/Simple.png"))
                {
                    image
                        .BackgroundColor(Color.Blue)
                        .Fill(Color.HotPink, new ComplexPolygon(simplePath, hole1))
                        .Save(output);
                }

                using (PixelAccessor<Color> sourcePixels = image.Lock())
                {
                    Assert.Equal(Color.HotPink, sourcePixels[11, 11]);

                    Assert.Equal(Color.HotPink, sourcePixels[200, 150]);

                    Assert.Equal(Color.HotPink, sourcePixels[50, 50]);

                    Assert.Equal(Color.HotPink, sourcePixels[35, 100]);

                    Assert.Equal(Color.Blue, sourcePixels[2, 2]);

                    //inside hole
                    Assert.Equal(Color.Blue, sourcePixels[57, 99]);
                }
            }
        }


        [Fact]
        public void ImageShouldBeOverlayedPolygonOutlineWithOverlap()
        {
            string path = this.CreateOutputDirectory("Drawing", "ComplexPolygon");
            LinearPolygon simplePath = new LinearPolygon(
                            new Vector2(10, 10),
                            new Vector2(200, 150),
                            new Vector2(50, 300));

            LinearPolygon hole1 = new LinearPolygon(
                            new Vector2(37, 85),
                            new Vector2(130, 40),
                            new Vector2(65, 137));

            using (Image image = new Image(500, 500))
            {
                using (FileStream output = File.OpenWrite($"{path}/SimpleOverlapping.png"))
                {
                    image
                        .BackgroundColor(Color.Blue)
                        .Fill(Color.HotPink, new ComplexPolygon(simplePath, hole1))
                        .Save(output);
                }

                using (PixelAccessor<Color> sourcePixels = image.Lock())
                {
                    Assert.Equal(Color.HotPink, sourcePixels[11, 11]);

                    Assert.Equal(Color.HotPink, sourcePixels[200, 150]);

                    Assert.Equal(Color.HotPink, sourcePixels[50, 50]);

                    Assert.Equal(Color.HotPink, sourcePixels[35, 100]);

                    Assert.Equal(Color.Blue, sourcePixels[2, 2]);

                    //inside hole
                    Assert.Equal(Color.Blue, sourcePixels[57, 99]);
                }
            }
        }

        [Fact]
        public void ImageShouldBeOverlayedPolygonOutlineWithOpacity()
        {
            string path = this.CreateOutputDirectory("Drawing", "ComplexPolygon");
            LinearPolygon simplePath = new LinearPolygon(
                            new Vector2(10, 10),
                            new Vector2(200, 150),
                            new Vector2(50, 300));

            LinearPolygon hole1 = new LinearPolygon(
                            new Vector2(37, 85),
                            new Vector2(93, 85),
                            new Vector2(65, 137));
            Color color = new Color(Color.HotPink.R, Color.HotPink.G, Color.HotPink.B, 150);

            using (Image image = new Image(500, 500))
            {
                using (FileStream output = File.OpenWrite($"{path}/Opacity.png"))
                {
                    image
                        .BackgroundColor(Color.Blue)
                        .Fill(color, new ComplexPolygon(simplePath, hole1))
                        .Save(output);
                }

                //shift background color towards forground color by the opacity amount
                Color mergedColor = new Color(Vector4.Lerp(Color.Blue.ToVector4(), Color.HotPink.ToVector4(), 150f / 255f));

                using (PixelAccessor<Color> sourcePixels = image.Lock())
                {
                    Assert.Equal(mergedColor, sourcePixels[11, 11]);

                    Assert.Equal(mergedColor, sourcePixels[200, 150]);

                    Assert.Equal(mergedColor, sourcePixels[50, 50]);

                    Assert.Equal(mergedColor, sourcePixels[35, 100]);

                    Assert.Equal(Color.Blue, sourcePixels[2, 2]);

                    //inside hole
                    Assert.Equal(Color.Blue, sourcePixels[57, 99]);
                }
            }
        }
    }
}