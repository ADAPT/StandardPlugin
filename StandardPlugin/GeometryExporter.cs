using AgGateway.ADAPT.Standard;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using AdaptShapes = AgGateway.ADAPT.ApplicationDataModel.Shapes;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal static class GeometryExporter
    {
        internal const double EarthRadiusM = 6378137;

        private static LinearRing ConvertToLinearRing(AdaptShapes.LinearRing srcRing)
        {
            if (srcRing == null || srcRing.Points.IsNullOrEmpty())
            {
                return null;
            }

            var coordinates = new List<CoordinateZ>(srcRing.Points.Count);
            foreach (var frameworkPoint in srcRing.Points)
            {
                var coordinate = new CoordinateZ(frameworkPoint.X, frameworkPoint.Y)
                {
                    Z = frameworkPoint.Z ?? Coordinate.NullOrdinate
                };
                coordinates.Add(coordinate);
            }
            if (coordinates.First() != coordinates.Last())
            {
                coordinates.Add(coordinates.First());
            }
            return new LinearRing(coordinates.ToArray());
        }

        private static LineString ConvertToLineString(AdaptShapes.LineString srcLine)
        {
            var coordinates = new List<CoordinateZ>();
            foreach (var frameworkLine in srcLine.Points)
            {
                var coordinate = new CoordinateZ(frameworkLine.X, frameworkLine.Y)
                {
                    Z = frameworkLine.Z ?? Coordinate.NullOrdinate
                };
                coordinates.Add(coordinate);
            }

            return new LineString(coordinates.ToArray());
        }

        private static LinearRing ConvertShapeToLinearRing(AdaptShapes.Shape srcShape)
        {
            if (srcShape is AdaptShapes.LinearRing srcLinearRing)
            {
                return ConvertToLinearRing(srcLinearRing);
            }

            return null;
        }

        internal static string ExportMultiPolygon(AdaptShapes.MultiPolygon srcMultiPolygon, IEnumerable<AdaptShapes.Shape> srcInteriorAttributes = null)
        {
            if (srcMultiPolygon == null || srcMultiPolygon.Polygons.IsNullOrEmpty())
            {
                return null;
            }

            var polygons = new List<Polygon>(srcMultiPolygon.Polygons.Count);
            foreach (var frameworkPolygon in srcMultiPolygon.Polygons)
            {
                var outerRing = ConvertToLinearRing(frameworkPolygon.ExteriorRing);
                var innerRings = frameworkPolygon.InteriorRings.Select(ConvertToLinearRing).ToArray();

                if (polygons.Count == 0 && srcInteriorAttributes != null)
                {
                    innerRings = innerRings.Concat(srcInteriorAttributes.Select(ConvertShapeToLinearRing).Where(x => x != null)).ToArray();
                }

                polygons.Add(new Polygon(outerRing, innerRings));
            }

            return new MultiPolygon(polygons.ToArray()).ToText();
        }

        internal static string ExportLineString(AdaptShapes.LineString srcLine)
        {
            if (srcLine == null || srcLine.Points.IsNullOrEmpty())
            {
                return null;
            }

            return ConvertToLineString(srcLine).ToText();
        }

        internal static string ExportPoint(AdaptShapes.Point srcPoint)
        {
            if (srcPoint == null)
            {
                return null;
            }

            var point = new Point(srcPoint.X, srcPoint.Y, srcPoint.Z ?? Coordinate.NullOrdinate);
            return point.ToText();
        }

        internal static string ExportLineStrings(List<AdaptShapes.LineString> srcLineStrings)
        {
            if (srcLineStrings.IsNullOrEmpty())
            {
                return null;
            }

            return new MultiLineString(srcLineStrings.Select(ConvertToLineString).ToArray()).ToText();
        }

        internal static void ThreePointsToCenterRadius(AdaptShapes.Point srcPoint1, AdaptShapes.Point srcPoint2, AdaptShapes.Point srcPoint3,
            out string centerPoint, out Radius radius)
        {
            if (srcPoint1 == null || srcPoint2 == null || srcPoint3 == null)
            {
                centerPoint = null;
                radius = null;
                return;
            }

            // Get the perpendicular bisector of Point1 and Point2
            var x1 = (srcPoint2.X + srcPoint1.X) / 2;
            var y1 = (srcPoint2.Y + srcPoint1.Y) / 2;
            var dy1 = srcPoint2.X - srcPoint1.Y;
            var dx1 = -(srcPoint2.Y - srcPoint1.Y);

            // Get the perpendicular bisector of Point2 and Point3
            var x2 = (srcPoint3.X + srcPoint2.X) / 2;
            var y2 = (srcPoint3.Y + srcPoint2.Y) / 2;
            var dy2 = srcPoint3.X - srcPoint2.Y;
            var dx2 = -(srcPoint3.Y - srcPoint2.Y);

            // Check if lines intersect
            var intersectionPoint = FindIntersection(x1, y1, x1 + dx1, y1 + dy1, x2, y2, x2 + dx2, y2 + dy2);
            if (intersectionPoint == null)
            {
                centerPoint = null;
                radius = null;
            }

            centerPoint = ExportPoint(intersectionPoint);

            var dx = intersectionPoint.X - srcPoint1.X;
            var dy = intersectionPoint.Y - srcPoint1.Y;
            radius = new Radius
            {
                NumericValue = Math.Sqrt(dx * dx + dy * dy)
            };
        }

        private static AdaptShapes.Point FindIntersection(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
        {
            var dx12 = x2 - x1;
            var dy12 = y2 - y1;
            var dx34 = x4 - x3;
            var dy34 = y4 - y3;

            var denominator = dy12 * dx34 - dx12 * dy34;
            var t1 = ((x1 - x3) * dy34 + (y3 - y1) * dx34) / denominator;
            if (double.IsInfinity(t1))
            {
                return null;
            }

            return new AdaptShapes.Point
            {
                X = x1 + dx12 * t1,
                Y = y1 + dy12 * t1
            };
        }

        internal static Point HaversineDestination(Point startPoint, double distanceM, double bearingDeg)
        {
            double latRad = DegreesToRads(startPoint.Y);
            double lonRad = DegreesToRads(startPoint.X);
            double bearingRad = DegreesToRads(bearingDeg);
            double length = distanceM / EarthRadiusM;
            double otherLat = Math.Asin(Math.Sin(latRad) *
                                        Math.Cos(length) +
                                        Math.Cos(latRad) *
                                        Math.Sin(length) *
                                        Math.Cos(bearingRad));
            double otherLon = lonRad +
                              Math.Atan2(
                                   Math.Sin(bearingRad) *
                                   Math.Sin(length) *
                                   Math.Cos(latRad),
                                   Math.Cos(length) -
                                   Math.Sin(latRad) *
                                   Math.Sin(otherLat)
                                  );
            return new Point(RadsToDegrees(otherLon), RadsToDegrees(otherLat));
        }

        internal static double DegreesToRads(double d)
        {
            return d * (Math.PI / 180d);
        }

        internal static double RadsToDegrees(double r)
        {
            return r * (180d / Math.PI);
        }

        internal static double HaversineDistance(Point point1, Point point2)
        {
            double latRad1 = DegreesToRads(point1.Y);
            double latRad2 = DegreesToRads(point2.Y);
            double deltaLat = DegreesToRads(point2.Y - point1.Y);
            double deltaLon = DegreesToRads(point2.X - point1.X);
            double a = Math.Pow(Math.Sin(deltaLat / 2d), 2) + Math.Cos(latRad1) * Math.Cos(latRad2) * Math.Pow(Math.Sin(deltaLon / 2d), 2);
            double c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return c * EarthRadiusM;
        }

        internal static double HaversineBearing(Point point1, Point point2)
        {
            double latRad1 = DegreesToRads(point1.Y);
            double latRad2 = DegreesToRads(point2.Y);
            double lonRad1 = DegreesToRads(point1.X);
            double lonRad2 = DegreesToRads(point2.X);
            double a = Math.Sin(lonRad2 - lonRad1) * Math.Cos(latRad2);
            double b = Math.Cos(latRad1) * Math.Sin(latRad2) - Math.Sin(latRad1) * Math.Cos(latRad2) * Math.Cos(lonRad2 - lonRad1);
            double rad = Math.Atan2(a, b);
            return RadsToDegrees(rad);
        }
    }
}
