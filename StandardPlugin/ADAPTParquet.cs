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
    internal class ADAPTParquetWriter
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

        public ADAPTParquetWriter(ADAPTParquetColumnData columnData)
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
        private ADAPTParquetColumnData ColumnData { get; set; }

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

    internal class ADAPTParquetColumnData
    {
        public ADAPTParquetColumnData(IEnumerable<NumericWorkingData> distinctNumericColumns, CommonExporters commonExporters)
        {
            Timestamps = new List<DateTime>();
            Columns = distinctNumericColumns.Select(x => new ADAPTDataColumn(x, commonExporters)).ToList();
            Geometries = new List<byte[]>();
        }
        public List<DateTime> Timestamps { get; set; }

        public List<ADAPTDataColumn> Columns { get; set; }

        public List<byte[]> Geometries { get; set; }
    }

    internal class ADAPTDataColumn
    {
        public ADAPTDataColumn(NumericWorkingData numericWorkingData, CommonExporters commonExporters)
        {
            SrcObject = numericWorkingData;
            SrcName = numericWorkingData.Representation.Code;
            SrcUOMCode = numericWorkingData.UnitOfMeasure.Code;
            TargetName = commonExporters.TypeMappings[numericWorkingData.Representation.Code];
            TargetUOMCode = commonExporters.StandardDataTypes.Definitions.First(x => x.DefinitionCode == TargetName).NumericDataTypeDefinitionAttributes.UnitOfMeasureCode;
            Values = new List<double?>();
        }
        public string SrcName { get; set; }

        public string SrcUOMCode { get; set; }

        public string TargetUOMCode { get; set; }

        public string TargetName { get; set; }

        public NumericWorkingData SrcObject { get; set; }

        public List<double?> Values { get; set; }
    }
}
