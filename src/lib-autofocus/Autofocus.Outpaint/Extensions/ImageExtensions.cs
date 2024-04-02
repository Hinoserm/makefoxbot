using System.Numerics;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Autofocus.Outpaint.Extensions
{
    internal static class ImageExtensions
    {
        public static void Bleed(this Image<Rgba32> image, Rectangle from, int radius, int? seed, float angleFactor = 0.5f)
        {
            var rng = new Random(seed ?? RandomNumberGenerator.GetInt32(int.MaxValue));

            for (var i = 0; i < image.Width; i++)
            {
                for (var j = 0; j < image.Height; j++)
                {
                    if (from.Contains(i, j))
                        continue;

                    // Get the distance to the closest point on the rectangle
                    var closestPoint = from.ClosestPoint(i, j).ToVector2();
                    var distance = Vector2.Distance(new Vector2(i, j), closestPoint);

                    // Select another nearby point on the rectangle border (more randomness with distance)
                    var nearbyPoint = from.ClosestPoint(new Point(
                        i + (int)(rng.NextSingle(-distance, distance) * angleFactor),
                        j + (int)(rng.NextSingle(-distance, distance) * angleFactor)
                    ));
                    var selectedPixel = image[nearbyPoint.X, nearbyPoint.Y].ToVector4();

                    // Randomize colour slightly (based on distance). Once the distance is > radius this add 100% randomisation.
                    // Note that even then the colour is still biased by the base colour.
                    var distanceFactor = Math.Clamp(distance / radius, 0, 1);
                    selectedPixel += new Vector4(
                        rng.NextSingle(-1f, 1f) * distanceFactor,
                        rng.NextSingle(-1f, 1f) * distanceFactor,
                        rng.NextSingle(-1f, 1f) * distanceFactor,
                        0
                    );

                    image[i, j] = new Rgba32(selectedPixel);
                }
            }
        }

        private static Point ClosestPoint(this Rectangle rectangle, Point point)
        {
            return ClosestPoint(rectangle, point.X, point.Y);
        }

        private static Point ClosestPoint(this Rectangle rectangle, int x, int y)
        {
            return new Point
            {
                X = Math.Clamp(x, rectangle.Left, rectangle.Right),
                Y = Math.Clamp(y, rectangle.Top, rectangle.Bottom)
            };
        }

        private static Vector2 ToVector2(this Point point)
        {
            return new Vector2(point.X, point.Y);
        }

        private static float NextSingle(this Random random, float minInclusive, float maxInclusive)
        {
            var range = maxInclusive - minInclusive;
            return random.NextSingle() * range + minInclusive;
        }
    }
}
