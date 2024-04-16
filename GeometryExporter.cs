using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using AdaptShapes = AgGateway.ADAPT.ApplicationDataModel.Shapes;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal static class GeometryExporter
    {
        private static LinearRing ConvertToLinerRing(AdaptShapes.LinearRing srcRing)
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

        internal static string ExportMultiPolygon(AdaptShapes.MultiPolygon srcMultiPolygon)
        {
            if (srcMultiPolygon == null || srcMultiPolygon.Polygons.IsNullOrEmpty())
            {
                return null;
            }

            var polygons = new List<Polygon>(srcMultiPolygon.Polygons.Count);
            foreach (var frameworkPolygon in srcMultiPolygon.Polygons)
            {
                var outerRing = ConvertToLinerRing(frameworkPolygon.ExteriorRing);
                var innerRings = frameworkPolygon.InteriorRings.Select(ConvertToLinerRing).ToArray();

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
    }
}
