using System;
using System.Collections.Generic;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using RepresentationUnitSystem = AgGateway.ADAPT.Representation.UnitSystem;
using NetTopologySuite.Geometries;
using System.Security.Cryptography;
using System.Text;

namespace AgGateway.ADAPT.StandardPlugin
{
    internal static class Extensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> collection)
        {
            return collection == null || !collection.Any();
        }

        public static bool IsNullOrEmpty<T>(this List<T> collection)
        {
            return collection == null || collection.Count == 0;
        }

        public static bool EqualsIgnoreCase(this string source, string other)
        {
            return string.Equals(source, other, StringComparison.OrdinalIgnoreCase);
        }

        public static List<string> FilterEmptyValues(params string[] values)
        {
            return values.Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> items, int maxItems)
        {
            return items.Select((item, inx) => new { item, inx })
                        .GroupBy(x => x.inx / maxItems)
                        .Select(g => g.Select(x => x.item));
        }

        public static double? AsConvertedDouble(this NumericRepresentationValue value, string targetUnitCode)
        {
            if (value == null)
            {
                return null;
            }
            else if (value.Value.UnitOfMeasure == null)
            {
                return value.Value.Value; //Return the unconverted value
            }
            else
            {
                return value.Value.Value.ConvertValue(value.Value.UnitOfMeasure.Code, targetUnitCode);
            }
        }

        public static double ConvertValue(this double n, string srcUnitCode, string dstUnitCode)
        {
            RepresentationUnitSystem.UnitOfMeasure sourceUOM = RepresentationUnitSystem.InternalUnitSystemManager.Instance.UnitOfMeasures[srcUnitCode];
            RepresentationUnitSystem.UnitOfMeasure targetUOM = RepresentationUnitSystem.InternalUnitSystemManager.Instance.UnitOfMeasures[dstUnitCode];
            if (sourceUOM == null || targetUOM == null)
            {
                return n; //Return the unconverted value
            }
            else
            {
                RepresentationUnitSystem.UnitOfMeasureConverter converter = new RepresentationUnitSystem.UnitOfMeasureConverter();
                return converter.Convert(sourceUOM, targetUOM, n);
            }
        }

        public static Offset AsOffset(this DeviceElementConfiguration configuration)
        {
            if (configuration is SectionConfiguration sectionConfiguration)
            {
                return new Offset(sectionConfiguration.InlineOffset.AsConvertedDouble("m"),
                    sectionConfiguration.LateralOffset.AsConvertedDouble("m"),
                    0d);
            }
            else if (configuration is ImplementConfiguration implementConfiguration)
            {
                return new Offset(implementConfiguration.ControlPoint?.XOffset?.AsConvertedDouble("m") ?? 0d,
                    implementConfiguration.ControlPoint?.YOffset?.AsConvertedDouble("m") ?? 0d,
                    implementConfiguration.ControlPoint?.ZOffset?.AsConvertedDouble("m") ?? 0d);
            }
            else if (configuration is MachineConfiguration machineConfiguration)
            {
                return new Offset(-machineConfiguration.GpsReceiverXOffset.AsConvertedDouble("m") ?? 0d, //The GPS recevier inline offset is in the opposing direction
                    machineConfiguration.GpsReceiverYOffset.AsConvertedDouble("m") ?? 0d,
                    machineConfiguration.GpsReceiverZOffset.AsConvertedDouble("m") ?? 0d);
            }
            return new Offset(0d, 0d, 0d);
        }

        public static Point Destination(this Point point, double distanceM, double bearingDeg)
        {
            return GeometryExporter.HaversineDestination(point, distanceM, bearingDeg);
        }

        public static Coordinate AsCoordinate(this Point point)
        {
            return new Coordinate(point.X, point.Y);
        }

        public static Polygon AsCoveragePolygon(this Point leadingPoint, double width, ref LeadingEdge latestLeadingEdge, double bearing, double? reportedDistance)
        {
            double left = bearing - 90d % 360d;
            double right = bearing + 90d % 360d;
            double back = bearing - 180 % 360d;


            double wh = width / 2d;
            Point frontLeft = leadingPoint.Destination(wh, left);
            Point frontRight = leadingPoint.Destination(wh, right);
            Point backRight;
            Point backLeft;
            if (latestLeadingEdge != null)
            {
                backRight = latestLeadingEdge.Right;
                backLeft = latestLeadingEdge.Left;
            }
            else
            {
                double distance = 1; //1m as default without any other information (at start of data)
                if (reportedDistance != null)
                {
                    distance = reportedDistance.Value;
                }
                distance = distance > 4 ? 4 : distance; //keep distances sane

                backRight = frontRight.Destination(distance, back);
                backLeft = backRight.Destination(width, left);
            }
            latestLeadingEdge = new LeadingEdge(frontLeft, leadingPoint, frontRight);
            List<Coordinate> ringCoordinates = new List<Coordinate>()
            {
                frontLeft.AsCoordinate(),
                frontRight.AsCoordinate(),
                backRight.AsCoordinate(),
                backLeft.AsCoordinate(),
                frontLeft.AsCoordinate()
            };
            LinearRing exterior = new LinearRing(ringCoordinates.ToArray());
            return new Polygon(exterior);
        }
        public static string AsMD5Hash(this string input)
         {
            var bytes = ASCIIEncoding.ASCII.GetBytes(input);
            var hashBytes = new MD5CryptoServiceProvider().ComputeHash(bytes);
            int i;
            StringBuilder sOutput = new StringBuilder(hashBytes.Length);
            for (i=0;i < hashBytes.Length; i++)
            {
                sOutput.Append(hashBytes[i].ToString("X2"));
            }
            return sOutput.ToString();
         }
    }
}
