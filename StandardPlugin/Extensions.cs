using System;
using System.Collections.Generic;
using System.Linq;
using AgGateway.ADAPT.ApplicationDataModel.Equipment;
using AgGateway.ADAPT.ApplicationDataModel.Representations;
using RepresentationUnitSystem = AgGateway.ADAPT.Representation.UnitSystem;

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
    }
}
