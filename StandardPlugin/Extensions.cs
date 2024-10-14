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
        private class UnitCodeRemap
        {
            public string SourceUnitCode;
            public string NewSourceUnitCode;
            public string TargetUnitCode;
        }
        private readonly static Dictionary<string, UnitCodeRemap> UnitOfMeasureMapping = new Dictionary<string, UnitCodeRemap>
        {
            // vrSeedRateSeeds... are mapped to AppliedCountPerArea... variables that use a different UoM domain.
            //'Seeds' is effectively the same as 'Count', so these unit conversions will suceed and be correct
            { "vrSeedRateSeedsTarget", new UnitCodeRemap { TargetUnitCode = "seeds1ha-1" } },
            { "vrSeedRateSeedsActual", new UnitCodeRemap { TargetUnitCode = "seeds1ha-1" } },
            { "vrSeedRateSeedsSetPoint", new UnitCodeRemap { TargetUnitCode = "seeds1ha-1" } },
            { "vrTotalSeedQuantityAppliedSeed", new UnitCodeRemap { TargetUnitCode = "seeds" } },
            { "vrSeedsProductivity", new UnitCodeRemap { TargetUnitCode = "seed1sec-1" } },
            // Some plugins by mistake assign "lb" to DownForce sensors instead of "lbf".
            // Here we fix this by replacing "lb" with "lbf"
            { "vrDownForceMargin", new UnitCodeRemap { SourceUnitCode = "lb", NewSourceUnitCode = "lbf" } },
            { "vrDownForceApplied", new UnitCodeRemap { SourceUnitCode = "lb", NewSourceUnitCode = "lbf" } },
        };

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

            var srcUnitCode = value.Value.UnitOfMeasure.Code;
            if (UnitOfMeasureMapping.TryGetValue(value.Representation.Code, out var unitCodeRemap))
            {
                if (!string.IsNullOrEmpty(unitCodeRemap.NewSourceUnitCode) && unitCodeRemap.SourceUnitCode == srcUnitCode)
                {
                    srcUnitCode = unitCodeRemap.NewSourceUnitCode;
                }
                if (!string.IsNullOrEmpty(unitCodeRemap.TargetUnitCode))
                {
                    targetUnitCode = unitCodeRemap.TargetUnitCode;
                }
            }

            return value.Value.Value.ConvertValue(srcUnitCode, targetUnitCode);
        }

        public static double ConvertValue(this double n, string srcUnitCode, string dstUnitCode)
        {
            RepresentationUnitSystem.UnitOfMeasure sourceUOM = RepresentationUnitSystem.InternalUnitSystemManager.Instance.UnitOfMeasures[srcUnitCode];
            RepresentationUnitSystem.UnitOfMeasure targetUOM = RepresentationUnitSystem.InternalUnitSystemManager.Instance.UnitOfMeasures[dstUnitCode];
            if (sourceUOM == null || targetUOM == null)
            {
                return n; //Return the unconverted value
            }

            RepresentationUnitSystem.UnitOfMeasureConverter converter = new RepresentationUnitSystem.UnitOfMeasureConverter();
            return converter.Convert(sourceUOM, targetUOM, n);
        }

        public static double WidthM(this DeviceElementConfiguration deviceElementConfiguration)
        {
            if (deviceElementConfiguration is SectionConfiguration sectionConfiguration)
            {
               return sectionConfiguration.SectionWidth?.AsConvertedDouble("m") ?? 0d;
            }
            else if (deviceElementConfiguration is ImplementConfiguration implementConfiguration)
            {
                return implementConfiguration.PhysicalWidth.AsConvertedDouble("m") ?? implementConfiguration.Width?.AsConvertedDouble("m") ?? 0d;
            }
            return 0d;
        }

        public static Offset AsOffset(this List<NumericRepresentationValue> offsets)
        {
            return new Offset(offsets.FirstOrDefault(x => x.Representation.Code == "vrInlineOffset")?.AsConvertedDouble("m") ?? 0d,
                offsets.FirstOrDefault(x => x.Representation.Code == "vrLateralOffset")?.AsConvertedDouble("m") ?? 0d,
                offsets.FirstOrDefault(x => x.Representation.Code == "vrVerticalOffset")?.AsConvertedDouble("m") ?? 0d);
        }

        public static Offset AsOffset(this ReferencePoint referencePoint)
        {
            return new Offset(referencePoint.XOffset?.AsConvertedDouble("m") ?? 0d,
                referencePoint.YOffset?.AsConvertedDouble("m") ?? 0d,
                referencePoint.ZOffset?.AsConvertedDouble("m") ?? 0d);
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
                if (implementConfiguration.ControlPoint != null)
                {
                    return implementConfiguration.ControlPoint.AsOffset();
                }
                else if (implementConfiguration.Offsets.Any())
                {
                    return implementConfiguration.Offsets.AsOffset();
                }
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
