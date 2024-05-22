using System;
using System.Collections.Generic;
using System.Linq;

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
    }
}
