using Parquet.Schema;
using Parquet;
using Parquet.Data;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using AgGateway.ADAPT.ApplicationDataModel.LoggedData;

namespace AgGateway.ADAPT.StandardPlugin
{
    public class ADAPTParquetWriter
    {
        public static Dictionary<string, string> GeoParquetMetadata
        {
            get
            {
                return new Dictionary<string, string>
                {
                    ["geo"] =
                    "{\"version\":\"1.0.0\",\"primary_column\":\"geometry\",\"columns\":{\"geometry\":{\"encoding\":\"WKB\",\"geometry_types\":[\"Polygon\"]}}}"
                };
            }
        }

        public ADAPTParquetWriter(ADAPTColumnData columnData)
        {
            List<DataField> dataFields = new List<DataField>();
            if (columnData.Timestamps.Any())
            {
                dataFields.Add(new DataField<string>("timestamp"));
            }
            dataFields.AddRange(columnData.Columns.Select(n => new DataField<double?>(n.SrcName))); //TODO change the name to destination name
            dataFields.Add(new DataField<byte[]>("geometry"));
            Schema = new ParquetSchema(dataFields);
            ColumnData = columnData;
        }

        private ParquetSchema Schema { get; set; }
        private ADAPTColumnData ColumnData { get; set; }

        public async Task Write(string outputFile)
        {
            using (var fs = File.Create(outputFile))
            {
                using (ParquetWriter writer = await ParquetWriter.CreateAsync(Schema, fs))
                {
                    writer.CustomMetadata = GeoParquetMetadata;
                    using (ParquetRowGroupWriter rg = writer.CreateRowGroup())
                    {
                        int index = 0;
                        if (ColumnData.Timestamps.Any())
                        {
                            await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[index], ColumnData.Timestamps.Select(t => t.ToString("O", CultureInfo.InvariantCulture)).ToArray()));
                        }
                        foreach (var doubleColumn in ColumnData.Columns)
                        {
                            //TODO Convert here or elsewhere
                            await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], doubleColumn.Values.ToArray()));
                        }
                        await rg.WriteColumnAsync(new DataColumn(Schema.DataFields[++index], ColumnData.Geometries.ToArray()));
                    }
                }
            }
        }
    }

    public class ADAPTColumnData
    {
        public ADAPTColumnData(IEnumerable<NumericWorkingData> distinctNumericColumns)
        {
            Timestamps = new List<DateTime>();
            Columns = distinctNumericColumns.Select(x => new ADAPTDataColumn(x.Representation.Code, x.UnitOfMeasure.Code)).ToList();
            Geometries = new List<byte[]>();
        }
        public List<DateTime> Timestamps { get; set; }

        public List<ADAPTDataColumn> Columns { get; set; }

        public List<byte[]> Geometries { get; set; }
    }

    public class ADAPTDataColumn
    {
        public ADAPTDataColumn(string srcName, string uomCode)
        {
            SrcName = srcName;
            SrcUOMCode = uomCode;
            Values = new List<double?>();
        }
        public string SrcName { get; set; }

        public string SrcUOMCode { get; set; }

        public string TargetUOMCode { get; set; }

        public List<double?> Values { get; set; }
    }
}
